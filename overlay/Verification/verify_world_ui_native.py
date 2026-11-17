"""Native world-map/three-team editor geometry, input and real saved roster."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,sys,time
ROOT=Path(__file__).resolve().parents[1]
baseline='--baseline' in sys.argv
folder=ROOT/'Verification'/('world-ui-'+('baseline-' if baseline else '')+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
game=ROOT/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
fixture=client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
    try:
        env=dict(os.environ,BNSM_WORLD_TEST_STATE='empty')
        fixture=subprocess.Popen(['dotnet',str(ROOT/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--competitive-fixture',str(folder)],env=env,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        until=time.monotonic()+60
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('World UI fixture did not start: '+str(folder))
            time.sleep(.2)
        port=json.loads((folder/'endpoints.json').read_text())['directoryPort']
        args=[str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-world-ui-test','-bnsm-world-ui-output='+str(folder),'-bnsm-test-directory-port='+str(port),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
        if baseline:args.append('-bnsm-world-ui-baseline')
        client=subprocess.Popen(args,creationflags=subprocess.CREATE_NO_WINDOW);client.wait(timeout=300)
        result=json.loads((folder/'world-ui.json').read_text('utf-8-sig'))
        p=json.loads((folder/'saves/players.json').read_text('utf-8-sig'))[0]
        exact=p['Decks']['34']==[2,1,3,4,5] and p['Decks']['35']==list(range(6,11)) and p['Decks']['36']==list(range(11,16))
        idle=p['Competitive']['ActiveMode']==0 and p['ActiveStage'] is None
        result.update(folder=str(folder),nativeExitCode=client.returncode,productionSaveUsed=False,persistedExactFifteenSlots=exact,unchangedCombatState=idle,
            clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((ROOT/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
        result['passed'] &= client.returncode==0 and idle and (baseline or exact)
        (ROOT/'Verification'/('world-ui-baseline.json' if baseline else 'world-ui-native-latest.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(dict(passed=result['passed'],folder=str(folder),checks=len(result['checks']),failed=[k for k,v in result['checks'].items() if not v],persistedExactFifteenSlots=exact,errors=result['errors']),ensure_ascii=False),flush=True)
        raise SystemExit(0 if result['passed'] else 1)
    finally:
        for process in [client,fixture]:
            if process is not None and process.poll() is None:
                process.terminate()
                try:process.wait(timeout=8)
                except subprocess.TimeoutExpired:process.kill();process.wait(timeout=8)
