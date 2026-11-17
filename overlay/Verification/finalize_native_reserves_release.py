from pathlib import Path
import datetime, hashlib, json, zipfile

root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text('utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),'utf-8')
def digest(path):return hashlib.sha256((root/path).read_bytes()).hexdigest()

pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json');manifest=read('Builds/latest-artifacts.json')
client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')
assert pc['passed'] and mobile['passed'] and all(item['versionCode']==27 for item in mobile['packages'])
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
names=['skin-shop-native-latest.json','infinite-defence-native-latest.json','manual-pvp-native-rank-latest.json','manual-pvp-native-balance-latest.json','document-ui-latest.json','viewport-latest.json','windows-full-native-latest.json','windows-full-launcher-latest.json','battle-lifecycle-native-latest.json','castle-lifecycle-native-latest.json']+['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
proofs={name:read('Verification/'+name) for name in names}
for name,proof in proofs.items():
    assert proof['passed'] and proof['clientAssemblySha256']==client,name
    if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==core,name
skin=proofs['skin-shop-native-latest.json'];defence=proofs['infinite-defence-native-latest.json']
assert skin['errors']==0 and all(skin['checks'].values())
assert defence['errors']==0 and all(defence['checks'].values()) and all(defence['restartChecks'].values())
assert defence['persistedExactSlots'] and defence['oneSuccessfulSave'] and defence['unchangedCombatState']
for name in ['manual-pvp-native-rank-latest.json','manual-pvp-native-balance-latest.json']:
    proof=proofs[name];assert proof['oneRealSettlementAfterRetreat'] and proof['actualReplay']['allEntrantsFullSp'] and proof['actualReplay']['enemySubstitutions']>=1
    assert all(proof['checks']['noSeparateReservePage'+str(i)] for i in range(3))
    assert all(proof['checks']['vacancyHit'+str(i)] and proof['checks']['reserveStartsWithFullSp'+str(i)] for i in range(3))
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as archive:
    assert hashlib.sha256(archive.read('BnsmLocal-Windows-x64/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')).hexdigest()==client
    assert 'v27' in archive.read('BnsmLocal-Windows-x64/README.txt').decode('utf-8-sig')
for item in manifest['mobile'].values():assert digest(item['path'])==item['sha256']
now=datetime.datetime.now(datetime.timezone.utc).isoformat();manifest['windows']['release']=27;manifest['updatedUtc']=now;write('Builds/latest-artifacts.json',manifest)
report=dict(passed=True,release=27,verifiedUtc=now,platforms=['Windows','Android'],clientAssemblySha256=client,sharedServerCoreSha256=core,normalSaveUsed=False,androidDeviceVerified=False,skinNativeChecks=len(skin['checks']),defenceNativeChecks=len(defence['checks'])+len(defence['restartChecks']),skinFolder=skin['folder'],defenceFolder=defence['folder'],windows=manifest['windows'],mobile=manifest['mobile'],proofs=names)
write('Verification/native-reserves-release-latest.json',report)
status=read('migration-status.json');status['nativeReservesUiLatest']=report;status['phase']='v27: original boss-style inline ranked/balanced reserves, infinite defence guidance/layout, skin shop and native regressions verified and published.';write('migration-status.json',status)
(root/'Builds/本次修复说明.txt').write_text(
    'v27：皮肤商店、斗神争霸编队和江湖大战/武林至尊替补操作更新\n'
    'PC主城皮肤商店：列表、立绘/模型预览和购买信息分区排列；选中条目高亮，名称/属性/价格/购买按钮集中在同一底板；修复强光遮挡、比例错位、顶部瓦片拉伸和4:3雾层遮挡。\n'
    '斗神争霸：保留原版三组各至少1张的规则，不需要填满15张。未配置第2/3组时明确指出组别；PC组别按钮、数量提示和保存操作独立排列。切组保留草稿，保存等待服务端确认，失败保留草稿，重启后恢复已确认阵容。\n'
    '江湖大战/武林至尊：使用原版魔王战的战斗内替补队列与阵亡空位上场按钮，可在战斗内变更顺序，再点击空位上场；取消整页编队弹窗。连续替补及满内力登场已验证，我方与敌方均保留完整替补和回放/结算规则。\n'
    '验证：100/85/70%比例、4:3及超宽窗口，皮肤实际购买、斗神编队保存与游戏/服务端重启，双模式连续三次替补、手动/自动切换、原生战斗和服务端结算，以及其他六种竞技/战斗退出/启动回归。均使用独立测试存档。\n'
    '本机PC已更新。Android请覆盖更新v27游戏APK和服务端APK，无需卸载；完成签名、ARM64、16KiB包检查，未实机测试。\n',encoding='utf-8')
print(json.dumps(dict(passed=True,release=27,skinNativeChecks=report['skinNativeChecks'],defenceNativeChecks=report['defenceNativeChecks'],clientAssemblySha256=client),ensure_ascii=False))
