from pathlib import Path
import datetime,hashlib,json,shutil,socket,subprocess,time,urllib.request

root=Path('I:/BnsmModernWork');package=root/'WindowsPreview'
playerSource=root/'Builds/Windows-SocialUi';player=package/'UnityRebuilt/WindowsBuild-SocialUi3'
serverSource=root/'Builds/WindowsServer-SocialUi';server=package/'LocalServer/WindowsSocialUi'
def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
assert json.loads((root/'Verification/social-ui-player-latest.json').read_text())['passed']
assert not player.exists(),'Never overwrite a running client release'
for source,target in [(playerSource,player),(serverSource,server)]:
    if not target.exists():shutil.copytree(source,target)
    for original in source.rglob('*'):
        if original.is_file():assert (target/original.relative_to(source)).stat().st_size==original.stat().st_size
critical=['BnsmLocal.exe','UnityPlayer.dll','BnsmLocal_Data/Managed/Assembly-CSharp.dll']
hashes={name:digest(player/name) for name in critical}
assert all(digest(playerSource/name)==hashes[name] for name in critical)
core=digest(server/'Bnsm.ServerCore.dll');assert core==digest(serverSource/'Bnsm.ServerCore.dll')
fixture=root/'Verification'/('social-ui-published-host-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));fixture.mkdir()
with socket.socket() as port:port.bind(('127.0.0.1',0));debugPort=port.getsockname()[1]
with (fixture/'host.log').open('wb') as log:
    process=subprocess.Popen([str(server/'Bnsm.LocalServer.exe'),'--content-root',str(package),'--save-directory',str(fixture/'saves'),'--directory-port','0','--login-port','0','--game-port','0','--debug-port',str(debugPort)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));until=time.monotonic()+45
        while True:
            try:
                with opener.open(f'http://127.0.0.1:{debugPort}/health',timeout=2) as response:health=json.load(response)
                break
            except Exception:
                if process.poll() is not None or time.monotonic()>until:raise RuntimeError('Published host failed startup: '+str(fixture))
                time.sleep(.2)
        assert health['status']=='ready'
        assert all(feature in health['data']['localFeatures'] for feature in ['native friend bot duels','overflow-safe mission stamina rewards','native mission title rewards','persistent faction bot battles'])
        assert Path(health['saveProfile']['directory']).resolve()==(fixture/'saves').resolve()
    finally:
        if process.poll() is None:process.terminate();process.wait(timeout=10)
gacha=digest(package/'LocalServer/data/gacha.json')
assert gacha=='de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908'
settings=package/'LocalWorkspace.json';backup=package/'LocalWorkspace-before-SocialUi3.json';assert not backup.exists();shutil.copy2(settings,backup)
data=json.loads(settings.read_text(encoding='utf-8-sig'));retired=data.setdefault('retiredClientPaths',[])
if data['clientPath'] not in retired:retired.append(data['clientPath'])
data['clientPath']=str(player/'BnsmLocal.exe');data['serverPath']=str(server/'Bnsm.LocalServer.exe')
temporary=settings.with_suffix('.social-ui.tmp');temporary.write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8-sig');temporary.replace(settings)
launcher=package/'Start-LocalGame.ps1';text=launcher.read_text(encoding='utf-8-sig')
text=text.replace('UnityRebuilt\\WindowsBuild-DesktopPages\\BnsmLocal.exe','UnityRebuilt\\WindowsBuild-SocialUi3\\BnsmLocal.exe').replace('LocalServer\\WindowsBots\\Bnsm.LocalServer.exe','LocalServer\\WindowsSocialUi\\Bnsm.LocalServer.exe')
text=text.replace("'native mission title rewards', 'persistent faction bot battles'","'native mission title rewards', 'persistent faction bot battles', 'native friend bot duels', 'overflow-safe mission stamina rewards'")
text=text.replace('UnityRebuilt\\WindowsBuild-SocialUi2\\BnsmLocal.exe','UnityRebuilt\\WindowsBuild-SocialUi3\\BnsmLocal.exe')
launcher.write_text(text,encoding='utf-8-sig')
report={'passed':True,'publishedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'client':str(player/'BnsmLocal.exe'),'server':str(server/'Bnsm.LocalServer.exe'),'clientSha256':hashes,'coreSha256':core,'nativeEvidence':json.loads((root/'Verification/social-ui-player-latest.json').read_text())['folder'],'publishedHostReady':True,'hostFixture':str(fixture),'productionSaveUsed':False,'gachaSha256':gacha}
(root/'Verification/social-ui-windows-publication.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2),flush=True)
