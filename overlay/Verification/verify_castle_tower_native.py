from pathlib import Path
import datetime,hashlib,json,subprocess,time
root=Path(__file__).resolve().parents[1]
folder=root/'Verification'/('castle-tower-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
 fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--castle-tower-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
 try:
  until=time.monotonic()+60
  while not (folder/'endpoints.json').exists():
   if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
   time.sleep(.2)
  endpoint=json.loads((folder/'endpoints.json').read_text());game=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
  client=subprocess.Popen([str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-castle-tower-test','-bnsm-castle-tower-output='+str(folder),'-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
  until=time.monotonic()+330
  while not (folder/'castle-tower-native.json').exists():
   if (folder/'errors.jsonl').exists() and (folder/'errors.jsonl').stat().st_size:raise RuntimeError('Native exception '+str(folder))
   if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Native probe did not finish '+str(folder))
   time.sleep(.3)
  result=json.loads((folder/'castle-tower-native.json').read_text(encoding='utf-8-sig'));client.wait(timeout=20)
  result.update(folder=str(folder),nativeExitCode=client.returncode,clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
  player=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'))[0];tower=player['ChallengeTower'];competitive=player['Competitive']
  result['persistedIdleAfterValidRetreat']=player['ActiveStage'] is None and not tower['ActiveStamp'] and not tower['ActiveId'] and not tower['BattleCards'] and not tower['UsedEntries'] and not tower['Points'] and not tower['DeadCards']
  result['allOwnedCastlesRetained']=len(competitive['Castles'])==26 and all(x['Status']==1 for x in competitive['Castles'].values()) and not competitive['ActiveMode']
  result['passed'] &= result['persistedIdleAfterValidRetreat'] and result['allOwnedCastlesRetained'] and client.returncode==0
  (root/'Verification/castle-tower-native-latest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False))
  raise SystemExit(0 if result['passed'] else 1)
 finally:
  for process in [client,fixture]:
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=8)
    except subprocess.TimeoutExpired:process.kill()
