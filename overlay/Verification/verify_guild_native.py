"""Run the existing game-owned native guild creation/member/contribution probe."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,sys,time
root=Path(__file__).resolve().parents[1]
stage=root/'Builds/.staging/Windows'
folder=root/'Verification'/('guild-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
env=os.environ.copy();env['PATH']='C:/Program Files/dotnet;'+env.get('PATH','')
client=None;log=(folder/'fixture.log').open('w',encoding='utf-8')
fixture=subprocess.Popen(['C:/Program Files/dotnet/dotnet.exe',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--guild-classic-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,env=env,creationflags=subprocess.CREATE_NO_WINDOW)
try:
    until=time.monotonic()+45
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Guild fixture failed: '+str(folder))
        time.sleep(.15)
    endpoint=json.loads((folder/'endpoints.json').read_text())
    started=time.time()
    client=subprocess.Popen([str(stage/'UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-guild-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-proof-file='+str(folder/'proof.json'),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
    until=time.monotonic()+170;report=None
    while report is None:
        choices=[p for p in (stage/'evidence').glob('guild-player-*/guild-player-test.json') if p.stat().st_mtime>=started-1]
        if choices:report=max(choices,key=lambda p:p.stat().st_mtime);break
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Guild native player did not finish: '+str(folder))
        time.sleep(.3)
    data=json.loads(report.read_text(encoding='utf-8-sig'));client.wait(timeout=15)
    data.update(folder=str(folder),screenshots=str(report.parent),nativeExitCode=client.returncode,
        normalSaveUsed=False,clientAssemblySha256=hashlib.sha256((stage/'UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),
        serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
    # Preserve evidence before the successful publisher prunes its stage.
    import shutil
    shutil.copytree(report.parent,folder/'screenshots')
    data['screenshots']=str(folder/'screenshots')
    (root/'Verification/guild-native-latest.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(data,ensure_ascii=False),flush=True)
    if not data['passed'] or client.returncode:raise RuntimeError('Guild native check failed: '+str(folder))
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
