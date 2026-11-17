"""Read/export only on the explicitly selected phone; never change its profiles."""
from pathlib import Path
import argparse, base64, datetime, hashlib, json, re, subprocess, time

ROOT = Path(__file__).resolve().parents[1]
ADB = Path('C:/Android/platform-tools/adb.exe')
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
args = parser.parse_args()

def adb(*parts, check=True):
    return subprocess.run([str(ADB), '-s', args.serial, *parts], capture_output=True,
                          text=True, encoding='utf-8', errors='replace', check=check)

def host_stopped():
    result = adb('shell', 'pidof', 'local.bnsm.integratedtest', check=False)
    assert result.returncode in [0, 1]
    return not result.stdout.strip()

game = json.loads((ROOT/'Verification/android-integrated-phone/apk-inspection.json').read_text())
manager = json.loads((ROOT/'Verification/android-save-manager-inspection.json').read_text())
def installed_sha(package):
    path = adb('shell', 'pm', 'path', package).stdout.strip().removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9_~=./-]+/base\.apk', path), path
    return adb('shell', 'sha256sum', path).stdout.split()[0]

assert host_stopped(), 'Close the game normally before offline verification'
management_cold = not adb('shell', 'pidof', 'local.bnsm.integratedtest:management', check=False).stdout.strip()
assert installed_sha(game['package']) == game['sha256']
assert installed_sha(manager['package']) == manager['sha256']
process = subprocess.Popen([str(ADB), '-s', args.serial, 'shell', 'am', 'instrument',
    '-w', '-r', '-e', 'mode', 'readonly',
    'local.bnsm.managementtests/local.bnsm.tests.TransportInstrumentation'],
    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')
deadline = time.monotonic() + 120
launch = None
try:
    # Some vendor ROMs block startActivitySync from a background instrumentation
    # process. Shell opens only the manager, as the user opening its launcher does.
    time.sleep(2)
    launch = adb('shell', 'am', 'start', '-W', '-n', 'local.bnsm.savemanager/.MainActivity')
    while True:
        assert host_stopped(), 'Unexpected game host process during offline read/export'
        try:
            output, _ = process.communicate(timeout=1)
            break
        except subprocess.TimeoutExpired:
            assert time.monotonic() < deadline, 'Native read/export verification timed out'
    (ROOT/'Verification/android-manager-offline-device-raw.log').write_text(output, 'utf-8')
    assert process.returncode == 0 and 'INSTRUMENTATION_CODE: -1' in output, output
    encoded = re.search(r'INSTRUMENTATION_RESULT: report=(\S+)', output).group(1)
    native = json.loads(base64.b64decode(encoded))
    assert native['passed'] and native['mode'] == 'readonly' and len(native['checks']) >= 10
    assert host_stopped()
    assert installed_sha(game['package']) == game['sha256']
    assert installed_sha(manager['package']) == manager['sha256']
    proof = dict(passed=True, gameSha256=game['sha256'], managerSha256=manager['sha256'],
        nativeReport=native, gameHostProcessStarted=False,
        deviceModel=adb('shell', 'getprop', 'ro.product.model').stdout.strip(),
        androidApi=int(adb('shell', 'getprop', 'ro.build.version.sdk').stdout.strip()),
        scope='Existing active profile read/export only; no create, switch, rename or restore; no gameplay or external sharing test',
        firstColdRequestPassed=management_cold,
        launchMethod='Explicit manager Activity via shell, required by vendor instrumentation background-start restriction',
        checkedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    (ROOT/'Verification/android-manager-offline-device-result.json').write_text(json.dumps(proof, indent=2), 'utf-8')
    print(json.dumps(proof, indent=2), flush=True)
finally:
    if process.poll() is None:
        process.terminate()
    # Remove only the temporary test package; keep both actual updated apps/data.
    result = adb('uninstall', 'local.bnsm.managementtests', check=False)
    print('Temporary verifier removed: ' + str(result.returncode == 0), flush=True)
