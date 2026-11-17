import json, pathlib, subprocess, time, datetime

ROOT = pathlib.Path('I:/BnsmModernWork')
folder = ROOT / 'Verification' / ('faction-player-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
folder.mkdir(parents=True)
fixture_log = open(folder / 'fixture.log', 'w', encoding='utf-8')
fixture = subprocess.Popen(['dotnet', str(ROOT / 'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'), '--faction-fixture', str(folder)], stdout=fixture_log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
client = None
try:
    until = time.monotonic() + 60
    while not (folder / 'endpoints.json').exists():
        if fixture.poll() is not None or time.monotonic() > until:
            raise RuntimeError('Fixture failed: ' + str(folder))
        time.sleep(.3)
    endpoints = json.loads((folder / 'endpoints.json').read_text())
    client = subprocess.Popen([str(ROOT / 'Builds/Windows-Competition/BnsmLocal.exe'), '-bnsm-probe', '-bnsm-autoconnect', '-bnsm-faction-test', '-bnsm-test-directory-port=' + str(endpoints['directoryPort']), '-bnsm-faction-output=' + str(folder), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', str(folder / 'client.log')])
    until = time.monotonic() + 200
    report = folder / 'faction-player-test.json'
    while not report.exists():
        if client.poll() is not None or time.monotonic() > until:
            raise RuntimeError('Native player did not finish: ' + str(folder))
        time.sleep(.5)
    result = json.loads(report.read_text(encoding='utf-8-sig'))
    result['fixture'] = str(folder)
    (ROOT / 'Verification/faction-player-test.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if not result.get('passed'):
        raise RuntimeError('Native faction battle failed')
finally:
    for process in [client, fixture]:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
    fixture_log.close()
