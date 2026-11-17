import argparse, datetime, json, pathlib, subprocess, time
root=pathlib.Path('I:/BnsmModernWork')
parser=argparse.ArgumentParser();parser.add_argument('--baseline',action='store_true');args=parser.parse_args()
folder=root/'Verification'/('desktop-pages-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
output=open(folder/'fixture.log','w',encoding='utf-8')
fixture=subprocess.Popen(['dotnet',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--desktop-pages-fixture',str(folder)],stdout=output,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
client=None
try:
    until=time.monotonic()+60
    while not (folder/'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic()>until:raise RuntimeError('Fixture failed '+str(folder))
        time.sleep(.2)
    endpoint=json.loads((folder/'endpoints.json').read_text())
    command=[str(root/'Builds/Windows-DesktopPages/BnsmLocal.exe'),'-bnsm-probe','-bnsm-autoconnect','-bnsm-desktop-pages-test','-bnsm-test-directory-port='+str(endpoint['directoryPort']),'-bnsm-desktop-output='+str(folder),'-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',str(folder/'client.log')]
    if args.baseline:command+=['-bnsm-desktop-baseline']
    client=subprocess.Popen(command)
    until=time.monotonic()+240
    while not (folder/'desktop-pages-test.json').exists():
        if client.poll() is not None or time.monotonic()>until:raise RuntimeError('Player did not finish '+str(folder))
        time.sleep(.4)
    result=json.loads((folder/'desktop-pages-test.json').read_text(encoding='utf-8-sig'));result['folder']=str(folder)
    (root/'Verification/desktop-pages-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps({'passed':result['passed'],'errors':result['errors'],'failedChecks':[k for k,v in result['checks'].items() if not v],'folder':str(folder),'checks':len(result['checks']),'cases':result['cases']},indent=2))
    if not result['passed']:raise SystemExit(1)
finally:
    for process in [client,fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=8)
            except subprocess.TimeoutExpired:process.kill()
    output.close()
