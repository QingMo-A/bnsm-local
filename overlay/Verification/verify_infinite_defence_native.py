from pathlib import Path
import datetime,hashlib,json,os,subprocess,time
root=Path(__file__).resolve().parents[1];folder=root/'Verification'/('infinite-defence-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
python_root=root/'Verification/BotWorldTests/bin/Release/net10.0';game=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest';fixture=client=None;stream=None
def stop(process):
 if process is not None and process.poll() is None:
  process.terminate()
  try:process.wait(timeout=8)
  except subprocess.TimeoutExpired:process.kill();process.wait(timeout=8)
try:
 reports=[]
 for relogin in [False,True]:
  endpoint=folder/'endpoints.json'
  if endpoint.exists():endpoint.unlink()
  stream=(folder/('fixture-restart.log' if relogin else 'fixture.log')).open('w',encoding='utf-8')
  fixture=subprocess.Popen(['dotnet',str(python_root/'Bnsm.LocalServer.dll'),'--infinite-defence-fixture',str(folder)],stdout=stream,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
  until=time.monotonic()+60
  while not endpoint.exists():
   if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Defence fixture failed '+str(folder))
   time.sleep(.2)
  port=json.loads(endpoint.read_text())['directoryPort'];name='infinite-defence-relogin.json' if relogin else 'infinite-defence-native.json'
  args=[str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-infinite-defence-test','-bnsm-infinite-defence-output='+str(folder),'-bnsm-test-directory-port='+str(port),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(folder/('client-restart.log' if relogin else 'client.log'))]
  if relogin:args.append('-bnsm-infinite-defence-relogin')
  client=subprocess.Popen(args,creationflags=subprocess.CREATE_NO_WINDOW);until=time.monotonic()+180
  while not (folder/name).exists():
   if (folder/'errors.jsonl').exists() and (folder/'errors.jsonl').stat().st_size:raise RuntimeError('Native exception '+str(folder))
   if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Defence probe failed to finish '+str(folder))
   time.sleep(.2)
  item=json.loads((folder/name).read_text('utf-8-sig'));client.wait(timeout=20);item['nativeExitCode']=client.returncode;reports.append(item)
  if not item['passed'] or client.returncode:raise RuntimeError('Native defence checks failed '+json.dumps(item)+' '+str(folder))
  stop(fixture);fixture=None;stream.close();stream=None
 player=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0];expected=[2,1,0,0,0,7,0,0,0,0,12,0,0,0,0]
 receipts=[x for x in player['Competitive']['Receipts'].values() if x['Name']=='GS_UPDATE_INFINIT_ARENA_DECK_INFO_OK']
 result=dict(passed=all(x['passed'] for x in reports),checks=reports[0]['checks'],restartChecks=reports[1]['checks'],errors=sum(x['errors'] for x in reports),folder=str(folder),persistedExactSlots=player['Decks']['56']==expected,oneSuccessfulSave=len(receipts)==1,unchangedCombatState=player['ActiveStage'] is None and player['Competitive']['ActiveMode']==0,productionSaveUsed=False,clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((python_root/'Bnsm.ServerCore.dll').read_bytes()).hexdigest())
 result['passed'] &= result['persistedExactSlots'] and result['oneSuccessfulSave'] and result['unchangedCombatState']
 (root/'Verification/infinite-defence-native-latest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8');print(json.dumps(result,ensure_ascii=False),flush=True)
 raise SystemExit(0 if result['passed'] else 1)
finally:
 stop(client);stop(fixture)
 if stream is not None:stream.close()
