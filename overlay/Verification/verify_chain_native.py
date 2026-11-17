"""Exercise the original 武神之路 entry and a real native settlement."""
import hashlib
import json
import subprocess
import time
from datetime import datetime
from pathlib import Path

root = Path(__file__).resolve().parents[1]
folder = root / 'Verification' / ('chain-native-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
folder.mkdir()
fixture_dll = root / 'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'
game = root / 'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'


def wait_json(path, process, timeout):
    deadline = time.monotonic() + timeout
    while not path.exists():
        if process.poll() is not None or time.monotonic() >= deadline:
            raise RuntimeError(f'Timed out or process exited waiting for {path}')
        time.sleep(.25)
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


client = None
with (folder / 'fixture.log').open('w', encoding='utf-8') as fixture_log:
    fixture = subprocess.Popen(
        ['dotnet', str(fixture_dll), '--chain-fixture', str(folder)],
        cwd=root, stdout=fixture_log, stderr=subprocess.STDOUT,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    try:
        port = wait_json(folder / 'endpoints.json', fixture, 40)['directoryPort']
        client = subprocess.Popen(
            [str(game / 'BnsmLocal.exe'), '-bnsm-probe', '-bnsm-autoconnect', '-bnsm-chain-test',
             f'-bnsm-test-directory-port={port}', f'-bnsm-competitive-output={folder}',
             '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
             '-logFile', str(folder / 'client.log')], cwd=game, creationflags=subprocess.CREATE_NO_WINDOW,
        )
        report = wait_json(folder / 'chain-native.json', client, 280)
        client.wait(timeout=20)
        player = json.loads((folder / 'saves/players.json').read_text(encoding='utf-8-sig'))[0]
        chain = player.get('ChainBattle', {})
        report['settlementSaved'] = chain.get('Step') == 2 and chain.get('Wins') == 1 and bool(chain.get('Cards'))
        report['activeStageCleared'] = player['ActiveStage'] is None
        report['passed'] &= report['settlementSaved'] and report['activeStageCleared'] and client.returncode == 0
        report['clientAssemblySha256'] = digest(game / 'BnsmLocal_Data/Managed/Assembly-CSharp.dll')
        report['serverCoreSha256'] = digest(fixture_dll.with_name('Bnsm.ServerCore.dll'))
        report['folder'] = str(folder)
        (root / 'Verification/chain-native-latest.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report, indent=2), flush=True)
        if not report['passed']:
            raise RuntimeError('Native chain battle failed')
    finally:
        for process in (client, fixture):
            if process is not None and process.poll() is None:
                process.terminate()
                process.wait(timeout=15)
