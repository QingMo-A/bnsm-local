"""Static checks for the shipped host/game contract; does not access a device."""
from pathlib import Path
import json,re,subprocess,zipfile
ROOT=Path('I:/BnsmModernWork')
GAME=ROOT/'Builds/Android-ARM64/BnsmLocal-arm64.apk'
SERVER=ROOT/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk'
AAPT='C:/Android/build-tools/36.0.0/aapt.exe'
checks=[]
def check(value,text):
    assert value,text
    checks.append(text)
def manifest(path):return subprocess.check_output([AAPT,'dump','xmltree',str(path),'AndroidManifest.xml'],text=True,encoding='utf8')
game=manifest(GAME);server=manifest(SERVER)
check('local.bnsm.serverhost.permission.BIND_GAME' in game and 'local.bnsm.serverhost.permission.BIND_GAME' in server,'Shipped game requests the companion binding permission declared by the host')
def service(name):
    for part in server.split('E: service '):
        block=part.split('E: service ')[0].split('E: activity ')[0]
        if f'="{name}"' in block:return block
    raise AssertionError(name)
main=service('local.bnsm.serverhost.ServerService');relay=service('local.bnsm.serverhost.GameBindingService')
check('android:exported(0x01010010)=(type 0x12)0x0' in main and '0x40000000' in main,'Main host service stays private and declares the special-use foreground type')
check('android:permission' in relay and 'local.bnsm.serverhost.permission.BIND_GAME' in relay,'Only the priority relay is externally bindable, protected by the matching permission')
check('SYSTEM_ALERT_WINDOW' not in server and 'REQUEST_IGNORE_BATTERY_OPTIMIZATIONS' not in server,'Companion uses no overlay or automatic battery-exemption permission')
check('HostApplication' in server and 'allowBackup(0x01010280)=(type 0x12)0x0' in server,'Host initializes process diagnostics and keeps private saves out of automatic app backup')
with zipfile.ZipFile(GAME) as archive:
    dex=[archive.read(n) for n in archive.namelist() if re.fullmatch(r'classes\d*\.dex',n)]
    check(any(b'Llocal/bnsm/bridge/LocalServiceLink;' in d for d in dex),'The actual game APK includes the Java binding bridge')
for role,expected in [('game','561ac9bd66ab120c4d45f637bb8d31e99909be9c31cf190a5ce5bdb2894c05cf'),('server','a2ba085adc252f58e99865735c7107cb2921e20e40ddc087753a2773f0c240b6')]:
    report=(ROOT/f'Verification/android-{role}-signature.txt').read_text()
    check('certificate SHA-256 digest: '+expected in report,f'{role} package preserves its previous update signing identity')
    badging=(ROOT/f'Verification/android-{role}-badging.txt').read_text()
    version=re.search(r"versionCode='(\d+)'",badging)
    check(version and int(version.group(1))>=(3 if role=='game' else 2),f'{role} version code advances for an in-place update')
source=ROOT/'ServerBuildSource/AndroidHost'
check('StartCommandResult.Sticky' in (source/'ServerService.cs').read_text() and 'runRequested' in (source/'ServerService.cs').read_text(),'Sticky recovery persists user intent and retains the manual-stop guard')
check('Task.Run(StartAsync)' in (source/'ServerService.cs').read_text(),'Content verification and server construction start off the Android UI thread')
check('Task.Run(()=>manager.Execute' in (source/'MainActivity.Saves.cs').read_text() and 'ActionCreateDocument' in (source/'MainActivity.Support.cs').read_text(),'Native save operations run in worker tasks and export via the system document picker')
report={'passed':True,'checks':checks,'deviceImeVerified':False,'deviceBackgroundStressVerified':False,'deviceSaveManagerUiVerified':False,'deviceOperated':False}
(ROOT/'Verification/phone-support-android-static.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('ANDROID_SUPPORT_STATIC_PASSED',len(checks))
