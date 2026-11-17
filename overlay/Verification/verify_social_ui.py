import datetime,json,pathlib,subprocess,time
root=pathlib.Path('I:/BnsmModernWork')
folder=root/'Verification'/('social-ui-player-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
log=open(folder/'fixture.log','w',encoding='utf-8')
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--desktop-pages-fixture',str(folder)],stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
client=None
try:
    until=time.monotonic()+60
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoints=json.loads((folder/'endpoints.json').read_text())
    command=[str(root/'Builds/Windows-SocialUi/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-social-ui-test','-bnsm-test-directory-port='+str(endpoints['directoryPort']),'-bnsm-social-ui-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    client=subprocess.Popen(command);until=time.monotonic()+300
    while not (folder/'social-ui-test.json').exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Player did not finish '+str(folder))
        time.sleep(.4)
    result=json.loads((folder/'social-ui-test.json').read_text(encoding='utf-8-sig'));result['folder']=str(folder)
    from PIL import Image
    for screenshot in ['tower-deck-70.png','friend-deck-70.png']:
        with Image.open(folder/screenshot) as visual:
            r,g,b=visual.convert('RGB').getpixel((60,180))
            result['checks']['transparentShade-'+screenshot]=max(r,g,b)<240 and b>r
    result['passed']=result['passed'] and all(result['checks'].values())
    (root/'Verification/social-ui-player-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2));raise SystemExit(0 if result['passed'] else 1)
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    log.close()
