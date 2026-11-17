from pathlib import Path
import datetime,hashlib,json,shutil

root=Path('I:/BnsmModernWork');verification=root/'Verification'
checked=json.loads((verification/'android-packages.json').read_text(encoding='utf-8-sig'));assert checked['passed']
def digest(p):
    with p.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
game=checked['packages'][0];server=checked['packages'][1]
assert game['signatureVerified'] and server['signatureVerified']
assert game['elf16KiBAligned'] and server['elf16KiBAligned']
gameTarget=root/'Builds/Android-ARM64/BnsmLocal-arm64.apk'
serverTarget=root/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk'
for item,target,version in [(game,gameTarget,5),(server,serverTarget,4)]:
    source=Path(item['apk']);assert digest(source)==item['sha256']
    temporary=target.with_suffix('.social-ui.tmp');shutil.copy2(source,temporary);assert digest(temporary)==item['sha256']
    backup=target.with_name(target.stem+'-v'+str(version)+target.suffix);assert not backup.exists()
    assert target.resolve().is_relative_to(root.resolve()) and backup.resolve().is_relative_to(root.resolve())
    target.rename(backup);temporary.replace(target)
    item['sourceApk']=item['apk'];item['apk']=str(target);item['previousApk']=str(backup)
    item['versionCode']=6 if item['role']=='game' else 5
# Keep the companion output directory consistent with the signed installation artifact.
for source in (root/'Builds/PhoneServer-SocialUi/apk').iterdir():
    if source.is_file() and source.name!=serverTarget.name:shutil.copy2(source,serverTarget.parent/source.name)
assert digest(serverTarget)==server['sha256']
core=digest(root/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
assert core==digest(root/'WindowsPreview/LocalServer/WindowsSocialUi/Bnsm.ServerCore.dll')
assert digest(root/'Builds/PhoneServer/server-content.zip')=='a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1'
report={'passed':True,'publishedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'packages':[game,server],'coreSha256':core,'sameServerCoreOnBothPlatforms':True,'sameSigningCertificatesAsPreviousPackages':True,'deviceVerified':False,'desktopLayoutAdaptersEnabledOnAndroid':False}
(verification/'social-ui-android-publication.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'passed':True,'game':game['apk'],'gameVersionCode':6,'server':server['apk'],'serverVersionCode':5,'sameServerCoreOnBothPlatforms':True,'deviceVerified':False},indent=2),flush=True)
