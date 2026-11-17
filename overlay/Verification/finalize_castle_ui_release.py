"""Record the PC-only castle composition update against published binaries."""
from pathlib import Path
import datetime,hashlib,json

root=Path(__file__).resolve().parents[1]
verification=root/'Verification'

def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def digest(path):
    with Path(path).open('rb') as source:return hashlib.file_digest(source,'sha256').hexdigest()

manifest=read(root/'Builds/latest-artifacts.json')
windows=read(verification/'windows-full-release.json')
previous=read(verification/'castle-instant-release-latest.json')
assert windows['passed'] and windows['sameServerCoreAsAndroid']
assert windows['serverCoreSha256']==previous['sharedServerCoreSha256']
names=['castle-ui-native-latest.json','viewport-latest.json','windows-full-native-latest.json',
       'windows-full-host-latest.json','windows-full-launcher-latest.json','windows-cet-latest.json',
       'castle-lifecycle-native-latest.json','castle-explore-instant-native-latest.json']
proofs={name:read(verification/name) for name in names}
for name,proof in proofs.items():
    assert proof['passed'],name
    if 'clientAssemblySha256' in proof:assert proof['clientAssemblySha256']==windows['clientAssemblySha256'],name
    if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==windows['serverCoreSha256'],name
ui=proofs['castle-ui-native-latest.json']
assert all(ui['checks'].values()) and ui['errors']==0 and len(ui['samples'])==7
assert all(not item['cetCompatible'] for item in proofs['windows-cet-latest.json']['entries'])
assert digest(windows['archive'])==windows['archiveSha256']
for role in ['game','server']:
    package=manifest['mobile'][role]
    prior=next(p for p in previous['android'] if p['role']==role)
    assert package['versionCode']==18 and package['sha256']==prior['sha256'] and digest(package['path'])==prior['sha256']
report={
    'passed':True,'pcUiRevision':19,'serverRelease':18,'androidUnchanged':True,
    'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'scope':'PC castle framed map, viewport clipping/picking, independent chrome/footer and smooth map wheel zoom',
    'normalSaveUsed':False,'sharedServerCoreSha256':windows['serverCoreSha256'],
    'clientAssemblySha256':windows['clientAssemblySha256'],
    'nativeChecks':len(ui['checks']),'samples':ui['samples'],
    'preview':str(Path(ui['folder'])/'castle-ui-3.png'),
    'windows':manifest['windows'],'proofs':proofs,
    'rules':[
        'Preserve the original 28 hexes, roads, exploration, occupancy and battle callbacks.',
        'Frame artwork, native clipped panels and collider picking share the same viewport at all density/aspect samples.',
        'Map panning/zoom transforms one parent; the toolbar, resource heading and footer use the selected control density.',
        'Wheel zoom interpolates; the native-style full-map button restores overview and bounded pan.',
        'Pooled reopen rebinds tile clipping and closing removes the page controls.',
        'Gameplay server, Android APKs, immutable catalogs, saves and normal music/sound preferences remain unchanged.',
    ],
}
write(verification/'castle-ui-release-latest.json',report)
status=read(root/'migration-status.json');status['castleUiLatest']=report;write(root/'migration-status.json',status)
manifest['updatedUtc']=report['verifiedUtc'];manifest['windows']['uiRevision']=19;write(root/'Builds/latest-artifacts.json',manifest)
(root/'Builds/本次修复说明.txt').write_text(
    'PC 攻城战界面优化（界面修订 19；沿用 v18 服务端）\n'
    'PC 最新完整包：BnsmLocal-Windows-x64.zip；本机原入口 I:/BnsmRecovery/Start-LocalGame.cmd 已更新。\n'
    '地图、边框和点击范围统一适配窗口；地图默认显示全图，顶部资源、左侧功能按钮、底部次数分区排列。\n'
    '鼠标滚轮平滑缩放，拖动移动地图，点击“全图”恢复视角；边框之外不能误点地图。\n'
    '保留原版画风、28 个六边形位置、探索、立即占领、守军配置和真实战斗逻辑。\n'
    '已检查 100%/85%/70%、1280x720、4:3、超宽窗口、打开时调整缩放，以及关闭重开后的显示和点击。\n'
    'PC 原生探索/立即占领、攻城战退出重进和真实结算、公共界面、窗口适配及启动器检查均通过。\n'
    '本次 Android 安装包维持 v18，无需重新下载；存档、服务端逻辑和正常音乐音效设置不变，PC CET 继续关闭。\n',encoding='utf-8')
print(json.dumps({key:report[key] for key in ['passed','pcUiRevision','serverRelease','androidUnchanged','nativeChecks','preview']},ensure_ascii=False))
