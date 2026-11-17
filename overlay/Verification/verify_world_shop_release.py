"""Bind the delivered PC/Android UI update to its native and package evidence."""
from pathlib import Path
import datetime, hashlib, json, zipfile

ROOT = Path(__file__).resolve().parents[1]
def read(name): return json.loads((ROOT / name).read_text('utf-8-sig'))
def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

win = read('Verification/windows-full-release.json')
android = read('Builds/Android-Integrated/latest.json')
manager = read('Builds/Android-SaveManager/latest.json')
shop = read('Verification/shop-edges-native-latest.json')
world = read('Verification/world-ui-native-latest.json')
viewport = read('Verification/viewport-latest.json')
infinite = read('Verification/infinite-defence-native-latest.json')
for item in [win, android, shop, world, viewport, infinite]: assert item['passed']
assert sha(win['archive']) == win['archiveSha256']
assert sha(android['apk']) == android['sha256']
for item in [shop, world, viewport, infinite]:
    assert item['clientAssemblySha256'] == win['clientAssemblySha256']
assert sha(win['client'].replace('BnsmLocal.exe', 'BnsmLocal_Data/Managed/Assembly-CSharp.dll')) == win['clientAssemblySha256']
assert android['shopLayoutSourceSha256'] == shop['sourceSha256'] == sha(ROOT / 'Project/Assets/Scripts/Assembly-CSharp/LocalShopNativeUI.cs')
assert world['all25AuthoredTerritoriesUnchanged'] and world['persistedExactFifteenSlots']
assert manager['gameSha256'] == android['sha256'] and sha(manager['apk']) == manager['sha256']
assert not android['deviceVerified'] and not android['installed']
with zipfile.ZipFile(win['archive']) as archive:
    note = archive.read('BnsmLocal-Windows-x64/README.txt').decode('utf-8-sig')
    assert '2026-11-09 界面更新' in note and '普通商店' in note

def authored(path):
    rows = json.loads(path.read_text('utf-8-sig'))
    parents = {x['path'].rsplit('/', 1)[0] for x in rows if 'WorldWarSpot' in x['components']}
    return {x['path'].split('/Map/', 1)[1]: x['pos'] for x in rows if x['path'] in parents}
baseline = read('Verification/world-ui-baseline.json')
original = authored(Path(baseline['folder']) / 'world-map-0.json')
assert len(original) == 25
assert all(authored(Path(world['folder']) / ('world-map-' + str(i) + '.json')) == original for i in range(5))

result = dict(passed=True, checkedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    scope=['PC world-war map and three-team defence editor', 'Shared PC/Android ordinary-shop border goods and arrow hit regions'],
    windows={k: win[k] for k in ['archive', 'archiveBytes', 'archiveSha256', 'clientAssemblySha256', 'serverCoreSha256', 'selfContainedRuntimeVerified']},
    android={k: android[k] for k in ['apk', 'versionCode', 'sha256', 'bytes', 'embeddedCoreSha256', 'signatureVerified', 'apk16KiBAligned', 'elf16KiBAligned', 'originalLauncherIconVerified', 'deviceVerified', 'installed']},
    checks=dict(ordinaryShopNative=len(shop['checks']), worldUiNative=len(world['checks']),
        all25TerritoryPositionsPreserved=True, fifteenDefenceSlotsPersisted=True,
        sharedInfiniteDefence=len(infinite['checks']) + len(infinite['restartChecks']), viewport=len(viewport['checks']),
        realWorldEmptyRosterBattle=read('Verification/competitive-native-world-latest.json')['passed'],
        realWorldDeadRosterRecoveryBattle=read('Verification/competitive-native-world-dead-latest.json')['passed'],
        windowsNative=win['nativeChecks'], windowsNativeErrors=win['nativeErrors'],
        launcherSixtySeconds=read('Verification/windows-full-launcher-latest.json')['passed'],
        androidPortableModeRegressions=android['portableRegressionCases'],
        aospFileContracts=android['androidAospContractChecks'], saveImport=manager['importChecks'], consoleExport=android['consoleExportChecks']),
    bossShopGeometryUnchanged=shop['checks']['bossShopGuttersUnchanged'], productionSaveUsed=False,
    androidVerificationLimit='Compiled shared layout, desktop native phone-aspect geometry and package/contracts verified; no Android device connected',
    sourceHashes={name: sha(ROOT / 'Project/Assets/Scripts/Assembly-CSharp' / name) for name in ['LocalShopNativeUI.cs', 'LocalWorldWarLayout.cs', 'LocalDeckLayout.cs']})
assert result['checks']['windowsNativeErrors'] == 0
assert all(value for key, value in result['checks'].items() if key != 'windowsNativeErrors')
(ROOT / 'Verification/world-shop-ui-release.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(passed=True, windows=win['archive'], android=android['apk'], androidVersion=android['versionCode'], shopChecks=len(shop['checks']), worldUiChecks=len(world['checks']), deviceVerified=False), ensure_ascii=False))
