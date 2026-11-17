"""Record the verified v18 castle exploration and native paid-action release."""
from pathlib import Path
import datetime
import hashlib
import json

root=Path(__file__).resolve().parents[1]
verification=root/'Verification'

def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source,'sha256').hexdigest()

def write(path,value):
    Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')

manifest=read(root/'Builds/latest-artifacts.json')
windows=read(verification/'windows-full-release.json')
mobile=read(verification/'mobile-publication-latest.json')
assert windows['passed'] and mobile['passed'] and windows['sameServerCoreAsAndroid']
core=windows['serverCoreSha256']
assert digest(root/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')==core
assert digest(Path(windows['server']).with_name('Bnsm.ServerCore.dll'))==core
assert manifest['mobile']['game']['versionCode']==manifest['mobile']['server']['versionCode']==18
names=['castle-explore-instant-server-latest.json','castle-explore-instant-native-latest.json',
       'castle-lifecycle-server-latest.json','castle-lifecycle-native-latest.json',
       'competitive-server-latest.json','pvp-difficulty-server-latest.json',
       'guild-bots-server-latest.json','guild-protocol-latest.json',
       'battle-lifecycle-server-latest.json','document-server-latest.json',
       'windows-full-host-latest.json','windows-full-native-latest.json',
       'windows-full-launcher-latest.json','windows-cet-latest.json']
names+=['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read(verification/name) for name in names}
for name,proof in proofs.items():
    assert proof['passed'],name
    if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==core,name
    if 'clientAssemblySha256' in proof:assert proof['clientAssemblySha256']==windows['clientAssemblySha256'],name
assert all(not item['cetCompatible'] for item in proofs['windows-cet-latest.json']['entries'])
assert all(proofs['castle-explore-instant-native-latest.json'][key] for key in ['persistedTwoPaidOccupations','noFabricatedReplay','persistedIdle'])
assert digest(windows['archive'])==windows['archiveSha256']
for role in ['game','server']:
    assert digest(manifest['mobile'][role]['path'])==manifest['mobile'][role]['sha256']
catalogs={
    'WindowsPreview/LocalServer/data/gacha.json':'de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908',
    'Builds/PhoneServer/server-content.zip':'a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1',
    'Project/Assets/StreamingAssets/DynamicAssets/CDT.bytes':'1ade0ffce7b3b59d4b6fe326be7f089612fd2ea02a7bf49f12bc595712cc2a76',
}
for path,expected in catalogs.items():assert digest(root/path)==expected,path
report={
    'passed':True,'release':18,'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'scope':'Native hex exploration and token-bound original paid instant occupation/invasion/restoration',
    'normalSaveUsed':False,'androidDeviceVerified':False,'androidGameVersionCode':18,'androidServerVersionCode':18,
    'sameServerCoreAsAndroid':True,'sharedServerCoreSha256':core,'clientAssemblySha256':windows['clientAssemblySha256'],
    'serverChecks':len(proofs['castle-explore-instant-server-latest.json']['checks']),
    'rules':[
        'Physical hex neighbours and independent level-35 roots match the native visibility rule, without requiring occupation or authored bridge links.',
        'Paid instant actions carry the specific selected encounter token and original daily escalating cash price plus admission.',
        'A paid shortcut cannot replace a real active battle; stale tokens/receipts cannot mutate a newer attempt.',
        'Ownership, costs, daily action counters, original rewards, history and idle state commit atomically; retries cannot duplicate them.',
        'Paid shortcuts consume no ammunition, create no fabricated replay and do not promote real PvP difficulty.',
        'Optional recovered decorative frames cannot interrupt a successful native result callback.',
        'Both native consecutive occupation results and reopened ownership/counters were checked against durable save data.',
    ],
    'windows':manifest['windows'],'android':mobile['packages'],'proofs':proofs,'immutableCatalogHashes':catalogs,
}
write(verification/'castle-instant-release-latest.json',report)
status=read(root/'migration-status.json');status['castleInstantLatest']=report;write(root/'migration-status.json',status)
manifest['updatedUtc']=report['verifiedUtc'];write(root/'Builds/latest-artifacts.json',manifest)
(root/'Builds/本次修复说明.txt').write_text(
    'v18：攻城战探索与立即占领修复（包含 v17 人机门派、v16 PvP 阶梯难度及此前修复）\n'
    '本次双端均须覆盖更新游戏和服务端，保留存档，不需要卸载或清除数据。\n'
    'PC 最新完整包：BnsmLocal-Windows-x64.zip；本机原入口 I:/BnsmRecovery/Start-LocalGame.cmd 已更新。\n'
    'Android 游戏：Android-ARM64/BnsmLocal-arm64.apk（versionCode 18）。\n'
    'Android 服务端：PhoneServer/apk/local.bnsm.serverhost-Signed.apk（0.18-castle-instant）。\n'
    '修复“尚有未占领城池时继续探索报 114/SEARCH_MAP_SPOT”：起始地块与相邻六边形按原版规则开放，不再误用道路连接表。\n'
    '修复“立即占领”误发普通战斗结算：立即占领/入侵/修复按原表钻石费用及入场消耗单独结算，返回真实的每日次数与城池状态。\n'
    '修复成功结算后可选装饰边框缺失造成的客户端异常，保留占领后的原版守军配置流程。\n'
    '重试、钻石不足、过期请求、退出及重启均已检查；重复请求不会重复扣费或发奖，立即操作不残留战斗状态。\n'
    'PC 原生连续两次付费占领、守军配置、重开地图，以及原有攻城战中途退出/重进/真实结算、六种 PvP 模式和启动器已验证。\n'
    'Android 已通过签名、ARM64/16KiB 静态检查，本次未接手机实测。PC CET 继续关闭，正常音乐音效设置保留。\n',encoding='utf-8')
print(json.dumps({key:report[key] for key in ['passed','release','serverChecks','sameServerCoreAsAndroid','androidDeviceVerified']},ensure_ascii=False))
