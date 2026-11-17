from pathlib import Path
import datetime,json,subprocess,time

root=Path('I:/BnsmModernWork');stage=root/'Builds/.staging/Windows'
fixture=root/'Verification'/('compact-audio-player-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));fixture.mkdir()
client=None
with (fixture/'fixture.log').open('wb') as log:
 host=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--desktop-pages-fixture',str(fixture)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
 try:
  until=time.monotonic()+45
  while not (fixture/'endpoints.json').exists():
   if host.poll() is not None or time.monotonic()>until:raise RuntimeError('Audio fixture failed')
   time.sleep(.25)
  endpoints=json.loads((fixture/'endpoints.json').read_text())
  before=set((stage/'evidence').glob('audio-player-*')) if (stage/'evidence').exists() else set()
  client=subprocess.Popen([str(stage/'UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-audio-test','-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(fixture/'client.log')],creationflags=subprocess.CREATE_NO_WINDOW)
  until=time.monotonic()+180;report_file=None
  while report_file is None:
   for folder in set((stage/'evidence').glob('audio-player-*'))-before:
    candidate=folder/'audio-player-test.json'
    if candidate.exists():report_file=candidate;break
   if report_file is None:
    if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Native audio verification did not finish: '+str(fixture))
    time.sleep(.4)
  result=json.loads(report_file.read_text(encoding='utf-8-sig'));client.wait(timeout=20)
  result.update(fixture=str(fixture),evidence=str(report_file),nativeExitCode=client.returncode,productionSaveUsed=False)
  assert result['passed'] and result['errors']==0 and client.returncode==0,result
  (root/'Verification/compact-audio-native.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
  print(json.dumps(result,ensure_ascii=False,indent=2))
 finally:
  for p in [client,host]:
   if p is not None and p.poll() is None:
    p.terminate()
    try:p.wait(timeout=8)
    except subprocess.TimeoutExpired:p.kill();p.wait(timeout=8)
