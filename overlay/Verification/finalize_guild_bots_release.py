"""Bind the guild-resident feature evidence to the current published artifacts."""
from pathlib import Path
import datetime
import hashlib
import json

root = Path(__file__).resolve().parents[1]
verification = root / 'Verification'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


manifest = read(root / 'Builds/latest-artifacts.json')
windows = read(verification / 'windows-full-release.json')
mobile = read(verification / 'mobile-publication-latest.json')
assert windows['passed'] and mobile['passed'] and windows['sameServerCoreAsAndroid']
core = windows['serverCoreSha256']
assert digest(root / 'Builds/PhoneServer/apk/Bnsm.ServerCore.dll') == core
assert digest(Path(windows['server']).with_name('Bnsm.ServerCore.dll')) == core
assert manifest['mobile']['game']['versionCode'] == 15
assert manifest['mobile']['server']['versionCode'] == 17
assert windows['clientAssemblySha256'] == 'eb6aadf0f2b11c46232c22736a53789ee4a7076a93717556c8a524a4d59520ce'
names = [
    'guild-bots-server-latest.json', 'guild-protocol-latest.json', 'guild-native-latest.json',
    'pvp-difficulty-server-latest.json', 'competitive-server-latest.json',
    'castle-lifecycle-server-latest.json', 'battle-lifecycle-server-latest.json',
    'document-server-latest.json', 'windows-full-host-latest.json',
    'windows-full-launcher-latest.json', 'windows-cet-latest.json',
]
proofs = {name: read(verification / name) for name in names}
for name, proof in proofs.items():
    assert proof['passed'], name
    if 'serverCoreSha256' in proof:
        assert proof['serverCoreSha256'] == core, name
    if 'clientAssemblySha256' in proof:
        assert proof['clientAssemblySha256'] == windows['clientAssemblySha256'], name
assert proofs['guild-bots-server-latest.json']['runtimeTimerVerified']
assert len(proofs['guild-bots-server-latest.json']['checks']) == 30
assert proofs['guild-protocol-latest.json']['count'] == 95
assert all(not entry['cetCompatible'] for entry in proofs['windows-cet-latest.json']['entries'])
for role in ['game', 'server']:
    item = manifest['mobile'][role]
    assert digest(item['path']) == item['sha256'], role
assert digest(windows['archive']) == windows['archiveSha256']
catalogs = {
    'WindowsPreview/LocalServer/data/gacha.json': 'de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908',
    'Builds/PhoneServer/server-content.zip': 'a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1',
    'Project/Assets/StreamingAssets/DynamicAssets/CDT.bytes': '1ade0ffce7b3b59d4b6fe326be7f089612fd2ea02a7bf49f12bc595712cc2a76',
}
for relative, expected in catalogs.items():
    assert digest(root / relative) == expected, relative
report = {
    'passed': True, 'release': 17,
    'verifiedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'scope': 'Resident guild invitations, normal member slots, timer-driven attendance and daily contribution',
    'normalSaveUsed': False, 'androidDeviceVerified': False, 'gameClientUnchanged': True,
    'androidGameVersionCode': 15, 'androidServerVersionCode': 17,
    'sameServerCoreAsAndroid': True, 'sharedServerCoreSha256': core,
    'clientAssemblySha256': windows['clientAssemblySha256'],
    'guildBotChecks': 30, 'guildProtocolChecks': 95, 'runtimeTimerVerified': True,
    'manualNpcInviteUiVerified': False,
    'manualUiLimitation': 'Computer Use native pipe unavailable (os error 2); native guild UI probe and native NPC invitation/profile/roster protocol tests passed.',
    'rules': [
        'Leaders/deputies invite eligible resident bots through the original profile invitation packet; bots automatically accept.',
        'Original guild capacity, minimum level and join-policy gates apply; every UID occupies one slot in one guild.',
        'Bot deputy promotion/removal is supported; the guild master remains human.',
        'The actual runtime timer drives attendance and daily work without an open guild page.',
        'Each resident earns at most one NPC-only contribution voucher per active day and consumes original-table points atomically.',
        'Guild funds, lifetime/weekly member contribution, donation count and mission progress share the normal guild state.',
        'Membership, activity watermarks, supplies and pending attendance claims are committed together in players.json.',
        'Restart, clock rollback, kick/reinvite and cross-guild transfer cannot duplicate the daily allowance.',
        'Only the current day is processed after downtime; human inventory/currencies and resident combat/world profiles remain unchanged.',
    ],
    'windows': manifest['windows'], 'android': mobile['packages'], 'proofs': proofs,
    'immutableCatalogHashes': catalogs,
}
write(verification / 'guild-bots-release-latest.json', report)
status = read(root / 'migration-status.json')
status['guildBotsLatest'] = report
write(root / 'migration-status.json', status)
manifest['updatedUtc'] = report['verifiedUtc']
write(root / 'Builds/latest-artifacts.json', manifest)
(root / 'Builds/本次修复说明.txt').write_text(
    'v17 服务端：人机入派、自动签到及门派日常贡献（保留 v15 游戏本体、v16 PvP 阶梯难度及此前修复）\n'
    'PC 完整包：BnsmLocal-Windows-x64.zip；本机仍用 I:/BnsmRecovery/Start-LocalGame.cmd，入口已更新。\n'
    'Android 服务端：PhoneServer/apk/local.bnsm.serverhost-Signed.apk，版本 0.17-guild-bots。\n'
    'Android 游戏本体沿用 v15；只需覆盖更新服务端，不需卸载或清除存档。\n'
    '掌门/副掌门打开人机好友或人机资料页，点击“邀请入派”；符合等级、人数及入派设置时自动加入。\n'
    '人机占用正常成员名额，支持副掌门及踢出；掌门职位保留给真实玩家。\n'
    '服务端运行时，人机入派约一分钟内签到，几分钟内做一次门派日常贡献；可在成员列表、门派资金和记录中查看。\n'
    '每日门派日常给人机一次专用贡献材料额度，按原表计入门派资金、累计/本周贡献和门派任务，不扣真实玩家的物品或货币。\n'
    '签到及每周贡献沿用原版重置时间；重启、重复请求及退派重加不能重复刷同一天贡献，停服期间不补刷多天。\n'
    '30 项人机门派检查、95 项原有门派回归、原生门派界面流程和服务端后台计时器均已通过。\n'
    'PC/Android 服务端核心一致；Android 已通过签名、ARM64/16KiB 静态检查，本次未接手机实测。\n'
    'PC CET 仍关闭，存档、常驻人机配装及正常音乐音效设置保留。\n', encoding='utf-8')
print(json.dumps({key: report[key] for key in [
    'passed', 'release', 'guildBotChecks', 'guildProtocolChecks',
    'runtimeTimerVerified', 'sameServerCoreAsAndroid', 'gameClientUnchanged',
]}, ensure_ascii=False))
