import datetime,json,pathlib,subprocess,time,sys,hashlib
root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('document-ui-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
with (folder/'fixture.log').open('w',encoding='utf-8') as log:
    fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--document-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    client=None
    try:
        until=time.monotonic()+70
        while not (folder/'endpoints.json').exists():
            if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed')
            time.sleep(.2)
        endpoint=json.loads((folder/'endpoints.json').read_text(encoding='utf-8-sig'))
        flags=['-bnsm-document-final'] if '--final' in sys.argv else []
        client=subprocess.Popen([str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-document-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-document-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]+flags,creationflags=subprocess.CREATE_NO_WINDOW)
        client.wait(timeout=350)
        result=json.loads((folder/'document-ui.json').read_text(encoding='utf-8-sig'));result['folder']=str(folder)
        result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
        result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
        (root/'Verification/document-ui-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        print(str(folder));print(json.dumps(result));raise SystemExit(0 if result['passed'] and client.returncode==0 else 1)
    finally:
        for p in (client,fixture):
            if p is not None and p.poll() is None:p.terminate()
