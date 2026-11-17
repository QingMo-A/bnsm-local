"""Authorized in-place ADB update. Never clears data, uninstalls, launches or touches the UI."""
from pathlib import Path
from datetime import datetime,timezone
import subprocess,json,re,hashlib,time,argparse
ROOT=Path('I:/BnsmModernWork');ADB='C:/Android/platform-tools/adb.exe';SERIAL='c7b5ab5c'
arguments=argparse.ArgumentParser();arguments.add_argument('--game-version',type=int,default=3);arguments.add_argument('--server-version',type=int,default=2);arguments.add_argument('--report',default='phone-support-install.json');options=arguments.parse_args()
if Path(options.report).name!=options.report or not options.report.endswith('.json'):raise ValueError('Report must be a JSON filename')
report={'startedUtc':datetime.now(timezone.utc).isoformat(),'serial':SERIAL,'preserveData':True,'uninstalled':False,'dataCleared':False,'deviceUiOperated':False,'launchedApps':False,'packages':[]}
path=ROOT/'Verification'/options.report
def save():path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
def run(*args,timeout=25):
    result=subprocess.run([ADB,'-s',SERIAL,*args],capture_output=True,text=True,encoding='utf8',errors='replace',timeout=timeout)
    if result.returncode:raise RuntimeError('ADB failed: '+result.stdout+result.stderr)
    return result.stdout
def info(package):
    text=run('shell','dumpsys','package',package)
    data={}
    for key,pattern in [('versionCode',r'\bversionCode=(\d+)'),('versionName',r'\bversionName=([^\r\n]+)'),('userId',r'\buserId=(\d+)'),('dataDir',r'\bdataDir=([^\r\n]+)'),('firstInstallTime',r'\bfirstInstallTime=([^\r\n]+)'),('lastUpdateTime',r'\blastUpdateTime=([^\r\n]+)')]:
        m=re.search(pattern,text)
        if m:data[key]=m.group(1).strip()
    if not data:raise RuntimeError('Package not found: '+package)
    return data
try:
    check=run('get-state');assert check.strip()=='device',check
    for package,apk,expected in [
        ('local.bnsm.serverhost',ROOT/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk',options.server_version),
        ('local.bnsm.game',ROOT/'Builds/Android-ARM64/BnsmLocal-arm64.apk',options.game_version)]:
        before=info(package);row={'package':package,'apk':str(apk),'sha256':hashlib.file_digest(apk.open('rb'),'sha256').hexdigest(),'before':before,'status':'installing'};report['packages'].append(row);save()
        print('Updating '+package+' from '+before.get('versionCode','?')+' to '+str(expected)+' with adb install -r',flush=True)
        started=time.monotonic();output=run('install','-r',str(apk),timeout=600);row['output']=output;row['seconds']=round(time.monotonic()-started,2)
        if 'Success' not in output:raise RuntimeError('Install did not confirm success: '+output)
        after=info(package);row['after']=after
        assert int(after['versionCode'])==expected,after
        for key in ('userId','dataDir','firstInstallTime'):
            if key in before:assert after.get(key)==before[key],(key,before,after)
        row['status']='updated';row['packageIdentityRetained']=True;save();print(package+' updated successfully; application identity/data directory retained',flush=True)
    report['passed']=True
except Exception as error:
    report['passed']=False;report['error']=str(error);raise
finally:
    report['finishedUtc']=datetime.now(timezone.utc).isoformat();save()
