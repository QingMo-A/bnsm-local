import datetime, hashlib, json, pathlib, subprocess, sys, time

root = pathlib.Path(__file__).resolve().parents[1]
baseline = '--baseline' in sys.argv
folder = root/'Verification'/('skin-shop-'+('baseline-' if baseline else '')+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
folder.mkdir()
game = root/('WindowsPreview' if baseline else 'Builds/.staging/Windows')/'UnityRebuilt/WindowsBuild-Latest'
fixture = client = None
with (folder/'fixture.log').open('w', encoding='utf-8') as log:
    try:
        fixture = subprocess.Popen(['dotnet', str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'), '--document-fixture', str(folder)], stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
        until = time.monotonic()+65
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until: raise RuntimeError('Fixture failed: '+str(folder))
            time.sleep(.2)
        endpoint = json.loads((folder/'endpoints.json').read_text('utf-8-sig'))
        flags = ['-bnsm-document-test', '-bnsm-document-output='+str(folder)] if baseline else ['-bnsm-skin-shop-test', '-bnsm-skin-shop-output='+str(folder)]
        client = subprocess.Popen([str(game/'BnsmLocal.exe'), '-bnsm-probe', '-bnsm-autoconnect', '-bnsm-test-directory-port='+str(endpoint['directoryPort']), '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '-logFile', str(folder/'client.log')]+flags, creationflags=subprocess.CREATE_NO_WINDOW)
        client.wait(timeout=340)
        report_name = 'document-ui.json' if baseline else 'skin-shop.json'
        result = json.loads((folder/report_name).read_text('utf-8-sig'))
        result.update(folder=str(folder), nativeExitCode=client.returncode, clientAssemblySha256=hashlib.sha256((game/'BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(), serverCoreSha256=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest())
        result['passed'] = result['passed'] and client.returncode==0
        (root/'Verification'/('skin-shop-baseline.json' if baseline else 'skin-shop-native-latest.json')).write_text(json.dumps(result, indent=2), 'utf-8')
        print(json.dumps(result, indent=2))
        raise SystemExit(0 if result['passed'] else 1)
    finally:
        for process in (client, fixture):
            if process is not None and process.poll() is None:
                process.terminate()
                try: process.wait(timeout=8)
                except subprocess.TimeoutExpired: process.kill()
