import datetime,hashlib,json,pathlib,subprocess,time,sys
root=pathlib.Path('I:/BnsmModernWork')
mode='lifecycle'
folder=root/'Verification'/('battle-lifecycle-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
log=open(folder/'fixture.log','w',encoding='utf-8');client=None
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--battle-lifecycle-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
try:
    endpoint=folder/'endpoints.json';until=time.monotonic()+60
    while not endpoint.exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoints=json.loads(endpoint.read_text())
    command=[str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-lifecycle-test','-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-bnsm-lifecycle-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    client=subprocess.Popen(command,creationflags=subprocess.CREATE_NO_WINDOW);until=time.monotonic()+330
    report=folder/'battle-lifecycle-native.json'
    while not report.exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Player did not finish '+str(folder))
        time.sleep(.4)
    result=json.loads(report.read_text(encoding='utf-8-sig'));client.wait(timeout=15);result['nativeExitCode']=client.returncode;result['folder']=str(folder);result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
    result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
    player=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'))[0]
    result['persistedRetreats']=player['Raids']['Used']['4']==3 and player['ActiveStage'] is None and player['Raids']['ActiveMode']==0
    result['persistedReserveVictory']=player['Wander']['Spot']==2 and len(player['Wander']['Cards'])>=2
    result['passed'] &= result['persistedRetreats'] and result['persistedReserveVictory']
    (root/('Verification/battle-lifecycle-native-latest.json')).write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False),flush=True)
    if not result['passed'] or client.returncode:raise RuntimeError('Native competitive failed '+str(folder))
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
