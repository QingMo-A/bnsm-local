"""Read-only evidence plus bounded lifecycle testing of our isolated research APK."""
from pathlib import Path
import datetime,hashlib,json,re,subprocess,time

root=Path(__file__).resolve().parents[1]
package='local.bnsm.inprocessprobe';activity=package+'/com.unity3d.player.UnityPlayerActivity'
folder=root/'Verification'/('android-inprocess-phone-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
device_root='/sdcard/Android/data/'+package+'/files/Research/'
def adb(*args,check=True):return subprocess.run(['C:/Android/platform-tools/adb.exe',*args],capture_output=True,check=check).stdout.decode('utf-8',errors='replace').strip()
def read(name):return json.loads(adb('shell','cat',device_root+name))
def pid():return adb('shell','pidof',package)
def sample():return dict(pid=pid(),heartbeat=read('heartbeat.json'))
def screenshot(name):
    with (folder/name).open('wb') as stream:subprocess.run(['C:/Android/platform-tools/adb.exe','exec-out','screencap','-p'],stdout=stream,check=True)
def versions():
    answer={}
    for p in ['local.bnsm.game','local.bnsm.serverhost']:
        text=adb('shell','dumpsys','package',p);match=re.search(r'versionCode=(\d+)',text);answer[p]=int(match.group(1)) if match else None
    return answer
before_versions=versions();native=read('unity-evidence.json');protocol=read('result.json');initial=sample()
assert native['passed'] and protocol['passed'],(native,protocol)
assert len(initial['pid'].split())==1 and int(initial['pid'])==native['processId']
screenshot('foreground.png')
service=adb('shell','dumpsys','activity','services',package);(folder/'foreground-service.txt').write_text(service,'utf-8')
print('Foreground protocol and same-process checks passed. Testing Home for 30 seconds.',flush=True)
adb('shell','input','keyevent','KEYCODE_HOME')
time.sleep(30)
background=sample();background_service=adb('shell','dumpsys','activity','services',package);(folder/'background-service.txt').write_text(background_service,'utf-8')
print('Home test captured. Testing screen sleep for 15 seconds.',flush=True)
adb('shell','input','keyevent','KEYCODE_SLEEP')
time.sleep(15)
asleep=sample();adb('shell','input','keyevent','KEYCODE_WAKEUP');adb('shell','am','start','-n',activity)
time.sleep(5)
resumed=sample();screenshot('resumed.png')
lifecycle=adb('shell','cat',device_root+'lifecycle.log');(folder/'lifecycle.log').write_text(lifecycle,'utf-8')
error=adb('shell','cat',device_root+'heartbeat-error.txt',check=False)
legacy_server=adb('shell','pidof','local.bnsm.serverhost',check=False)
checks=dict(nativeProtocolPassed=protocol['passed'],nativeUnityPassed=native['passed'],singleProcess=all(x['pid']==initial['pid'] for x in [background,asleep,resumed]),clientStaticStateIsolated=native['clientSeedIndependent'],serverRunsOffUnityMainThread=native['unityMainThread']!=native['serverWorkerThread'],foregroundServiceSameProcess='isForeground=true' in service and ' '+initial['pid']+':' in service,homeHeartbeatsAdvance=background['heartbeat']['count']>=initial['heartbeat']['count']+8,serviceRemainsForeground='isForeground=true' in background_service,resumedHeartbeatsAdvance=resumed['heartbeat']['count']>asleep['heartbeat']['count'],unityReallyPaused='paused=True' in lifecycle,noHeartbeatErrors=not error,oldCompanionNotRunning=not legacy_server,oldInstalledVersionsPreserved=versions()==before_versions,normalSaveUnused=not protocol['normalSaveUsed'])
apk=Path('C:/BnsmModernBuildCache/InProcessProbe/probe.apk')
report=dict(passed=all(checks.values()),checks=checks,package=package,apk=str(apk),apkSha256=hashlib.sha256(apk.read_bytes()).hexdigest(),bytes=apk.stat().st_size,androidApi=adb('shell','getprop','ro.build.version.sdk'),cachedAppsFreezerSetting=adb('shell','settings','get','global','cached_apps_freezer'),oldVersions=before_versions,native=native,protocol=protocol,initial=initial,background=background,asleep=asleep,resumed=resumed,scope='Minimal IL2CPP Unity in-process server; encrypted protocol, persistence and short lifecycle check. Full game UI/combat and long-duration vendor testing not verified.',folder=str(folder))
(folder/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8');(root/'Verification/android-inprocess-phone-latest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
adb('shell','am','force-stop',package)
print(json.dumps(dict(passed=report['passed'],checks=checks,folder=str(folder)),ensure_ascii=False),flush=True)
if not report['passed']:raise SystemExit(1)
