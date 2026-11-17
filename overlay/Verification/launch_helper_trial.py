"""Open the actual Windows player with the opt-in helper wardrobe preview and isolated save."""
from pathlib import Path
import hashlib, json, os, subprocess, sys, time

ROOT=Path(__file__).resolve().parents[1]
FOLDER=ROOT/'Verification/helper-trial-playable'
EXE=ROOT/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'
if not EXE.exists(): EXE=ROOT/'WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'
FIXTURE=ROOT/'Verification/HelperTrialTests/bin/Release/net10.0/Bnsm.LocalServer.dll'

def serve():
    server=client=None
    try:
        for name in ('endpoints.json','preview-ready.json'):
            (FOLDER/name).unlink(missing_ok=True)
        log=(FOLDER/'fixture.log').open('w',encoding='utf-8')
        server=subprocess.Popen(['C:/Program Files/dotnet/dotnet.exe',str(FIXTURE),str(FOLDER)],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        until=time.monotonic()+80
        while not (FOLDER/'endpoints.json').exists():
            if server.poll() is not None or time.monotonic()>until:raise RuntimeError('Helper fixture did not start')
            time.sleep(.2)
        port=json.loads((FOLDER/'endpoints.json').read_text())['directoryPort']
        command=[str(EXE),'-bnsm-probe','-bnsm-autoconnect','-bnsm-helper-outfit-preview','-bnsm-test-directory-port='+str(port),'-bnsm-helper-output='+str(FOLDER),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(FOLDER/'client.log')]
        client=subprocess.Popen(command,cwd=EXE.parent,creationflags=subprocess.CREATE_NO_WINDOW)
        state=dict(serverPid=server.pid,clientPid=client.pid,normalSaveUsed=False,client=str(EXE),clientAssemblySha256=hashlib.sha256((EXE.parent/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest())
        (FOLDER/'running.json').write_text(json.dumps(state,indent=2),'utf-8')
        until=time.monotonic()+170
        while not (FOLDER/'preview-ready.json').exists():
            if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Helper player did not reach interactive preview')
            time.sleep(.3)
        proof=json.loads((FOLDER/'preview-ready.json').read_text('utf-8-sig'))
        state.update(ready=True,passed=proof['passed'])
        (FOLDER/'running.json').write_text(json.dumps(state,indent=2),'utf-8')
        print('HELPER_PREVIEW_INTERACTIVE '+str(state),flush=True)
        client.wait()
    except Exception as error:
        (FOLDER/'launch-error.txt').write_text(repr(error),'utf-8')
        raise
    finally:
        for proc in (client,server):
            if proc is not None and proc.poll() is None:
                proc.terminate()
                try:proc.wait(timeout=10)
                except subprocess.TimeoutExpired:proc.kill()

if __name__=='__main__':
    FOLDER.mkdir(exist_ok=True)
    if '--serve' in sys.argv:
        serve()
    else:
        log=(FOLDER/'launcher.log').open('w',encoding='utf-8')
        proc=subprocess.Popen([sys.executable,str(Path(__file__).resolve()),'--serve'],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        print(json.dumps(dict(controllerPid=proc.pid,evidence=str(FOLDER))))
