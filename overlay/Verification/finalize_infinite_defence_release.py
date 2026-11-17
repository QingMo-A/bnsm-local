from pathlib import Path
import datetime,hashlib,json,zipfile
root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text('utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),'utf-8')
def digest(path):return hashlib.sha256((root/path).read_bytes()).hexdigest()
pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json');manifest=read('Builds/latest-artifacts.json')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll');client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')
assert pc['passed'] and mobile['passed'] and all(x['versionCode']==26 for x in mobile['packages'])
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
names=['infinite-defence-native-latest.json','rank-deck-server-latest.json','competitive-server-latest.json','pvp-difficulty-server-latest.json','castle-lifecycle-server-latest.json','castle-lifecycle-native-latest.json']+['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read('Verification/'+name) for name in names}
for name,item in proofs.items():
 assert item['passed'] and item['serverCoreSha256']==core,name
 if 'clientAssemblySha256' in item:assert item['clientAssemblySha256']==client,name
native=proofs['infinite-defence-native-latest.json'];assert native['errors']==0 and native['persistedExactSlots'] and native['oneSuccessfulSave'] and native['unchangedCombatState']
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as z:
 assert hashlib.sha256(z.read('BnsmLocal-Windows-x64/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')).hexdigest()==core
assert digest('WindowsPreview/LocalServer/data/activities.json')=='052a813ba6c5ed8b8eb0bcac22dd80da27b2cdb9bb89cb32f377e8ee0aeb9bc1'
now=datetime.datetime.now(datetime.timezone.utc).isoformat();manifest['windows']['release']=26;manifest['updatedUtc']=now;write('Builds/latest-artifacts.json',manifest)
report=dict(passed=True,release=26,verifiedUtc=now,cause='Partial team tab changes called SaveDeck -> IsDeckChange false -> UpdateInfo reloaded old confirmed roster; editor also closed before the authoritative save.',sharedServerCoreSha256=core,clientAssemblySha256=client,nativeChecks=len(native['checks'])+len(native['restartChecks']),nativeFolder=native['folder'],androidDeviceVerified=False,productionSaveUsed=False,windows=manifest['windows'],android=mobile['packages'],proofs=names)
write('Verification/infinite-defence-release-latest.json',report)
status=read('migration-status.json');status['infiniteDefenceLatest']=report;status['phase']='Infinite arena defence draft switching and acknowledged save/restart verified and published as v26.';write('migration-status.json',status)
(root/'Builds/本次修复说明.txt').write_text(
 'v26：斗神争霸防御卡组保存与切换修复\n'
 '修复只配置好一队或两队时，切换编队从旧数据刷新并清空刚选卡牌的问题。切换、换卡、拖动排序和弹窗返回均保留完整15槽草稿。\n'
 '确认保存后一次提交三队卡组，等待服务端确认成功才关闭页面；失败保留页面和草稿，重复点击不重复提交，旧页面回调不能关闭新页面。\n'
 '修复配置页打开后大厅迟到的搜索回复尝试在隐藏对象上播放动画的问题。\n'
 '保留每队至少一张、三队卡牌互不重复和不能使用修炼卡的原版规则。存档及其他模式规则保留。\n'
 '原生验证覆盖从未完成阵容开始逐队选卡、快速切队、排序、服务端拒绝、双击保存、重新打开以及游戏/服务端重启；六种竞技实际战斗与攻城生命周期回归通过。\n'
 '本机PC已更新。Android请覆盖更新游戏APK和服务端APK，无需卸载；Android完成签名、ARM64及16KiB包检查，未实机测试。\n',encoding='utf-8')
print(json.dumps(dict(passed=True,release=26,nativeChecks=report['nativeChecks'],sharedServerCoreSha256=core,clientAssemblySha256=client),ensure_ascii=False))
