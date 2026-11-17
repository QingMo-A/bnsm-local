import datetime,hashlib,json,pathlib,subprocess,sys,time
root=pathlib.Path('I:/BnsmModernWork');mode=sys.argv[1] if len(sys.argv)>1 else 'rank'
folder=root/'Verification'/('manual-pvp-native-'+mode+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
log=open(folder/'fixture.log','w',encoding='utf-8');client=None
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--manual-pvp-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
try:
    until=time.monotonic()+60
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoint=json.loads((folder/'endpoints.json').read_text())
    args=[str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-manual-pvp-test','-bnsm-manual-pvp-output='+str(folder),'-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    if mode=='balance':args+=['-bnsm-manual-pvp-balance']
    client=subprocess.Popen(args,creationflags=subprocess.CREATE_NO_WINDOW);until=time.monotonic()+300
    while not (folder/'manual-pvp-native.json').exists():
        if (folder/'errors.jsonl').exists() and (folder/'errors.jsonl').stat().st_size:raise RuntimeError('Native exception '+str(folder))
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Native test did not finish '+str(folder))
        time.sleep(.3)
    result=json.loads((folder/'manual-pvp-native.json').read_text(encoding='utf-8-sig'));client.wait(timeout=15)
    result['folder']=str(folder);result['nativeExitCode']=client.returncode
    result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
    result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
    player=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'))[0];state=player['Competitive'];record=state['Records'][str(result['mode'])]
    result['oneRealSettlementAfterRetreat']=record['Battles']==1 and record['Used']==2 and state['ActiveMode']==0 and not state['ReserveCards'] and player['ActiveStage'] is None
    result['oneRestoredActivityCredit']=player['Activities']['Weekly'].get('MCT_ENTER_RANKING_PVP' if mode=='rank' else 'MCT_ENTER_BALANCE_PVP')==1
    result['passed'] &= result['oneRestoredActivityCredit']
    subprocess.run(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--manual-pvp-replay-audit',str(folder)],check=True,capture_output=True,timeout=25)
    result['actualReplay']=json.loads((folder/'actual-replay-audit.json').read_text())
    replay=result['actualReplay'];result['passed'] &= replay['playerCards']==8 and replay['enemyCards']==8 and replay['allEntrantsFullSp'] and replay['enemySubstitutions']>=1
    result['passed'] &= result['oneRealSettlementAfterRetreat'] and client.returncode==0
    (root/('Verification/manual-pvp-native-'+mode+'-latest.json')).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False),flush=True)
    if not result['passed']:raise RuntimeError('Native manual PvP failed '+str(folder))
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
