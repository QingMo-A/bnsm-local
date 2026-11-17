"""Native castle layout, clipping/picking and wheel regression at desktop sizes."""
from pathlib import Path
import datetime,hashlib,json,subprocess,time

root=Path('I:/BnsmModernWork')
folder=root/'Verification'/('castle-ui-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
    fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--castle-lifecycle-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        until=time.monotonic()+70
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
            time.sleep(.2)
        endpoint=json.loads((folder/'endpoints.json').read_text(encoding='utf-8-sig'))
        player=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
        client=subprocess.Popen([str(player/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-castle-ui-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-castle-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
        client.wait(timeout=220)
        result=json.loads((folder/'castle-ui-native.json').read_text(encoding='utf-8-sig'))
        result.update(folder=str(folder),nativeExitCode=client.returncode)
        result['clientAssemblySha256']=hashlib.sha256((player/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
        result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
        result['passed'] &= client.returncode==0
        (root/'Verification/castle-ui-native-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(result),flush=True)
        raise SystemExit(0 if result['passed'] else 1)
    finally:
        for process in [client,fixture]:
            if process is not None and process.poll() is None:
                process.terminate()
                try:process.wait(timeout=8)
                except subprocess.TimeoutExpired:process.kill()
