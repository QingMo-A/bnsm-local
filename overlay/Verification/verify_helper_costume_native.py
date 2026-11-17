"""Exercise real shared wardrobe items, activity rewards and all ten native crafts."""
from pathlib import Path
import datetime, hashlib, json, subprocess, time

ROOT=Path(__file__).resolve().parents[1]
FOLDER=ROOT/'Verification'/('helper-costume-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
FOLDER.mkdir()
GAME=ROOT/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
FIXTURE=ROOT/'Verification/HelperTrialTests/bin/Release/net10.0/Bnsm.LocalServer.dll'
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
client=server=None
with (FOLDER/'fixture.log').open('w',encoding='utf-8') as log:
    try:
        server=subprocess.Popen(['dotnet',str(FIXTURE),str(FOLDER)],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        until=time.monotonic()+60
        while not (FOLDER/'endpoints.json').exists():
            if server.poll() is not None or time.monotonic()>until: raise RuntimeError('Fixture failed '+str(FOLDER))
            time.sleep(.2)
        port=json.loads((FOLDER/'endpoints.json').read_text())['directoryPort']
        client=subprocess.Popen([str(GAME/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-helper-costume-test','-bnsm-test-directory-port='+str(port),'-bnsm-helper-output='+str(FOLDER),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(FOLDER/'client.log')],cwd=GAME,creationflags=subprocess.CREATE_NO_WINDOW)
        until=time.monotonic()+230
        while not (FOLDER/'helper-costume-test.json').exists():
            if client.poll() is not None or time.monotonic()>until: raise RuntimeError('Native wardrobe probe did not finish '+str(FOLDER))
            time.sleep(.3)
        report=json.loads((FOLDER/'helper-costume-test.json').read_text('utf-8-sig'));client.wait(timeout=15)
        p=json.loads((FOLDER/'saves/players.json').read_text('utf-8-sig'))[0]
        report.update(folder=str(FOLDER),nativeExitCode=client.returncode,
            clientAssemblySha256=digest(GAME/'BnsmLocal_Data/Managed/Assembly-CSharp.dll'),
            serverCoreSha256=digest(FIXTURE.parent/'Bnsm.ServerCore.dll'),
            activityConfigSha256=digest(ROOT/'WindowsPreview/LocalServer/data/activities.json'),
            iconSha256=digest(ROOT/'Project/Assets/Resources/helper/LocalNightSakura/icons.png'),
            definitionSha256=digest(ROOT/'Project/Assets/Scripts/Assembly-CSharp/LocalHelperCostumeDefinition.cs'),
            persistedPlusTen=all(v==1900030011 for v in p['HelperCostumes'].values()),
            persistedClaimCount=sum(k.startswith('900011/') for k in p['Activities']['PermanentClaims']),
            persistedGold=p['Gold'])
        report['passed']=report['passed'] and all(report['checks'].values()) and report['errors']==0 and client.returncode==0 and not report['previewOverrideUsed'] and report['persistedPlusTen'] and report['persistedClaimCount']==3 and report['persistedGold']==745000
        (ROOT/'Verification/helper-costume-native-latest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
        print(json.dumps(report,ensure_ascii=False,indent=2))
        raise SystemExit(0 if report['passed'] else 1)
    finally:
        for process in [client,server]:
            if process is not None and process.poll() is None:
                process.terminate()
                try: process.wait(timeout=8)
                except subprocess.TimeoutExpired: process.kill()
