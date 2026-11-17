from pathlib import Path
import datetime,hashlib,json,subprocess,time
root=Path(__file__).resolve().parents[1];folder=root/'Verification'/('guild-battle-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir();client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
 fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--guild-battle-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
 try:
  until=time.monotonic()+60
  while not (folder/'endpoints.json').exists():
   if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
   time.sleep(.2)
  endpoint=json.loads((folder/'endpoints.json').read_text());game=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
  client=subprocess.Popen([str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-guild-battle-test','-bnsm-guild-battle-output='+str(folder),'-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
  until=time.monotonic()+240
  while not (folder/'guild-battle-native.json').exists():
   if "_FAIL:" in (folder/"fixture.log").read_text(encoding="utf-8",errors="replace"):raise RuntimeError("Unexpected native guild protocol failure "+str(folder))
   if (folder/"errors.jsonl").exists() and (folder/"errors.jsonl").stat().st_size:raise RuntimeError("Native guild exception "+str(folder))
   if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Native guild battle did not finish '+str(folder))
   time.sleep(.3)
  result=json.loads((folder/'guild-battle-native.json').read_text('utf-8-sig'));client.wait(timeout=20)
  result.update(folder=str(folder),nativeExitCode=client.returncode,clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
  player=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0];state=player['Competitive'];guild=player['FoundedGuilds'][0]['Battle']
  result['persistedSettlement']=guild['Battles']==1 and len(guild['History'])==1 and len(guild['ArenaHistory'])==1 and state['Records']['29']['Used']==1 and state['Records']['29']['Battles']==1
  result['persistedReplay']=len(state['Replays'])==1
  result['persistedIdle']=state['ActiveMode']==0 and player['ActiveStage'] is None and player['ActiveBattleStamp']==0
  result['oneActivityCredit']=player['Activities']['Weekly'].get('MCT_ENTER_GUILD_BATTLE')==1
  result['passed'] &= result['persistedSettlement'] and result['persistedIdle'] and result['persistedReplay'] and result['oneActivityCredit'] and client.returncode==0
  (root/'Verification/guild-battle-native-latest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8');print(json.dumps(result,ensure_ascii=False));raise SystemExit(0 if result['passed'] else 1)
 finally:
  for process in [client,fixture]:
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=8)
    except subprocess.TimeoutExpired:process.kill()
