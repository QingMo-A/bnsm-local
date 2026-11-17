import datetime,hashlib,json,pathlib,subprocess,time,sys
root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('viewport-'+(sys.argv[1] if len(sys.argv)>1 else 'native')+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
log=open(folder/'fixture.log','w',encoding='utf-8')
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--desktop-pages-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
client=None
try:
    until=time.monotonic()+60
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoints=json.loads((folder/'endpoints.json').read_text())
    command=[str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-viewport-test','-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-bnsm-viewport-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    client=subprocess.Popen(command,creationflags=subprocess.CREATE_NO_WINDOW);until=time.monotonic()+200
    while not (folder/'viewport-test.json').exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Player did not finish '+str(folder))
        time.sleep(.4)
    result=json.loads((folder/'viewport-test.json').read_text(encoding='utf-8-sig'));result['folder']=str(folder)
    client.wait(timeout=15);result['nativeExitCode']=client.returncode
    result['clientAssemblySha256']=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
    (root/'Verification/viewport-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2));raise SystemExit(0 if result['passed'] and client.returncode==0 else 1)
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
