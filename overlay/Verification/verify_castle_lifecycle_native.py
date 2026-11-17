import datetime,hashlib,json,pathlib,subprocess,time,sys
root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('castle-lifecycle-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
    fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--castle-lifecycle-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    client=None
    try:
        until=time.monotonic()+70
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
            time.sleep(.2)
        endpoint=json.loads((folder/'endpoints.json').read_text('utf-8-sig'))
        client=subprocess.Popen([str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-castle-lifecycle-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-castle-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
        client.wait(timeout=260)
        result=json.loads((folder/'castle-lifecycle-native.json').read_text('utf-8-sig'));result['folder']=str(folder)
        result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
        result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
        player=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0];state=player['Competitive']
        result['persistedSearchCount']=state['CastleSearches']==3
        result['persistedAdmissionCount']=state['CastleAttacks']==2
        result['oneActualSettlement']=len(state['CastleHistory'])==1
        result['persistedIdle']=state['ActiveMode']==0 and state['CastleTarget']==0 and player.get('ActiveStage') is None
        result['passed'] &= all(result[k] for k in ['persistedSearchCount','persistedAdmissionCount','oneActualSettlement','persistedIdle'])
        (root/'Verification/castle-lifecycle-native-latest.json').write_text(json.dumps(result,indent=2),'utf-8')
        print(json.dumps(result),flush=True)
        raise SystemExit(0 if result['passed'] and client.returncode==0 else 1)
    finally:
        for p in (client,fixture):
            if p is not None and p.poll() is None:p.terminate()
