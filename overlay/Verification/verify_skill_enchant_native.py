import datetime,hashlib,json,pathlib,subprocess,time
root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('skill-enchant-native-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
results=[]
for relogin in [False,True]:
    endpoint=folder/'endpoints.json'
    if endpoint.exists():endpoint.unlink()
    log=open(folder/('fixture-relogin.log' if relogin else 'fixture.log'),'w',encoding='utf-8');client=None
    fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--skill-enchant-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        until=time.monotonic()+60
        while not endpoint.exists():
            if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
            time.sleep(.2)
        endpoints=json.loads(endpoint.read_text())
        command=[str(root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-skill-enchant-test','-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-bnsm-skill-enchant-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/('client-relogin.log' if relogin else 'client.log'))]
        if relogin:command+=['-bnsm-skill-enchant-relogin']
        client=subprocess.Popen(command,creationflags=subprocess.CREATE_NO_WINDOW);until=time.monotonic()+150
        report=folder/('skill-enchant-relogin.json' if relogin else 'skill-enchant-native.json')
        while not report.exists():
            if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Player did not finish '+str(folder))
            time.sleep(.4)
        result=json.loads(report.read_text(encoding='utf-8-sig'));client.wait(timeout=15);result['nativeExitCode']=client.returncode;results.append(result)
        print(json.dumps(result,ensure_ascii=False),flush=True)
        if not result['passed'] or client.returncode:raise RuntimeError('Native skill enchant failed '+str(folder))
    finally:
        for process in [client,fixture]:
            if process is not None and process.poll() is None:
                process.terminate()
                try:process.wait(timeout=8)
                except subprocess.TimeoutExpired:process.kill()
        log.close()
result={'passed':True,'folder':str(folder),'runs':results,'productionSaveUsed':False,'clientAssemblySha256':hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()}
(root/'Verification/skill-enchant-native-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
