from pathlib import Path
import datetime,hashlib,json,subprocess,time
root=Path(__file__).resolve().parents[1];folder=root/'Verification'/('world-raid-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir();client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
 fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--world-raid-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
 try:
  until=time.monotonic()+60
  while not (folder/'endpoints.json').exists():
   if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
   time.sleep(.2)
  endpoint=json.loads((folder/'endpoints.json').read_text());game=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
  client=subprocess.Popen([str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-world-raid-test','-bnsm-world-raid-output='+str(folder),'-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
  until=time.monotonic()+540
  while not (folder/'world-raid-native.json').exists():
   if (folder/'errors.jsonl').exists() and (folder/'errors.jsonl').stat().st_size:raise RuntimeError('Native exception '+str(folder))
   if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Native probe did not finish '+str(folder))
   time.sleep(.3)
  result=json.loads((folder/'world-raid-native.json').read_text('utf-8-sig'));client.wait(timeout=20)
  result.update(folder=str(folder),nativeExitCode=client.returncode,clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
  player=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0];raids=player['Raids']
  result['bothActualDamageRecords']=player['Ranking']['WorldBest']>0 and player['Ranking']['ComboBest']>0 and len(raids['BestDamage'])==2 and len(raids['BestDecks'])==2
  result['twoEntriesEachWithoutExtraDebit']=raids['Used']['2']==2 and raids['Used']['3']==2 and raids['CoolResets']['2']==1 and raids['CoolResets']['3']==1
  result['persistedIdleAfterSettlement']=player['ActiveStage'] is None and raids['ActiveMode']==0 and raids['ActiveStamp']==0 and not raids['BattleCards']
  result['passed'] &= result['bothActualDamageRecords'] and result['twoEntriesEachWithoutExtraDebit'] and result['persistedIdleAfterSettlement'] and client.returncode==0
  (root/'Verification/world-raid-native-latest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8');print(json.dumps(result,ensure_ascii=False));raise SystemExit(0 if result['passed'] else 1)
 finally:
  for process in [client,fixture]:
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=8)
    except subprocess.TimeoutExpired:process.kill()
