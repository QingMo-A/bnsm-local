"""Bind the live ranked/balanced combat proof to the v21 deliverables."""
from pathlib import Path
import datetime,hashlib,json

root=Path(__file__).resolve().parents[1];verification=root/'Verification'
def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def digest(path):
    with Path(path).open('rb') as source:return hashlib.file_digest(source,'sha256').hexdigest()

manifest=read(root/'Builds/latest-artifacts.json');windows=read(verification/'windows-full-release.json');mobile=read(verification/'mobile-publication-latest.json')
assert windows['passed'] and mobile['passed'] and windows['sameServerCoreAsAndroid']
core=windows['serverCoreSha256'];client=windows['clientAssemblySha256']
assert digest(root/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')==core
assert digest(Path(windows['server']).with_name('Bnsm.ServerCore.dll'))==core
assert digest(Path(windows['client']).parent/'BnsmLocal_Data/Managed/Assembly-CSharp.dll')==client
assert digest(windows['archive'])==windows['archiveSha256']
for role in ['game','server']:
    package=manifest['mobile'][role];assert package['versionCode']==21 and digest(package['path'])==package['sha256']

names=['manual-pvp-server-latest.json','manual-pvp-native-rank-latest.json','manual-pvp-native-balance-latest.json',
       'competitive-server-latest.json','battle-lifecycle-server-latest.json','battle-lifecycle-native-latest.json',
       'pvp-difficulty-server-latest.json','guild-bots-server-latest.json','castle-monster-server-latest.json','castle-lifecycle-server-latest.json',
       'windows-full-host-latest.json','windows-full-native-latest.json','windows-full-launcher-latest.json','windows-cet-latest.json']
names+=['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read(verification/name) for name in names}
for name,proof in proofs.items():
    assert proof['passed'],name
    if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==core,name
    if 'clientAssemblySha256' in proof:assert proof['clientAssemblySha256']==client,name
for mode in ['rank','balance']:
    native=proofs['manual-pvp-native-'+mode+'-latest.json'];replay=native['actualReplay']
    assert native['oneRealSettlementAfterRetreat'] and native['checks']['manualSkillConsumesNativeSp']
    assert native['checks']['startersWipedWithReservesDoNotLose']
    assert all(native['checks']['reserveStartsWithFullSp'+str(i)] for i in range(3))
    assert replay['playerCards']==8 and replay['enemyCards']==8 and replay['enemySubstitutions']==3 and replay['allEntrantsFullSp']
assert all(not item['cetCompatible'] for item in proofs['windows-cet-latest.json']['entries'])
report={'passed':True,'release':21,'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope':'Live manual/automatic skill control and both-team reserves for ranked/balanced local PvP',
        'normalSaveUsed':False,'androidDeviceVerified':False,'sharedServerCoreSha256':core,'clientAssemblySha256':client,
        'protocolChecks':len(proofs['manual-pvp-server-latest.json']['checks']),
        'rules':[
            'Both modes use the native live simulator; ordinary arena and infinite arena retain their existing automation.',
            'Five starters plus up to three chosen reserves are captured at admission; duplicate, unowned, training and overlapping cards reject before spending.',
            'The native reserve pool reorders only remaining captured cards and confirms entry into the selected vacancy.',
            'Player and AI entrants start immediately at their own maximum SP; no defeat occurs while player reserves remain.',
            'Enemy reserves come from the same difficulty offer and legal templates; saved resident profiles and balanced stat tables are preserved.',
            'Replay validation binds both full rosters, reserve identities, slots, single use and entry SP to the started battle.',
            'Native interaction tests use diagnostic fatalities only in a retreated attempt. Separate unmodified battles supply actual compressed replays and durable results.',
            'Both real replay audits contain three enemy entries. Balanced native combat also exercised three player entries and a real defeat.',
            'Retreat and lobby recovery do not fabricate losses or refunds; duplicate or stale packets cannot revive old attempts or replace a newer battle.'
        ],'windows':manifest['windows'],'android':mobile['packages'],'proofs':proofs}
write(verification/'manual-pvp-release-latest.json',report)
status=read(root/'migration-status.json');status['manualPvpLatest']=report;status['phase']='Ranked and balanced live control/reserves verified and published for Windows x64 and Android game/server v21.';write(root/'migration-status.json',status)
manifest['updatedUtc']=report['verifiedUtc'];manifest['windows']['release']=21;write(root/'Builds/latest-artifacts.json',manifest)
(root/'Builds/本次修复说明.txt').write_text(
    'v21：跨服江湖大战、武林至尊同步修复\n'
    'PC 完整包：BnsmLocal-Windows-x64.zip；本机原启动入口已更新。\n'
    'Android 请同时覆盖更新 Android-ARM64/BnsmLocal-arm64.apk 和 PhoneServer/apk/local.bnsm.serverhost-Signed.apk。\n'
    '保留原存档，不需要卸载或清除数据；PC 的 CET 继续关闭。\n'
    '两玩法改用实时战斗，恢复手动释放武功和手动/自动切换。\n'
    '恢复备用卡组配置；阵亡位置显示“上场”，点击打开剩余替补池，调整顺序后确认首张登场。\n'
    '我方和敌方各最多三张替补，均以自身满内力登场；敌方由 AI 自动补位。先发全灭但仍有替补时不会提前判负。\n'
    '开战阵容、替补、内力和回放由服务端核对；退出、重进、重复和过期回包均有状态检查。\n'
    '保留原分档难度、均衡属性表及门票规则，不修改常驻人机存档。\n'
    '已通过两玩法原生手动技能、自动切换、连续三次替补、实际完整战斗与保存结算；其他六类竞技、战斗退出及启动检查通过。\n'
    'Android 已通过签名、ARM64 与 16-KiB 对齐检查；未连接实体手机进行现场测试。\n',encoding='utf-8')
print(json.dumps({k:report[k] for k in ['passed','release','protocolChecks','sharedServerCoreSha256']},ensure_ascii=False))
