import argparse,hashlib,json,pathlib,socket,subprocess,time,urllib.request,uuid
ROOT=pathlib.Path(r'I:\BnsmModernWork')
parser=argparse.ArgumentParser();parser.add_argument('--packaged',action='store_true');options=parser.parse_args()
exe=ROOT/('WindowsPreview/LocalServer/WindowsBots/Bnsm.LocalServer.exe' if options.packaged else 'Builds/WindowsServer/Bnsm.LocalServer.exe')
fixture=ROOT/'Verification'/('bot-windows-'+uuid.uuid4().hex)
fixture.mkdir()
checks=[]
def check(value,text):
    assert value,text
    checks.append(text)
def request(port,path,body=None):
    data=None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(f'http://127.0.0.1:{port}'+path,data=data,headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(req,timeout=10) as r:return json.load(r)
def launch():
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    log=(fixture/('host-'+str(port)+'.log')).open('wb')
    args=[str(exe),'--save-directory',str(fixture/'saves'),'--directory-port','0','--login-port','0','--game-port','0','--debug-port',str(port)]
    if not options.packaged:args+=['--content-root',r'I:\BnsmEssenceBuildWork']
    p=subprocess.Popen(args,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        for _ in range(100):
            if p.poll() is not None:raise RuntimeError('Host exited: '+str(p.returncode))
            try:
                health=request(port,'/health')
                return p,log,port,health
            except OSError:time.sleep(.1)
        raise TimeoutError('Host startup')
    except:
        p.terminate();p.wait(timeout=10);log.close();raise
def stop(p,log):
    p.terminate();p.wait(timeout=10);log.close()
p,log,port,health=launch()
try:
    check(health['status']=='ready' and health['bots']==12,'Self-contained Windows host starts with twelve bots')
    roster=request(port,'/debug/bots')
    check(roster['count']==12 and roster['pvpReady']==False,'Shared bot API reports correct supported scope')
    x=request(port,'/debug/bots/generate',{'count':4,'strength':'mixed'})
    check(x['ok'] and x['count']==16,'Published host generates residents')
    uid=x['bots'][-1]['uid']
    x=request(port,'/debug/bots/configure',{'botUid':uid,'strength':'legend','style':'burst','aiStrength':'regular'})
    bot=next(b for b in x['bots'] if b['uid']==uid)
    check(bot['strength']=='legend' and bot['aiStrength']=='regular' and bot['style']=='burst','Published host configures loadout independently of AI policy')
    cli=subprocess.run(['python','-X','utf8',str(ROOT/'WindowsPreview/Manage-LocalBots.py'),'--port',str(port)],capture_output=True,text=True,encoding='utf8',timeout=20)
    check(cli.returncode==0 and 'Residents: 16' in cli.stdout,'Windows management command lists running residents')
    saved=json.dumps(x,sort_keys=True)
finally:stop(p,log)
p,log,port,health=launch()
try:
    x=request(port,'/debug/bots')
    check(json.dumps(x,sort_keys=True)==saved,'Published Windows host preserves roster and builds after restart')
finally:stop(p,log)
# A raw server fixture has no human login; the normal profile manager creates this file.
(fixture/'saves/players.json').write_text('[]',encoding='utf8')
cmd=[str(exe),'--save-root',str(fixture/'workspace'),'--legacy-save-directory',str(fixture/'saves')]
if not options.packaged:cmd+=['--content-root',r'I:\BnsmEssenceBuildWork']
def save_command(*args):
    r=subprocess.run(cmd+list(args),capture_output=True,text=True,encoding='utf8',timeout=20)
    assert r.returncode==0,r.stderr
    return json.loads(r.stdout)
listed=save_command('--save-command','list')
active=listed['activeProfileId'];archive=fixture/'roster.bnsm-save.zip'
save_command('--save-command','export','--profile-id',active,'--save-file',str(archive))
restored=save_command('--save-command','import','--profile-name','Bot backup','--save-file',str(archive))['profile']['Id']
check((fixture/'workspace/profiles'/restored/'saves/bot-world.json').read_bytes()==(fixture/'saves/bot-world.json').read_bytes(),'Published host save commands back up and restore bot residents')
report={'passed':True,'checks':checks,'fixture':str(fixture),'originalPlayerSaveUsed':False,'packaged':options.packaged,'pvpBattleExecutionTested':False,'exeSha256':hashlib.file_digest(exe.open('rb'),'sha256').hexdigest()}
(ROOT/('Verification/bot-windows-packaged.json' if options.packaged else 'Verification/bot-windows-host.json')).write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps(report,indent=2))
