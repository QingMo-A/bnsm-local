from pathlib import Path
import datetime,hashlib,json,zipfile
root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text('utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),'utf-8')
def digest(path):return hashlib.sha256((root/path).read_bytes()).hexdigest()
pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json');manifest=read('Builds/latest-artifacts.json')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll');client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')
assert pc['passed'] and mobile['passed'] and all(x['versionCode']==24 for x in mobile['packages'])
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
proofs={}
for name in ['world-raid-server-latest.json','world-raid-native-latest.json','battle-lifecycle-server-latest.json','battle-lifecycle-native-latest.json','castle-monster-server-latest.json','castle-monster-native-latest.json','rank-deck-server-latest.json','castle-invasion-server-latest.json']:
 item=read('Verification/'+name);assert item['passed'] and item['serverCoreSha256']==core,name
 if 'clientAssemblySha256' in item:assert item['clientAssemblySha256']==client,name
 proofs[name]=item
native=proofs['world-raid-native-latest.json'];assert native['bothActualDamageRecords'] and native['twoEntriesEachWithoutExtraDebit'] and native['persistedIdleAfterSettlement']
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as z:
 assert hashlib.sha256(z.read('BnsmLocal-Windows-x64/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')).hexdigest()==core
assert digest('WindowsPreview/LocalServer/data/activities.json')=='052a813ba6c5ed8b8eb0bcac22dd80da27b2cdb9bb89cb32f377e8ee0aeb9bc1'
now=datetime.datetime.now(datetime.timezone.utc).isoformat();manifest['windows']['release']=24;manifest['updatedUtc']=now;write('Builds/latest-artifacts.json',manifest)
report=dict(passed=True,release=24,verifiedUtc=now,cause='Native lobby visibility refresh after world/combo admission abandoned its active encounter; info read is now pure.',sharedServerCoreSha256=core,clientAssemblySha256=client,protocolChecks=len(proofs['world-raid-server-latest.json']['checks']),nativeChecks=len(native['checks']),nativeFolder=native['folder'],androidDeviceVerified=False,productionSaveUsed=False,windows=manifest['windows'],android=mobile['packages'],proofs=list(proofs))
write('Verification/world-raid-release-latest.json',report)
status=read('migration-status.json');status['worldRaidLatest']=report;status['phase']='Blood/combo raid settlement, background list reads, token-bound retreat and stale receipt protection verified and published as v24.';write('migration-status.json',status)
(root/'Builds/本次修复说明.txt').write_text(
 'v24：幻魔战血战、连招副本结算修复\n'
 '修复开战成功后选卡窗口关闭触发大厅列表刷新，导致服务端误清战斗状态、结算报332/END_WORLD_BOSS_BATTLE的问题。\n'
 '列表和次数刷新只读取状态；退出仍通过原有战斗凭据确认，保留已消耗次数和冷却。\n'
 '正常真实伤害按原表结算精气，并保存最高伤害和对应卡组；重复回复不重复发奖，旧回复不能覆盖新战斗。\n'
 '保留v23的攻城战、挑战塔、斗神争霸卡组、PvP名次修复，以及已有活动、存档和设置。\n'
 'Windows完整包：BnsmLocal-Windows-x64.zip；本机WindowsPreview已同步更新。\n'
 'Android请同时覆盖更新Android-ARM64/BnsmLocal-arm64.apk和PhoneServer/apk/local.bnsm.serverhost-Signed.apk，保留存档，无需卸载。\n'
 'PC原生血战/连招真实结算、战中刷新、退出重入、伤害/奖励存档及战斗生命周期检查通过；Android包检查通过，未连接实体手机测试。\n',encoding='utf-8')
print(json.dumps({k:report[k] for k in ['passed','release','protocolChecks','nativeChecks','sharedServerCoreSha256','clientAssemblySha256']},ensure_ascii=False))
