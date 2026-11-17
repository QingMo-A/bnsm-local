"""Native full-card Live2D, actual arena replay/settlement and saved prototype."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,time,sys
root=Path(__file__).resolve().parents[1]
smoke='--smoke-interactive' in sys.argv
interactive='--interactive' in sys.argv or smoke
folder=root/'Verification'/('original-card-playable' if interactive else 'original-card-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir(exist_ok=interactive)
env=os.environ.copy();env['PATH']='C:/Program Files/dotnet;'+env.get('PATH','')
if interactive:env['BNSM_ORIGINAL_CARD_INTERACTIVE']='1'
if interactive:
    (folder/'endpoints.json').unlink(missing_ok=True)
    (folder/'preview-ready.json').unlink(missing_ok=True)
client=None;log=(folder/'fixture.log').open('w',encoding='utf-8')
fixture=subprocess.Popen(['C:/Program Files/dotnet/dotnet.exe',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--original-card-fixture',str(folder)],cwd=root,stdout=log,stderr=subprocess.STDOUT,env=env,creationflags=subprocess.CREATE_NO_WINDOW)
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
try:
    until=time.monotonic()+60
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Prototype fixture did not start: '+str(folder))
        time.sleep(.2)
    port=json.loads((folder/'endpoints.json').read_text())['directoryPort']
    exe=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'
    command=[str(exe),'-bnsm-probe','-bnsm-autoconnect','-bnsm-original-card-test','-bnsm-test-directory-port='+str(port),'-bnsm-competitive-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    if interactive:command.append('-bnsm-original-card-preview')
    client=subprocess.Popen(command,cwd=exe.parent,env=env,creationflags=subprocess.CREATE_NO_WINDOW)
    if interactive:
        print('新卡试制窗口已启动。可关闭全屏立绘后查看卡组和进入战斗；存档独立保存在 '+str(folder/'saves'),flush=True)
        if smoke:
            until=time.monotonic()+110
            while not (folder/'preview-ready.json').exists():
                if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Interactive preview did not become ready')
                time.sleep(.3)
            ready=json.loads((folder/'preview-ready.json').read_text('utf-8-sig'))
            assert all(ready['checks'].values()),ready
            ready.update(passed=True,clientAssemblySha256=digest(exe.parent/'BnsmLocal_Data/Managed/Assembly-CSharp.dll'))
            (root/'Verification/original-card-interactive-latest.json').write_text(json.dumps(ready,ensure_ascii=False,indent=2),'utf-8')
            print('Interactive preview entry passed.',flush=True);raise SystemExit(0)
        client.wait();raise SystemExit(client.returncode)
    until=time.monotonic()+260
    while not (folder/'competitive-native.json').exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Prototype player did not finish: '+str(folder))
        time.sleep(.3)
    client.wait(timeout=15)
    ui=json.loads((folder/'original-card-ui.json').read_text('utf-8-sig'));combat=json.loads((folder/'competitive-native.json').read_text('utf-8-sig'))
    saved=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0]
    checks=dict(ui['checks']);checks.update(actualNativeBattleAndResult=combat['passed'],savedIndependentCard=saved['CardIds'][0]==1900000001,onePersistedPvpSettlement=saved['Competitive']['Records']['12']['Battles']==1,persistedReplay=bool(saved['Competitive']['Replays']),idleAfterResult=saved['ActiveStage'] is None and saved['Competitive']['ActiveMode']==0)
    result=dict(passed=ui['passed'] and combat['passed'] and all(checks.values()) and client.returncode==0,checks=checks,errors=ui['errors'],nativeExitCode=client.returncode,folder=str(folder),normalSaveUsed=False,fullyLayeredCubismModel=False,sourceRigReused=True,clientAssemblySha256=digest(exe.parent/'BnsmLocal_Data/Managed/Assembly-CSharp.dll'),serverCoreSha256=digest(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll'))
    (root/'Verification/original-card-native-latest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8');print(json.dumps(result,ensure_ascii=False),flush=True)
    if not result['passed']:raise RuntimeError('Native original-card checks failed: '+str(folder))
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=10)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
