from pathlib import Path
import datetime,hashlib,json,zipfile
root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text('utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),'utf-8')
def digest(path):return hashlib.sha256((root/path).read_bytes()).hexdigest()
pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json');manifest=read('Builds/latest-artifacts.json')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll');client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')
assert pc['passed'] and mobile['passed'] and all(x['versionCode']==25 for x in mobile['packages'])
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
names=['guild-battle-server-latest.json','guild-battle-native-latest.json','guild-protocol-latest.json','guild-bots-server-latest.json','guild-native-latest.json','competitive-server-latest.json','battle-lifecycle-server-latest.json','battle-lifecycle-native-latest.json','pvp-difficulty-server-latest.json']+['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read('Verification/'+name) for name in names}
for name,item in proofs.items():
 assert item['passed'] and item['serverCoreSha256']==core,name
 if 'clientAssemblySha256' in item:assert item['clientAssemblySha256']==client,name
native=proofs['guild-battle-native-latest.json']
assert native['persistedSettlement'] and native['persistedReplay'] and native['persistedIdle'] and native['oneActivityCredit']
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as z:
 assert hashlib.sha256(z.read('BnsmLocal-Windows-x64/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')).hexdigest()==core
assert digest('WindowsPreview/LocalServer/data/activities.json')=='052a813ba6c5ed8b8eb0bcac22dd80da27b2cdb9bb89cb32f377e8ee0aeb9bc1'
now=datetime.datetime.now(datetime.timezone.utc).isoformat();manifest['windows']['release']=25;manifest['updatedUtc']=now;write('Builds/latest-artifacts.json',manifest)
report=dict(passed=True,release=25,verifiedUtc=now,mode=29,scope='Native guild arena with local resident opponent templates; scheduled territory RvR is separate.',sharedServerCoreSha256=core,clientAssemblySha256=client,protocolChecks=len(proofs['guild-battle-server-latest.json']['checks']),nativeFolder=native['folder'],productionSaveUsed=False,androidDeviceVerified=False,windows=manifest['windows'],android=mobile['packages'],proofs=names)
write('Verification/guild-battle-release-latest.json',report)
status=read('migration-status.json');status['guildBattleLatest']=report;status['phase']='Original guild arena, native result/replay, authoritative admission and preserved guild functionality verified and published as v25.';write('migration-status.json',status)
(root/'Builds/本次修复说明.txt').write_text(
 'v25：恢复本地门派对抗战（原版竞技门派战，mode 29）\n'
 '入口：竞技页门派战，或门派首页的门派对抗战按钮。按原版等级与门派等级限制开放。\n'
 '恢复三档本地人机对手、原版选卡和同门助战、真实战斗与回放、积分、排行榜、个人及门派奖励、战绩和任务活动进度。\n'
 '查看对手不扣次数；确认开战才扣次数和弹药、占用当日助战名额。退出不伪造失败或奖励，重进、重复结算和旧回复不会重复发奖或覆盖新战斗。\n'
 '保留原表入场限制、冷却及重置费用；每日门派战名额与助战在原版4点重置，存档和原门派签到贡献等功能保留。\n'
 '本次恢复本地人机匹配；报名争夺据点的门派RvR（mode 40）是另一套玩法，未在本次开放。\n'
 'Windows完整包：BnsmLocal-Windows-x64.zip；本机WindowsPreview已同步。\n'
 'Android请同时覆盖更新Android-ARM64/BnsmLocal-arm64.apk和PhoneServer/apk/local.bnsm.serverhost-Signed.apk，无需卸载。\n'
 'PC原生门派战、原门派流程、六种竞技模式和战斗生命周期检查通过。Android签名、ARM64及16KiB包检查通过，未连接实体手机测试。\n',encoding='utf-8')
print(json.dumps(dict(passed=True,release=25,protocolChecks=report['protocolChecks'],sharedServerCoreSha256=core),ensure_ascii=False))
