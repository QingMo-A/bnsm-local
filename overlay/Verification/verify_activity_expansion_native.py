import datetime,hashlib,json,pathlib,subprocess,time
root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('activity-expansion-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
log=open(folder/'fixture.log','w',encoding='utf-8');client=None
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--activity-expansion-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
try:
    until=time.monotonic()+60
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoints=json.loads((folder/'endpoints.json').read_text())
    game=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
    client=subprocess.Popen([str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-activity-test','-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-bnsm-activity-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
    until=time.monotonic()+240
    while not (folder/'activities-test.json').exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Native activity probe did not finish '+str(folder))
        time.sleep(.4)
    result=json.loads((folder/'activities-test.json').read_text(encoding='utf-8-sig'));client.wait(timeout=20)
    result.update(folder=str(folder),nativeExitCode=client.returncode,clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest(),activityConfigSha256=hashlib.sha256((root/'WindowsPreview/LocalServer/data/activities.json').read_bytes()).hexdigest())
    result['passed']=result['passed'] and all(result['checks'].values()) and client.returncode==0
    players=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'))
    state=players[0]['Activities']
    result['persistedSupplyClaim']='900001/1' in state['DailyClaims']
    result['persistedRestoredLoginClaim']='900006/1' in state['PermanentClaims']
    result['oneActualLoginDay']=state['Total']['LOGIN']==1
    result['passed']=result['passed'] and all(result[k] for k in ['persistedSupplyClaim','persistedRestoredLoginClaim','oneActualLoginDay'])
    (root/'Verification/activity-expansion-native-latest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False,indent=2))
    raise SystemExit(0 if result['passed'] else 1)
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
