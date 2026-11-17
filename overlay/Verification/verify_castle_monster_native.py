"""Run actual castle monster summon/retreat/combat, and resident online UI."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,time

root=Path(__file__).resolve().parents[1]
stage=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
folder=root/'Verification'/('castle-monster-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
environment=os.environ.copy();environment['PATH']='C:/Program Files/dotnet;'+environment.get('PATH','')
client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
 fixture=subprocess.Popen(['C:/Program Files/dotnet/dotnet.exe',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--castle-monster-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,env=environment,creationflags=subprocess.CREATE_NO_WINDOW)
 try:
  until=time.monotonic()+60
  while not (folder/'endpoints.json').exists():
   if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
   time.sleep(.2)
  endpoint=json.loads((folder/'endpoints.json').read_text(encoding='utf-8-sig'))
  client=subprocess.Popen([str(stage/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-castle-monster-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-monster-output='+str(folder),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(folder/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
  client.wait(timeout=430)
  result=json.loads((folder/'castle-monster-native.json').read_text(encoding='utf-8-sig'))
  players=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'));owner=next(p for p in players if p['Account']=='local-player');spots=owner['Competitive']['Castles']
  result['persistedActualCooperativeDamage']=0<spots['1/13']['Raid']['Hp']<484690 and len(spots['1/13']['Raid']['History'])==2 and len(spots['1/13']['Raid']['BotHelpers'])==1
  result['persistedActualFinalClear']=spots['1/22']['Raid']['State']==3 and spots['1/22']['Raid']['Hp']==0 and len(spots['1/22']['Raid']['History'])==1
  result['persistedIdle']=owner['ActiveStage'] is None and owner['ActiveBattleStamp']==0 and owner['Raids']['ActiveStamp']==0
  result.update(folder=str(folder),nativeExitCode=client.returncode,clientAssemblySha256=hashlib.sha256((stage/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
  result['passed'] &= client.returncode==0 and all(result[k] for k in ['persistedActualCooperativeDamage','persistedActualFinalClear','persistedIdle'])
  (root/'Verification/castle-monster-native-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
  print(json.dumps(result),flush=True);raise SystemExit(0 if result['passed'] else 1)
 finally:
  for process in [client,fixture]:
   if process is not None and process.poll() is None:
    process.terminate()
    try:process.wait(timeout=8)
    except subprocess.TimeoutExpired:process.kill()
