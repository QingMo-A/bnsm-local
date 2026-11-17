"""Inspect a real Unity Xcode export and package it without claiming native compilation."""
from pathlib import Path
import argparse
import hashlib
import json
import plistlib
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--export', type=Path, required=True)
    parser.add_argument('--package', action='store_true')
    args = parser.parse_args()
    export = args.export.resolve()
    checks = []

    def check(value, name):
        if not value:
            raise RuntimeError(name)
        checks.append(name)

    build = json.loads((ROOT / 'Verification/ios-build-result.json').read_text(encoding='utf-8'))
    check(build['result'] == 'Succeeded' and build['errors'] == 0, 'Unity reports successful iOS device export')
    project = (export / 'Unity-iPhone.xcodeproj/project.pbxproj').read_text(encoding='utf-8')
    plist = plistlib.loads((export / 'Info.plist').read_bytes())
    check('UnityFramework' in project and 'arm64' in project, 'ARM64 app and UnityFramework targets present')
    check(not re.search(r'[A-Za-z]:[/\\]', project), 'Xcode references remain portable across machines')
    check('BnsmIosSupport.mm' in project and '-fobjc-arc' in project, 'Native bridge included with ARC')
    check((export / 'Libraries/Plugins/iOS/BnsmIosSupport.mm').read_bytes() == (ROOT / 'Project/Assets/Plugins/iOS/BnsmIosSupport.mm').read_bytes(), 'Exported native source matches current bridge')
    check('UniformTypeIdentifiers.framework' in project, 'Native document picker framework linked')
    check((export / 'BnsmConsoleIcon.png').read_bytes() == (ROOT / 'AndroidSaveManager/app/src/main/res/drawable/cat.png').read_bytes(), 'Original supplied console icon retained')
    check(plist['NSAppTransportSecurity']['NSAllowsLocalNetworking'] and not plist['NSAppTransportSecurity'].get('NSAllowsArbitraryLoads', False), 'Local-only HTTP allowance')
    check(not plist.get('UIBackgroundModes'), 'No fake perpetual background capability')
    check(not plist['UIFileSharingEnabled'] and not plist['LSSupportsOpeningDocumentsInPlace'], 'Live save folder stays private; file picker uses copies')
    icon_proof = json.loads((ROOT / 'Verification/ios-launcher-icon.json').read_text(encoding='utf-8'))
    check(icon_proof['sourceSha256'] == digest(ROOT / 'AndroidGameIcon/res/mipmap-xxxhdpi/app_icon.png'), 'Launcher derived from original game icon')
    for icon in icon_proof['icons']:
        check(digest(export / 'Unity-iPhone/Images.xcassets/AppIcon.appiconset' / icon['file']) == icon['sha256'], 'Launcher icon bound: ' + icon['file'])
    check(plist['CFBundleShortVersionString'] == '1.97.0', 'Apple-compatible three-component version')
    raw = export / 'Data/Raw'
    check(digest(raw / 'embedded-server.zip') == digest(ROOT / 'Project/Assets/StreamingAssets/embedded-server.zip'), 'Embedded server content matches current project')
    check((raw / 'LocalAndroidContent.json').is_file(), 'Verified mobile data manifest included')
    il2cpp = export / 'Il2CppOutputProject/Source/il2cppOutput'
    cpp_files = list(il2cpp.glob('Assembly-CSharp*.cpp'))
    text = '\n'.join(p.read_text(encoding='utf-8', errors='replace') for p in cpp_files)
    for symbol in ['LocalIosHost', 'BnsmIosInstall', 'LocalMobileSupportArchive', 'SaveInBackground', 'MobileLocal']:
        check(symbol in text, 'IL2CPP contains ' + symbol)
    check(any(il2cpp.glob('Bnsm.EmbeddedServer*.cpp')), 'Shared server converted by IL2CPP')
    check(any(il2cpp.glob('Live2DUnity*.cpp')), 'Original Live2D managed engine converted by IL2CPP')
    macho_magic = [bytes.fromhex(value) for value in ['cffaedfe', 'feedfacf', 'cafebabe', 'bebafeca', 'cafebabf', 'bfbafeca']]
    for arch, bee_arch in [('arm64', 'arm64'), ('x86_64', 'x64')]:
        deploy = export / ('Il2CppOutputProject/IL2CPP/build/deploy_' + arch)
        for relative in ['il2cpp', 'il2cpp-compile', 'bee_backend/mac-' + bee_arch + '/bee_backend']:
            tool = deploy / relative
            with tool.open('rb') as source:
                magic = source.read(4)
            check(magic in macho_magic, 'Real macOS build tool included: ' + arch + '/' + relative)
    check(not any(p.name in ['players.json', 'bot-world.json'] or p.suffix in ['.p12', '.mobileprovision', '.keystore', '.jks'] for p in export.rglob('*') if p.is_file()), 'Delivery excludes player saves and signing credentials')
    contract = json.loads((ROOT / 'Verification/ios-support-contract.json').read_text(encoding='utf-8'))
    check(contract['success'], 'Portable support archive runtime contract passes')
    sources = []
    for relative in ['LocalIosHost.cs', 'LocalMobileSupportArchive.cs', 'LocalMode.cs', 'LocalEmbeddedServer.cs', 'LocalAndroidBootstrap.cs', 'LocalAndroidDiagnostics.cs']:
        path = ROOT / 'Project/Assets/Scripts/Assembly-CSharp' / relative
        sources.append(dict(path=str(path.relative_to(ROOT)), sha256=digest(path)))
    report = dict(product='BnsmLocalGame', target='iOS', artifactType='XcodeProject',
                  unityExported=True, il2cppGenerated=True, nativeCompiled=False, signed=False,
                  deviceVerified=False, checks=checks, sources=sources,
                  embeddedCoreSha256=digest(ROOT / 'Project/Assets/Plugins/EmbeddedServer/Bnsm.EmbeddedServer.dll'))
    if args.package:
        destination = ROOT / 'Builds/iOS-Integrated'
        destination.mkdir(parents=True, exist_ok=True)
        package = destination / 'BnsmLocal-iOS-Xcode.zip'
        partial = package.with_suffix('.zip.partial')
        with zipfile.ZipFile(partial, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as zip:
            for path in sorted(export.rglob('*')):
                if path.is_file():
                    zip.write(path, 'Xcode/' + path.relative_to(export).as_posix())
            for name in ['build_ipa.py', 'README.md']:
                zip.write(ROOT / 'Tools/iOS' / name, name)
            zip.writestr('delivery.json', json.dumps(report, ensure_ascii=False, indent=2))
        with zipfile.ZipFile(partial) as zip:
            check(zip.testzip() is None, 'Delivery ZIP integrity passes')
        partial.replace(package)
        report.update(package=str(package), bytes=package.stat().st_size, sha256=digest(package))
        (destination / 'latest.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        index_path = ROOT / 'Builds/latest-artifacts.json'
        index = json.loads(index_path.read_text(encoding='utf-8'))
        index['ios'] = {key: report[key] for key in ['artifactType', 'package', 'bytes', 'sha256', 'unityExported', 'il2cppGenerated', 'nativeCompiled', 'signed', 'deviceVerified']}
        index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'Verification/ios-export-final.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: report[key] for key in ['artifactType', 'nativeCompiled', 'signed', 'deviceVerified']}))
    print(str(len(checks)) + ' iOS export checks passed')


if __name__ == '__main__':
    main()
