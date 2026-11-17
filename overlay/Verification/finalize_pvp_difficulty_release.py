"""Record evidence for the published shared-server difficulty update."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil

root=Path(__file__).resolve().parents[1]
verification=root/'Verification'
def read(name):
    return json.loads((verification/name).read_text(encoding='utf-8-sig'))
def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source,'sha256').hexdigest()

manifest=json.loads((root/'Builds/latest-artifacts.json').read_text(encoding='utf-8-sig'))
windows=read('windows-full-release.json')
mobile=read('mobile-publication-latest.json')
assert windows['passed'] and mobile['passed'] and windows['sameServerCoreAsAndroid']
core=windows['serverCoreSha256']
assert digest(root/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')==core
assert manifest['mobile']['game']['versionCode']==15 and manifest['mobile']['server']['versionCode']==16
assert windows['clientAssemblySha256']=='eb6aadf0f2b11c46232c22736a53789ee4a7076a93717556c8a524a4d59520ce'
names=['pvp-difficulty-server-latest.json','competitive-server-latest.json','castle-lifecycle-server-latest.json',
       'battle-lifecycle-server-latest.json','document-server-latest.json','castle-lifecycle-native-latest.json',
       'windows-full-host-latest.json','windows-full-launcher-latest.json','windows-cet-latest.json']
names += ['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read(name) for name in names}
for name,proof in proofs.items():
    assert proof['passed'],name
    if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==core,name
    if 'clientAssemblySha256' in proof:assert proof['clientAssemblySha256']==windows['clientAssemblySha256'],name
for role in ['game','server']:
    item=manifest['mobile'][role]
    assert digest(item['path'])==item['sha256'],role
assert digest(windows['archive'])==windows['archiveSha256']
catalogs={
    'WindowsPreview/LocalServer/data/gacha.json':'de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908',
    'Builds/PhoneServer/server-content.zip':'a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1',
    'Project/Assets/StreamingAssets/DynamicAssets/CDT.bytes':'1ade0ffce7b3b59d4b6fe326be7f089612fd2ea02a7bf49f12bc595712cc2a76',
}
for relative,expected in catalogs.items():assert digest(root/relative)==expected,relative
report={
    'passed':True,'release':16,'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'scope':'Five real PvP loadout tiers, introductory matchmaking, loss relief and persisted opponent offers',
    'normalSaveUsed':False,'androidDeviceVerified':False,'gameClientUnchanged':True,
    'androidGameVersionCode':15,'androidServerVersionCode':16,
    'sameServerCoreAsAndroid':True,'sharedServerCoreSha256':core,
    'clientAssemblySha256':windows['clientAssemblySha256'],
    'tiers':['入门','普通','进阶','高手','宗师'],
    'rules':[
        'Displayed strong/matched/easy choices use distinct actual tiers.',
        'Automatic matchmaking in the first five completed matches uses introductory easy/ordinary opponents.',
        'Validated wins/rank promote brackets; three consecutive validated defeats reduce the next bracket by one, down to introductory.',
        'Template inputs persist from preview through start and restart; a started battle retains its replay-bound snapshot.',
        'Cancellation or retreat cannot count as a defeat, win, reward or difficulty progression.',
        'All three infinite arena enemy teams remain at the selected tier; balanced mode preserves native equal-stat data.',
        'World-war victory preserves actual inner-energy points up to each native card maximum; nonfinite/out-of-range values reject atomically.',
        'Resident saves, immutable catalogs and normal music/sound preferences remain unchanged.',
    ],
    'windows':manifest['windows'],'android':mobile['packages'],'proofs':proofs,
    'immutableCatalogHashes':catalogs,
}
(verification/'pvp-difficulty-release-latest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
castle=read('castle-release-latest.json')
if castle.get('release')==15:
    archived=verification/'castle-release-v15.json'
    if not archived.exists():shutil.copy2(verification/'castle-release-latest.json',archived)
castle.update(release=16,verifiedUtc=report['verifiedUtc'],sharedServerCoreSha256=core,windows=manifest['windows'],
              android=mobile['packages'],pvpDifficultyReport=str(verification/'pvp-difficulty-release-latest.json'))
(verification/'castle-release-latest.json').write_text(json.dumps(castle,ensure_ascii=False,indent=2),encoding='utf-8')
status=json.loads((root/'migration-status.json').read_text(encoding='utf-8-sig'))
status['pvpDifficultyLatest']=report
status['castleLatest']=castle
(root/'migration-status.json').write_text(json.dumps(status,ensure_ascii=False,indent=2),encoding='utf-8')
manifest['updatedUtc']=report['verifiedUtc']
(root/'Builds/latest-artifacts.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(root/'Builds/本次修复说明.txt').write_text(
    'v16 服务端 PvP 阶梯难度版（保留 v15 客户端及此前修复）\n'
    'PC 完整包：BnsmLocal-Windows-x64.zip；本机仍用 I:/BnsmRecovery/Start-LocalGame.cmd。\n'
    'Android 服务端：PhoneServer/apk/local.bnsm.serverhost-Signed.apk，版本 0.16-pvp-tiers。\n'
    'Android 游戏本体沿用已验证的 v15；本次只需覆盖更新服务端，不需卸载或清除存档。\n'
    '强度有入门、普通、进阶、高手、宗师五档；三类对手对应实际配装差异。\n'
    '自动匹配前五场保护，连续三次真实败北后降低一档（最低入门段），按胜场和排名逐步升级。\n'
    '对手预览、开战、退出重连使用固定模板；无限挑战三队保持所选难度，均衡玩法保留原版统一属性。\n'
    'PC 已完成各 PvP 模式真实战斗及保存结算检查；Android 完成静态检查，本次未接手机实测。\n'
    'PC CET 仍关闭，常驻人机原档及玩家存档保留。\n',encoding='utf-8')
print(json.dumps({'passed':True,'release':16,'pvpDifficultyChecks':len(proofs['pvp-difficulty-server-latest.json']['checks']),
                  'sameServerCoreAsAndroid':True,'gameClientUnchanged':True},ensure_ascii=False))
