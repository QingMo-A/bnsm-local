"""Exercise the shipped Windows host, using only an isolated save and owned process."""
from pathlib import Path
import io,json,socket,subprocess,time,urllib.request,uuid,zipfile,hashlib
ROOT=Path('I:/BnsmModernWork')
fixture=ROOT/'Verification'/('support-windows-'+uuid.uuid4().hex);fixture.mkdir()
exe=ROOT/'WindowsPreview/LocalServer/WindowsBots/Bnsm.LocalServer.exe'
checks=[]
def check(value,text):
    assert value,text
    checks.append(text)
with socket.socket() as reservation:
    reservation.bind(('127.0.0.1',0));port=reservation.getsockname()[1]
def get(path):
    with urllib.request.urlopen(f'http://127.0.0.1:{port}'+path,timeout=10) as response:return response.read()
def state():return json.loads(get('/debug/logs'))
with (fixture/'host-output.log').open('wb') as output:
    process=subprocess.Popen([str(exe),'--save-root',str(fixture/'managed'),'--legacy-save-directory',str(fixture/'no-legacy'),
        '--directory-port','0','--login-port','0','--game-port','0','--debug-port',str(port)],stdout=output,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        deadline=time.monotonic()+20
        while True:
            if process.poll() is not None:raise RuntimeError('Host exited during startup')
            try:health=json.loads(get('/health'));break
            except OSError:
                if time.monotonic()>deadline:raise
                time.sleep(.1)
        check('导出日志 ZIP' in get('/diagnostics').decode(),'Packaged host serves the Chinese log viewer')
        check(state()['ok'] and state()['activeGameConnections']==0,'Packaged host begins with zero game connections')
        with socket.create_connection(('127.0.0.1',health['gamePort']),timeout=5) as peer:
            for _ in range(50):
                connected=state()
                if connected['activeGameConnections']==1:break
                time.sleep(.05)
            check(connected['activeGameConnections']==1 and 'connected' in connected['text'],'Game connection opens are counted and logged')
        for _ in range(50):
            disconnected=state()
            if disconnected['activeGameConnections']==0:break
            time.sleep(.05)
        check(disconnected['activeGameConnections']==0 and 'disconnected' in disconnected['text'],'Game disconnects are counted and logged')
        archive=get('/debug/logs/export');(fixture/'exported-logs.zip').write_bytes(archive)
        with zipfile.ZipFile(io.BytesIO(archive)) as logs:
            check(logs.testzip() is None and {'diagnostics.json','recent-tail.log'}.issubset(logs.namelist()),'Packaged log export has valid ZIP contents')
            check(all(x.endswith('.log') or x=='diagnostics.json' for x in logs.namelist()),'Log export contains diagnostics only, with no save files')
        command=subprocess.run([str(exe),'--save-root',str(fixture/'managed'),'--save-command','list'],capture_output=True,text=True,encoding='utf8',timeout=10,creationflags=subprocess.CREATE_NO_WINDOW)
        check(command.returncode==0 and json.loads(command.stdout)['profiles'][0]['busy'],'Live profile list remains machine-readable and reports the running lease')
    finally:
        process.terminate();process.wait(timeout=10)
report={'passed':True,'checks':checks,'fixture':str(fixture),'realSaveChanged':False,'exeSha256':hashlib.file_digest(exe.open('rb'),'sha256').hexdigest(),
    'coreSha256':hashlib.file_digest((exe.parent/'Bnsm.ServerCore.dll').open('rb'),'sha256').hexdigest()}
(ROOT/'Verification/phone-support-windows.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False,indent=2))
