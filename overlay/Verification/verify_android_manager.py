"""Bind the small manager, compatible game bridge and native file contracts."""
from pathlib import Path
import hashlib,json,re,subprocess,zipfile,datetime
ROOT=Path(__file__).resolve().parents[1]
TOOLS=Path('C:/Android/build-tools/36.0.0')
JAVA=Path('I:/BnsmTools/Unity6.3/6000.3.27f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe')
def command(args):return subprocess.check_output(list(map(str,args)),stderr=subprocess.STDOUT).decode('utf-8',errors='replace')
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
game=json.loads((ROOT/'Verification/android-integrated-phone/apk-inspection.json').read_text())
build=json.loads((ROOT/'Verification/android-save-manager-build.json').read_text())
apk=Path(build['apk'])
if not apk.exists():apk=ROOT/'Builds/Android-SaveManager/BnsmSaveManager.apk'
game_apk=Path(game['apk'])
if not game_apk.exists():game_apk=ROOT/'Builds/Android-Integrated/BnsmLocal-integrated-arm64.apk'
assert sha(apk)==build['sha256']
for name,want in build['sourceSha256'].items():assert sha(ROOT/'AndroidSaveManager'/name)==want
assert sha(game_apk)==game['sha256'] and game['versionCode']>=33
signature=command([JAVA,'-jar',TOOLS/'lib/apksigner.jar','verify','--verbose','--print-certs',apk])
game_signature=(ROOT/'Verification/android-integrated-phone/signature.txt').read_text()
cert=lambda text:re.search(r'Signer #1 certificate SHA-256 digest: (\w+)',text).group(1)
assert cert(signature)==cert(game_signature)
badging=command([TOOLS/'aapt.exe','dump','badging',apk])
assert "package: name='local.bnsm.savemanager'" in badging and "targetSdkVersion:'36'" in badging and "sdkVersion:'26'" in badging
permissions=command([TOOLS/'aapt.exe','dump','permissions',apk])
for disallowed in ['android.permission.INTERNET','android.permission.MANAGE_EXTERNAL_STORAGE','android.permission.READ_EXTERNAL_STORAGE','android.permission.WRITE_EXTERNAL_STORAGE']:
    assert disallowed not in permissions
assert 'local.bnsm.integratedtest.permission.MANAGE_SAVES' in permissions
manifest=command([TOOLS/'aapt.exe','dump','xmltree',game_apk,'AndroidManifest.xml'])
for value in ['local.bnsm.bridge.SaveManagerGateway','local.bnsm.bridge.SaveBridge',':management','local.bnsm.integratedtest.management','local.bnsm.integratedtest.permission.MANAGE_SAVES','local.bnsm.bridge.ExportProvider']:
    assert value in manifest,value
assert '0x2' in manifest # signature protection is also checked below against source.
source_manifest=(ROOT/'Project/Assets/Plugins/Android/AndroidManifest.xml').read_text(encoding='utf-8')
assert 'android:protectionLevel="signature"' in source_manifest and 'android:grantUriPermissions="true"' in source_manifest
command([TOOLS/'zipalign.exe','-c','4',apk])
with zipfile.ZipFile(apk) as package:
    assert package.testzip() is None and not any(x.startswith('lib/') for x in package.namelist())
    manager_dex=b''.join(package.read(x) for x in package.namelist() if re.fullmatch(r'classes\d*\.dex',x))
    assert b'receiveSelectedImport' in manager_dex and b'pickImport' in manager_dex and b'importSha256' in manager_dex
    assert b'confirmDelete' in manager_dex and b'cleanupPending' in manager_dex
    assert all(name in manager_dex for name in [b'SaveBackups',b'createBackup',b'restoreBackup',b'shareCopy'])
with zipfile.ZipFile(game_apk) as package:
    assert hashlib.sha256(package.read('assets/console-cat.png')).hexdigest()==sha(ROOT/'Project/Assets/StreamingAssets/console-cat.png')
    dex=b''.join(package.read(x) for x in package.namelist() if re.fullmatch(r'classes\d*\.dex',x))
    for name in ['SaveBridge','SharedWorkspace','ExportProvider','SaveManagerGateway','ManagementActions']:assert name.encode() in dex
    assert b'deleteProfile' in dex and b'recoverDeletes' in dex
contract=json.loads((ROOT/'Verification/android-manager-contract/java-result.json').read_text())
numeric=json.loads((ROOT/'Verification/android-manager-contract/numeric-result.json').read_text())
assert contract['losslessBotQueue'] and contract['jsonRuntime'].startswith('Android AOSP')
assert numeric['passed'] and len(numeric['checks'])>=14 and numeric['coreSha256']==game['embeddedCoreSha256']
saves=json.loads((ROOT/'Verification/android-manager-contract/csharp-result.json').read_text())
bot_settings=json.loads((ROOT/'Verification/android-manager-contract/bot-result.json').read_text())
assert contract['passed'] and contract['checks']>=26 and saves['passed'] and len(saves['checks'])>=8
assert contract['deletionChecks']>=11
backup=json.loads((ROOT/'Verification/android-manager-contract/backup-result.json').read_text())
assert backup['passed'] and backup['checks']>=7
imports=json.loads((ROOT/'Verification/android-manager-contract/import-result.json').read_text())
import_core=json.loads((ROOT/'Verification/android-manager-contract/import-core-result.json').read_text())
assert imports['passed'] and imports['checks']>=20 and imports['opaqueUInt64Preserved'] and imports['receiptIdempotent']
assert import_core['passed'] and import_core['coreSha256']==game['embeddedCoreSha256'] and len(import_core['checks'])>=4
assert saves['coreSha256']==game['embeddedCoreSha256']
assert bot_settings['passed'] and len(bot_settings['checks'])>=10 and bot_settings['coreSha256']==game['embeddedCoreSha256']
ui=(ROOT/'Project/Assets/Plugins/Android/LocalSupport.java').read_text(encoding='utf-8')
console=ui[ui.index('private static void showConsole'):ui.index('private static void actionCard')]
assert console.count('actionCard(')==3
for forbidden in ['启动内置服务端','保存并停止服务端','导入旧服务端存档']:assert forbidden not in console
assert 'entryWidth=dp(activity,48);entryHeight=dp(activity,48)' in ui
manager_ui=(ROOT/'AndroidSaveManager/app/src/main/java/local/bnsm/savemanager/MainActivity.java').read_text('utf-8')
assert 'getContentResolver().call(' not in manager_ui and 'startActivityForResult(intent,7201)' in manager_ui
assert 'transferSha256' in manager_ui and 'revokeUriPermission' in manager_ui
assert 'private void bots()' in manager_ui and 'botConfigure' in manager_ui and 'botGenerate' in manager_ui
version_code=int(re.search(r"versionCode='(\d+)'",badging).group(1));assert version_code>=6 and game['versionCode']>=53
report=dict(passed=True,apk=str(apk),package='local.bnsm.savemanager',versionCode=version_code,bytes=apk.stat().st_size,sha256=sha(apk),gameSha256=game['sha256'],embeddedCoreSha256=game['embeddedCoreSha256'],sameSigningCertificate=True,signerSha256=cert(signature),nativeOnly=True,privateManagementProcess=True,foregroundResultTransport=True,nonceAndChecksumTransfer=True,offlineBotSettings=True,botSettingsRegressionChecks=len(bot_settings['checks']),broadStoragePermission=False,internetPermission=False,javaContractChecks=contract['checks'],saveRegressionChecks=len(saves['checks']),deviceVerified=False,installed=False,checkedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
device=ROOT/'Verification/android-manager-offline-device-result.json'
report.update(losslessBotQueueVerified=True,botStartupRecoveryChecks=len(numeric['checks']),androidJsonRuntime=contract['jsonRuntime'])
report.update(offlineSaveImport=True,combinedSaveLogImport=True,importChecks=imports['checks'],importCoreChecks=len(import_core['checks']))
report.update(offlineSaveDeletion=True,deletionChecks=contract['deletionChecks'])
report.update(persistentSaveBackups=True,backupChecks=backup['checks'])
if device.exists():
    proof=json.loads(device.read_text('utf-8'))
    if proof.get('passed') and proof.get('gameSha256')==game['sha256'] and proof.get('managerSha256')==sha(apk):
        report.update(installed=True,offlineManagementDeviceVerified=True,offlineManagementDeviceProof=proof)
(ROOT/'Verification/android-save-manager-inspection.json').write_text(json.dumps(report,indent=2),'utf-8')
print(json.dumps(report,indent=2))
