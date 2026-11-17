from pathlib import Path
import datetime,hashlib,json
root=Path('I:/BnsmModernWork')
def read(name):return json.loads((root/'Verification'/name).read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
windows=read('windows-full-release.json');mobile=read('mobile-publication-latest.json');native=read('document-ui-latest.json');server=read('document-server-latest.json')
assert windows['passed'] and mobile['passed'] and native['passed'] and server['passed']
assert windows['clientAssemblySha256']==native['clientAssemblySha256']
assert windows['serverCoreSha256']==native['serverCoreSha256']==server['serverCoreSha256']
immutable={
 'WindowsPreview/LocalServer/data/gacha.json':'de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908',
 'Builds/PhoneServer/server-content.zip':'a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1',
 'Project/Assets/StreamingAssets/DynamicAssets/CDT.bytes':'1ade0ffce7b3b59d4b6fe326be7f089612fd2ea02a7bf49f12bc595712cc2a76'}
for path,expected in immutable.items():assert digest(root/path)==expected,path
assert len(list((root/'Builds').rglob('*.apk')))==2
report={
 'passed':True,'release':13,'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'sourceDocument':'I:/BnsmRecovery/Bug统计.docx','sourceSnapshotSha256':digest(root/'Verification/bug-document-20261103/current.docx'),
 'includesAddedScreenshot13':True,'nativeUiChecks':len(native['checks']),'nativeUiErrors':native['errors'],
 'serverDocumentChecks':len(server['checks']),'normalSaveUsed':False,'androidDeviceVerified':False,
 'clientAssemblySha256':native['clientAssemblySha256'],'sharedServerCoreSha256':server['serverCoreSha256'],
 'fixed':['签到和活动入口移到体力右侧，隐藏时拒绝点击','体力物品介绍中文','皮肤商店列表、立绘和购买区布局','势力战恢复周排期，保留有效限时全开','无限挑战规则和八卦牌入口分别靠右','职业强化七职业布局','跨服江湖大战、武林至尊、天下争霸、斗神争霸、门派大战布局','一统天下及页面遮罩覆盖窗口','天下争霸按UTC+8每日22:00结算，持久化邮件领取和离线补发','比武商店开放门槛、原价扣币及错误原因','人机真实抽卡大厅播报、世界聊天扩充和防复读','势力战通关难度同步及高难度解锁低难度扫荡','箱子复用统一容量检查，避免已有扩展库存误拦截'],
 'proofs':['document-ui-latest.json','document-server-latest.json','viewport-latest.json','windows-full-native-latest.json','windows-full-launcher-latest.json','battle-lifecycle-native-latest.json','resource-missions-native-latest.json','competitive-server-latest.json','android-packages.json'],
 'immutableCatalogHashes':immutable,'windows':windows['archive'],'android':mobile['packages']}
(root/'Verification/document-release-latest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
status=root/'Verification/migration-status.json'
if status.exists():
    state=json.loads(status.read_text(encoding='utf-8-sig'));state['bugDocumentRelease']={'release':13,'passed':True,'report':str(root/'Verification/document-release-latest.json'),'androidDeviceVerified':False};status.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
(root/'Builds/本次修复说明.txt').write_text('v13 文档修复版\nPC：BnsmLocal-Windows-x64.zip\nAndroid 游戏：Android-ARM64/BnsmLocal-arm64.apk\nAndroid 服务端：PhoneServer/apk/local.bnsm.serverhost-Signed.apk\n手机请两个APK一起覆盖更新，保留存档。\n天下争霸按服务器时间 UTC+8 每日22:00结算，奖励在礼物箱领取。\n修复界面缩放/遮罩、势力战扫荡和开放时间、宝箱容量判断、体力说明与比武商店，增加人机大厅播报和世界聊天。\n已完成PC原生测试及Android静态检查，本次未做手机实机验证。\n',encoding='utf-8-sig')
print(json.dumps({k:report[k] for k in ['passed','release','nativeUiChecks','nativeUiErrors','serverDocumentChecks','androidDeviceVerified']},ensure_ascii=False))
