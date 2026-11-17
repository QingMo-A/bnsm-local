"""Publish only a verified independent trial; retain the production game/host pair and saves."""
from pathlib import Path
import argparse, datetime, hashlib, json, os, re, shutil
import embedded_phone_tools as phone

ROOT=phone.ROOT
OUT=phone.OUT
parser=argparse.ArgumentParser()
parser.add_argument('--package-only',action='store_true',help='Publish a checked APK without claiming installation or current device validation')
parser.add_argument('--with-manager',action='store_true')
args=parser.parse_args()
inspection=json.loads((OUT/'apk-inspection.json').read_text())
apk=Path(inspection['apk'])
device={}
if not args.package_only:
    installed=phone.adb('shell','pm path local.bnsm.integratedtest').strip().removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9_~=./-]+/base\.apk',installed)
    installed_hash=phone.adb('shell','sha256sum '+installed).split()[0]
    assert installed_hash==inspection['sha256']
    native=json.loads(phone.adb('shell','cat /sdcard/Android/data/local.bnsm.integratedtest/files/verification/client-probe/competitive-native.json'))
    assert native['passed']
    background=json.loads((OUT/'background.json').read_text())
    drag=json.loads((OUT/'popup-drag.json').read_text())
    memory=json.loads((OUT/'position-memory.json').read_text())
    assert background['passed'] and drag['dragged'] and drag['snappedLeft'] and memory['remembered']
    device=dict(installedApkSha256=installed_hash,nativeBattle=native,backgroundPassed=True,
        dragAndDockPassed=True,fullButtonVisibleWhileDragging=True,positionRememberedAcrossUpdate=True,deviceModel='24122RKC7C')
legacy={}
delivery_index=json.loads((ROOT/'Builds/latest-artifacts.json').read_text('utf-8-sig'))
for key,path,want in [
    ('androidGame','Builds/Android-ARM64/BnsmLocal-arm64.apk',delivery_index['mobile']['game']['sha256']),
    ('androidHost','Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk',delivery_index['mobile']['server']['sha256']),
    ('windowsClient','WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll',delivery_index['windows']['clientAssemblySha256'])]:
    value=hashlib.sha256((ROOT/path).read_bytes()).hexdigest()
    assert value==want
    legacy[key]=value
result=dict(inspection,**device,deviceVerified=not args.package_only,installed=not args.package_only,
    normalSaveOverwritten=False,legacyDeliveriesUnchanged=legacy,
    releaseChannel='independent-full-game-trial',
    createdUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
target=ROOT/'Builds/Android-Integrated/BnsmLocal-integrated-arm64.apk'
pause_proof=ROOT/'Verification/android-manager-pause-device-result.json'
if pause_proof.exists():
    pause=json.loads(pause_proof.read_text('utf-8'))
    if pause.get('passed') and pause.get('gameSha256')==inspection['sha256']:
        result.update(backgroundAutoSaveDeviceVerified=True,backgroundAutoSaveDeviceProof=pause)

manager=None
if args.with_manager:
    manager=json.loads((ROOT/'Verification/android-save-manager-inspection.json').read_text())
    assert manager['passed'] and manager['sameSigningCertificate'] and manager['gameSha256']==inspection['sha256']
    assert hashlib.sha256(Path(manager['apk']).read_bytes()).hexdigest()==manager['sha256']
    if manager.get('offlineManagementDeviceVerified'):
        proof=manager['offlineManagementDeviceProof']
        assert proof['passed'] and proof['gameSha256']==inspection['sha256'] and proof['managerSha256']==manager['sha256']
        result.update(installed=True,offlineManagementDeviceVerified=True,
            deviceVerificationScope='Cold offline management read/export only; gameplay not revalidated',
            offlineManagementDeviceProof=proof)
    manager_target=ROOT/'Builds/Android-SaveManager/BnsmSaveManager.apk'
    manager_target.parent.mkdir(parents=True,exist_ok=True)
    manager_temp=Path(str(manager_target)+'.tmp');shutil.copyfile(manager['apk'],manager_temp)
    assert hashlib.sha256(manager_temp.read_bytes()).hexdigest()==manager['sha256']
target.parent.mkdir(parents=True,exist_ok=True)
temporary=Path(str(target)+'.tmp')
shutil.copyfile(apk,temporary)
assert hashlib.sha256(temporary.read_bytes()).hexdigest()==inspection['sha256']
os.replace(temporary,target)
if manager is not None:
    os.replace(manager_temp,manager_target);manager['apk']=str(manager_target)
    (manager_target.parent/'latest.json').write_text(json.dumps(manager,indent=2),encoding='utf-8')
result['apk']=str(target)
(OUT/'final-result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
(target.parent/'latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
manifest=ROOT/'Builds/latest-artifacts.json'
index=json.loads(manifest.read_text(encoding='utf-8-sig'))
index['integratedTrial']={k:result[k] for k in ['apk','sha256','bytes','package','embeddedCoreSha256','releaseChannel']}
if manager is not None:index['saveManager']={k:manager[k] for k in ['apk','sha256','bytes','package','versionCode','gameSha256']}
manifest.write_text(json.dumps(index,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(published=str(target),sha256=inspection['sha256'],installed=result['installed'],passed=True)))
