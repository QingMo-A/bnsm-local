import datetime,hashlib,json,pathlib,subprocess,time,sys
root=pathlib.Path('I:/BnsmModernWork')
import os
mode=sys.argv[1] if len(sys.argv)>1 else 'arena'
folder=root/'Verification'/('competitive-native-'+mode+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
log=open(folder/'fixture.log','w',encoding='utf-8');client=None
ladder_rank=next((int(arg.split('=',1)[1]) for arg in sys.argv[2:] if arg.startswith('--ladder-rank=')),None)
environment=os.environ.copy()
if ladder_rank:environment['BNSM_LADDER_TEST_RANK']=str(ladder_rank)
world_state=next((arg.split('=',1)[1] for arg in sys.argv[2:] if arg.startswith('--world-state=')),'empty')
if mode=='world':environment['BNSM_WORLD_TEST_STATE']=world_state
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--competitive-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW,env=environment)
try:
    endpoint=folder/'endpoints.json';until=time.monotonic()+60
    while not endpoint.exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoints=json.loads(endpoint.read_text())
    command=[str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-competitive-test','-bnsm-competitive-mode='+mode,'-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-bnsm-competitive-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    if mode=='world':command.append('-bnsm-world-test-state='+world_state)
    client=subprocess.Popen(command,creationflags=subprocess.CREATE_NO_WINDOW);until=time.monotonic()+210
    report=folder/'competitive-native.json'
    while not report.exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Player did not finish '+str(folder))
        time.sleep(.4)
    result=json.loads(report.read_text(encoding='utf-8-sig'));client.wait(timeout=15);result['nativeExitCode']=client.returncode;result['folder']=str(folder);result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
    result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
    player=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'))[0];state=player['Competitive']
    if mode in ['arena','rank','balance','infinite']:
        number={'arena':'12','rank':'10','balance':'42','infinite':'31'}[mode];result['persistedSettlement']=state['Records'].get(number,{}).get('Battles')==(151 if ladder_rank else 1)
        if ladder_rank:result['ladderStartingRank']=ladder_rank;result['ladderFinalRank']=state['Records'][number]['Rank'];result['normalSaveUsed']=False
        if mode in ['arena','rank','balance']:
            counter={'arena':'MCT_ENTER_ARENA_PVP','rank':'MCT_ENTER_RANKING_PVP','balance':'MCT_ENTER_BALANCE_PVP'}[mode]
            result['oneRestoredActivityCredit']=player['Activities']['Weekly'].get(counter)==1
            result['passed'] &= result['oneRestoredActivityCredit']
    elif mode=='castle':result['persistedSettlement']=state['Castles']['1/1']['Status']==1
    else:
        result['persistedSettlement']=state['WorldScores']['6']['Daily']>0 and state['ActiveMode']==0
        if 'WorldDifficulty' in state:
            result['persistedVictory']=state['WorldDifficulty'].get('Wins')==1
            result['persistedAbsoluteSp']=any(card.get('Sp',0)>1 for card in state['WorldCards'].values())
            result['passed'] &= result['persistedVictory'] and result['persistedAbsoluteSp']
    if mode=='arena':result['ammunitionConsumedOnce']=next(x for x in player['EquipmentItems'] if x['Sequence']==player['CardStates']['1']['WeaponSequence'])['BsBallCount']==40;result['passed'] &= result['ammunitionConsumedOnce']
    result['passed'] &= result['persistedSettlement']
    (root/('Verification/'+('pvp-ladder-native-' if ladder_rank else 'competitive-native-')+mode+('-dead' if mode=='world' and world_state=='dead' else '')+'-latest.json')).write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False),flush=True)
    if not result['passed'] or client.returncode:raise RuntimeError('Native competitive failed '+str(folder))
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
