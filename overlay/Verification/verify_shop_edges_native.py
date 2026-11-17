"""Raycast real goods and native shop/page arrows using isolated accounts."""
from pathlib import Path
import datetime,hashlib,json,os,subprocess,sys,time
ROOT=Path(__file__).resolve().parents[1];baseline='--baseline' in sys.argv
folder=ROOT/'Verification'/('shop-edges-'+('baseline-' if baseline else '')+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
game=ROOT/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest';fixture=client=None
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
    try:
        fixture=subprocess.Popen(['dotnet',str(ROOT/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--competitive-fixture',str(folder)],env=dict(os.environ,BNSM_WORLD_TEST_STATE='empty',BNSM_SHOP_TEST_FUNDS='1'),stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
        until=time.monotonic()+60
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Shop fixture did not start: '+str(folder))
            time.sleep(.2)
        port=json.loads((folder/'endpoints.json').read_text())['directoryPort']
        args=[str(game/'BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-shop-edge-test','-bnsm-shop-edge-output='+str(folder),'-bnsm-test-directory-port='+str(port),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
        if baseline:args.append('-bnsm-shop-edge-baseline')
        client=subprocess.Popen(args,creationflags=subprocess.CREATE_NO_WINDOW);client.wait(timeout=300)
        result=json.loads((folder/'shop-edges.json').read_text('utf-8-sig'))
        result.update(folder=str(folder),nativeExitCode=client.returncode,productionSaveUsed=False,
            clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),serverCoreSha256=hashlib.sha256((ROOT/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest(),
            sourceSha256=hashlib.sha256((ROOT/'Project/Assets/Scripts/Assembly-CSharp/LocalShopNativeUI.cs').read_bytes()).hexdigest())
        result['passed'] &= client.returncode==0
        (ROOT/'Verification'/('shop-edges-baseline.json' if baseline else 'shop-edges-native-latest.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(json.dumps(dict(passed=result['passed'],folder=str(folder),checks=len(result['checks']),failed=[k for k,v in result['checks'].items() if not v],overlapReproduced=result['overlapReproduced'],errors=result['errors']),ensure_ascii=False),flush=True)
        raise SystemExit(0 if result['passed'] else 1)
    finally:
        for p in [client,fixture]:
            if p is not None and p.poll() is None:
                p.terminate()
                try:p.wait(timeout=8)
                except subprocess.TimeoutExpired:p.kill();p.wait(timeout=8)
