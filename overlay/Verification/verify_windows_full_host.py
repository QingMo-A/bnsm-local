from pathlib import Path
import io,json,socket,subprocess,time,urllib.request,uuid,zipfile,os
root=Path('I:/BnsmModernWork');package=root/'Builds/.staging/Windows';exe=package/'LocalServer/WindowsLatest/Bnsm.LocalServer.exe'
fixture=root/'Verification'/('windows-full-host-'+uuid.uuid4().hex);fixture.mkdir()
checks=[]
def check(value,note):
 assert value,note
 checks.append(note)
for name in ['coreclr.dll','hostfxr.dll','hostpolicy.dll','Bnsm.ServerCore.dll']:check((exe.parent/name).is_file(),'Bundled '+name)
environment=os.environ.copy();environment['DOTNET_ROOT']=str(fixture/'no-global-dotnet');environment['DOTNET_ROOT_X64']=environment['DOTNET_ROOT'];environment['PATH']=str(Path(os.environ.get('SystemRoot','C:/Windows'))/'System32')
with socket.socket() as reserved:reserved.bind(('127.0.0.1',0));port=reserved.getsockname()[1]
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
def get(path):
 with opener.open('http://127.0.0.1:'+str(port)+path,timeout=10) as response:return response.read()
with (fixture/'host.log').open('wb') as log:
 process=subprocess.Popen([str(exe),'--content-root',str(package),'--save-root',str(fixture/'managed'),'--legacy-save-directory',str(fixture/'no-legacy'),'--directory-port','0','--login-port','0','--game-port','0','--debug-port',str(port)],cwd=package,stdout=log,stderr=subprocess.STDOUT,env=environment,creationflags=subprocess.CREATE_NO_WINDOW)
 try:
  until=time.monotonic()+40
  while True:
   if process.poll() is not None:raise RuntimeError('Self-contained host exited')
   try:health=json.loads(get('/health'));break
   except OSError:
    if time.monotonic()>until:raise
    time.sleep(.15)
  check(health['status']=='ready','Self-contained host starts without dotnet on PATH')
  check(str(Path(health['saveProfile']['directory'])).startswith(str(fixture)),'Host uses isolated test save')
  check(all(f in health['data']['localFeatures'] for f in ['native friend bot duels','overflow-safe mission stamina rewards','native mission title rewards','persistent faction bot battles']),'Packaged host includes current gameplay fixes')
  check('导出日志 ZIP' in get('/diagnostics').decode('utf8'),'Chinese log viewer is available')
  data=get('/debug/logs/export');(fixture/'server-logs.zip').write_bytes(data)
  with zipfile.ZipFile(io.BytesIO(data)) as zip:
   check(zip.testzip() is None and zip.getinfo('diagnostics.json') is not None,'Exported server log ZIP validates')
   check(not any('players' in n or 'bot-world' in n for n in zip.namelist()),'Log export excludes player saves')
  command=subprocess.run([str(exe),'--content-root',str(package),'--save-command','list','--save-root',str(fixture/'managed'),'--legacy-save-directory',str(fixture/'no-legacy')],stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=environment,timeout=20,creationflags=subprocess.CREATE_NO_WINDOW)
  result=json.loads(command.stdout.decode('utf-8-sig'))
  check(command.returncode==0 and result['profiles'][0]['busy'],'Save manager command resolves packaged tables and live profile lease')
 finally:
  if process.poll() is None:process.terminate();process.wait(timeout=10)
report={'passed':True,'checks':checks,'fixture':str(fixture),'normalSaveUsed':False,'selfContainedRuntimeVerified':True}
(root/'Verification/windows-full-host-latest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf8')
print(json.dumps(report,ensure_ascii=False,indent=2))
