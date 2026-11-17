"""Bind rank strength, real fights, both packages and the installed v28 core."""
from pathlib import Path
import datetime, hashlib, json, zipfile
root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text('utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),'utf-8')
def digest(path):
    with (root/path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json');manifest=read('Builds/latest-artifacts.json')
client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')
assert pc['passed'] and pc['clientAssemblySha256']==client and pc['serverCoreSha256']==core and pc['sameServerCoreAsAndroid']
assert client=='177b6ec1e7cc204a26eae6de6950b4ffdf691e346c4f8de744b8a4a84f2c4cf6','Retain verified v27 game'
assert mobile['passed'] and manifest['mobile']['game']['versionCode']==27 and manifest['mobile']['server']['versionCode']==28
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
names=['pvp-difficulty-server-latest.json','rank-deck-server-latest.json','competitive-server-latest.json','castle-lifecycle-server-latest.json','battle-lifecycle-server-latest.json','manual-pvp-server-latest.json','manual-pvp-native-rank-latest.json','manual-pvp-native-balance-latest.json','battle-lifecycle-native-latest.json','castle-lifecycle-native-latest.json','windows-full-native-latest.json','windows-full-host-latest.json','windows-full-launcher-latest.json','pvp-ladder-native-arena-latest.json','pvp-ladder-native-infinite-latest.json']+['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read('Verification/'+name) for name in names}
for name,proof in proofs.items():
    assert proof['passed'],name
    if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==core,name
    if 'clientAssemblySha256' in proof:assert proof['clientAssemblySha256']==client,name
    if 'errors' in proof:assert proof['errors']==0,name
for mode,rank in [('arena',30),('infinite',100)]:
    proof=proofs['pvp-ladder-native-'+mode+'-latest.json'];assert proof['persistedSettlement'] and proof['ladderStartingRank']==rank and proof['nativeExitCode']==0
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as archive:
    assert 'v28' in archive.read('BnsmLocal-Windows-x64/README.txt').decode('utf-8-sig')
    assert hashlib.sha256(archive.read('BnsmLocal-Windows-x64/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')).hexdigest()==core
for item in manifest['mobile'].values():assert digest(item['path'])==item['sha256']
now=datetime.datetime.now(datetime.timezone.utc).isoformat();manifest['windows']['release']=28;manifest['updatedUtc']=now;write('Builds/latest-artifacts.json',manifest)
report=dict(passed=True,release=28,verifiedUtc=now,gameClientUnchanged=True,androidGameVersion=27,androidHostVersion=28,androidDeviceVerified=False,normalSaveUsed=False,clientAssemblySha256=client,sharedServerCoreSha256=core,serverChecks=len(proofs['pvp-difficulty-server-latest.json']['checks']),proofs=names,windows=manifest['windows'],mobile=manifest['mobile'],rules=['Opponent-rank floors: top 5000 ordinary, top 1000 advanced, top 300 expert, top 100 grandmaster.','Native engraving/transcendence budgets across top 1000/300/100/30/10; no invented stat multiplier.','Low-ladder introductory and loss protection retained; front rank minimum cannot be erased by viewer level/losses.','Versioned immutable offers; idle legacy lobby migration; admitted battle/replay/settlement unchanged.','All infinite teams inherit selected strength and preserve equipment engraving.','Balanced stat tables and resident saved builds remain unchanged.'])
write('Verification/pvp-ladder-release-latest.json',report)
status=read('migration-status.json');status['pvpLadderLatest']=report;status['phase']='v28: opponent-rank strength floors, native engraving/growth and infinite team parity verified and published with unchanged v27 game client.';write('migration-status.json',status)
(root/'Builds/本次修复说明.txt').write_text(
    'v28：PvP高排名人机强度更新（游戏沿用v27）\n'
    '按对手自己的排名设置卡组最低档位：前5000普通、前1000进阶、前300高手、前100宗师。\n'
    '前1000/300/100/30/10逐步增加原表合法装备刻印、超越培养；前排卡组不再被玩家等级或连败保护降成入门档。\n'
    '保留低排名前五场保护、连败缓冲、强/中/易选择、非等距对手名次和战胜高排名对手后占据其名次的规则。\n'
    '斗神争霸三队继承选中对手的强度及完整刻印；武林至尊继续使用原版均衡属性表。\n'
    '空闲大厅刷新时升级旧对手；已开战快照保持不变，预览、开战、回放、重试和重启保持一致。\n'
    'PC完整包已更新，本机安装已覆盖；Android仅覆盖更新v28服务端APK，游戏继续使用v27，不需卸载或清档。\n'
    '已通过隔离存档协议回归、六种竞技原生战斗、30名PvP及100名斗神实战、替补/退出重入和启动检查。\n'
    'Android通过构建、原签名、ARM64及16KiB检查；本次未接手机实测。\n',encoding='utf-8')
print(json.dumps(dict(passed=True,release=28,serverChecks=report['serverChecks'],sharedServerCoreSha256=core,gameClientUnchanged=True),ensure_ascii=False))
