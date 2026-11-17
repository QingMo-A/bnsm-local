"""Exercise native exploration and two paid occupation buttons on an isolated save."""
import datetime
import hashlib
import json
import pathlib
import subprocess
import time

root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('castle-explore-instant-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
folder.mkdir()
fixture=None
client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
    try:
        fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--castle-explore-instant-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        until=time.monotonic()+70
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until:
                raise RuntimeError('Fixture failed '+str(folder))
            time.sleep(.2)
        endpoint=json.loads((folder/'endpoints.json').read_text('utf-8-sig'))
        client=subprocess.Popen([str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-castle-explore-instant-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-castle-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
        client.wait(timeout=200)
        result=json.loads((folder/'castle-explore-instant-native.json').read_text('utf-8-sig'))
        result.update(folder=str(folder),normalSaveUsed=False,nativeExitCode=client.returncode)
        result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
        result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
        player=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0]
        state=player['Competitive']
        result['persistedTwoPaidOccupations']=state['CastleSkips'].get('0')==2 and len(state['CastleHistory'])==2 and sum(s['Status']==1 for s in state['Castles'].values())==2
        result['noFabricatedReplay']=not state['Replays']
        result['persistedIdle']=state['ActiveMode']==0 and state['Stamp']==0 and player.get('ActiveStage') is None and player['ActiveBattleStamp']==0
        result['passed'] &= client.returncode==0 and all(result[key] for key in ['persistedTwoPaidOccupations','noFabricatedReplay','persistedIdle'])
        (root/'Verification/castle-explore-instant-native-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(result),flush=True)
        raise SystemExit(0 if result['passed'] else 1)
    finally:
        for process in [client,fixture]:
            if process is not None and process.poll() is None:
                process.terminate()
                try:process.wait(timeout=8)
                except subprocess.TimeoutExpired:process.kill()
