"""Read PE headers to reject CET opt-in on the two Windows entry executables."""
from pathlib import Path
import datetime
import hashlib
import json
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
ENTRIES = [
    'LocalServer/WindowsLatest/Bnsm.LocalServer.exe',
    'UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe',
]


def inspect_pe(path):
    data = path.read_bytes()
    assert data[:2] == b'MZ', str(path)
    pe = struct.unpack_from('<I', data, 60)[0]
    assert data[pe:pe + 4] == b'PE\0\0', str(path)
    machine, section_count = struct.unpack_from('<HH', data, pe + 4)
    optional_size = struct.unpack_from('<H', data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from('<H', data, optional)[0]
    assert machine == 0x8664 and magic == 0x20B, 'Expected Windows x64 PE: ' + str(path)
    directory_count = struct.unpack_from('<I', data, optional + 108)[0]
    sections = []
    for i in range(section_count):
        entry = optional + optional_size + 40 * i
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from('<IIII', data, entry + 8)
        sections.append((virtual_address, max(virtual_size, raw_size), raw_pointer))

    def rva_offset(rva):
        for address, size, pointer in sections:
            if address <= rva < address + size:
                return pointer + rva - address
        raise ValueError('Unmapped debug directory: ' + str(path))

    flags = []
    if directory_count > 6:
        debug_rva, debug_size = struct.unpack_from('<II', data, optional + 112 + 6 * 8)
        if debug_rva:
            assert debug_size % 28 == 0, 'Invalid PE debug table: ' + str(path)
            for offset in range(rva_offset(debug_rva), rva_offset(debug_rva) + debug_size, 28):
                record = struct.unpack_from('<IIHHIIII', data, offset)
                if record[4] == 20:  # IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS
                    assert record[5] >= 4, 'Invalid extended characteristics: ' + str(path)
                    flags.append(struct.unpack_from('<I', data, record[7])[0])
    return {
        'path': str(path),
        'sha256': hashlib.sha256(data).hexdigest(),
        'machine': 'x64',
        'extendedDllCharacteristics': flags,
        'cetCompatible': any(value & 1 for value in flags),
    }


def verify(package):
    entries = [inspect_pe(package / relative) for relative in ENTRIES]
    report = {
        'passed': all(not entry['cetCompatible'] for entry in entries),
        'verifiedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'packageDirectory': str(package),
        'entries': entries,
        'scope': 'CET opt-in on application entry executables; bundled runtime DLLs are unmodified.',
    }
    (ROOT / 'Verification/windows-cet-latest.json').write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    assert report['passed'], 'CET must be disabled on Windows game/server executables'
    return report


if __name__ == '__main__':
    print(json.dumps(verify(Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'Builds/.staging/Windows'),
                     ensure_ascii=False, indent=2))
