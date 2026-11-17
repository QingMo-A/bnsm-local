import datetime, hashlib, json, pathlib, shutil

root=pathlib.Path('I:/BnsmModernWork')
preview=root/'WindowsPreview'
verification=root/'Verification'
result=json.loads((verification/'desktop-pages-latest.json').read_text(encoding='utf-8-sig'))
assert result['passed'] and result['errors']==0 and all(result['checks'].values())
evidence=pathlib.Path(result['folder'])
def sha(path):
    with open(path,'rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
contracts={}
for suffix,width,height in [('100',1920,1080),('85',1920,1080),('70',1920,1080),('3-85',1024,768),('4-70',2560,1080)]:
    book=read(evidence/f'book-{suffix}.json')
    tabs=[next(r for r in book if r['path'].endswith('/03_SortingBtn/'+name)) for name in ['00_Card','01_Weapon','02_Necklace','04_Earring','05_Gem']]
    centers=[r['pixel'][0] for r in tabs]
    contracts['bookCentered'+suffix]=abs(sum(centers)/5-width/2)<2
    contracts['bookSmallEqualGaps'+suffix]=max(centers[i+1]-centers[i] for i in range(4))-min(centers[i+1]-centers[i] for i in range(4))<2
    contracts['bookProgressInside'+suffix]=all(0<=r['widgetRect'][0] and r['widgetRect'][0]+r['widgetRect'][2]<=width+1 for r in book if r['active'] and r['widgetRect'] and r['path'].endswith('/ProgressiveBar/BG'))
    deck=read(evidence/f'deck-{suffix}.json')
    slots=[r for r in deck if r['active'] and '/Deck_CardListRoot/ListBg/Slot_' in r['path'] and r['path'].split('/')[-1] in ['Slot_01','Slot_02','Slot_03','Slot_04','Slot_05']]
    contracts['deckFiveCentered'+suffix]=len(slots)==5 and abs(sum(r['pixel'][0] for r in slots)/5-width/2)<2
    contracts['deckLowerHalf'+suffix]=all(0<r['pixel'][1]<height*.5 for r in slots)
    bag=read(evidence/f'bag-{suffix}.json')
    tabs=[r for r in bag if r['active'] and '/03_SortingBtn/Sorting_' in r['path'] and r['path'].split('/')[-1].startswith('Sorting_')]
    contracts['bagCentered'+suffix]=len(tabs)==5 and abs((min(r['hitRect'][0] for r in tabs)+max(r['hitRect'][0]+r['hitRect'][2] for r in tabs))/2-width/2)<2
    contracts['bagNoOverlappingTabs'+suffix]=all(tabs[i]['hitRect'][0]+tabs[i]['hitRect'][2]<tabs[i+1]['hitRect'][0] for i in range(4))
    stage=read(evidence/f'stage-{suffix}.json')
    rewardList=next(r for r in stage if r['active'] and r['list'] and r['path'].endswith('/RewardListRoot'))
    contracts['stageFewRewardsUnclipped'+suffix]=rewardList['list'][6]==0
    heading=next(r for r in stage if r['active'] and r['path'].endswith('/Rewards/RewardListBg/Text'))
    contracts['stageRewardHeadingBelowEnemyPanel'+suffix]=heading['widgetRect'][1]+heading['widgetRect'][3] < height/2-120*height/(720*100/int(suffix.split('-')[-1]))+10
assert all(contracts.values()),[key for key,value in contracts.items() if not value]
old=read(verification/'pc-entry-repair.json')
save_checks=[{'path':entry['path'],'unchanged':sha(entry['path'])==entry['sha256'].lower()} for entry in old['saveHashes']]
assert all(row['unchanged'] for row in save_checks)
immutable={
 'gacha':(preview/'LocalServer/data/gacha.json','de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908'),
 'androidGame':(root/'Builds/Android-ARM64/BnsmLocal-arm64.apk','f4089e61615e1c3dc8b230aa643b56344306a6523da5cb0c9eb09046de24a0f2'),
 'androidServer':(root/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk','da72ce593cbcc15f4b4f27e80471da5ce0b1e48fbae77a5996434bdb24371cce'),
 'serverContent':(root/'Builds/PhoneServer/server-content.zip','a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1'),
 'serverCore':(preview/'LocalServer/WindowsBots/Bnsm.ServerCore.dll','97ab31d40db5f9471049a10bc6a3aa901de0984883740b36eae9e88fa3cf8661')}
hashes={key:sha(path) for key,(path,expected) in immutable.items()}
assert all(hashes[key]==expected for key,(path,expected) in immutable.items())
source=root/'Builds/Windows-DesktopPages'
target=preview/'UnityRebuilt/WindowsBuild-DesktopPages'
assert not target.exists(),'The verified destination must be new; do not overwrite a running build.'
shutil.copytree(source,target)
assembly='BnsmLocal_Data/Managed/Assembly-CSharp.dll'
assert sha(source/assembly)==sha(target/assembly)
workspace=read(preview/'LocalWorkspace.json')
previous=workspace['clientPath'];workspace['clientPath']=str(target/'BnsmLocal.exe')
workspace['retiredClientPaths']=list(dict.fromkeys([previous]+workspace.get('retiredClientPaths',[])))
write(preview/'LocalWorkspace.json',workspace)
launcher=preview/'Start-LocalGame.ps1'
launcher.write_text(launcher.read_text(encoding='utf-8-sig').replace('UnityRebuilt\\WindowsBuild-MissionRewards\\BnsmLocal.exe','UnityRebuilt\\WindowsBuild-DesktopPages\\BnsmLocal.exe'),encoding='utf-8-sig')
guide=preview/'PC界面缩放优化-说明.txt'
guide.write_text('''PC 界面缩放优化（2026-10-31）

正常入口：I:\\BnsmRecovery\\Start-LocalGame.cmd
关闭旧游戏后，从这个入口重新启动即可使用新版，无需换存档。

1. 风云棋盘：人物相对格子的位置和大小重新调整；骰子、提示区改为底部居中。
2. 挑战塔：天空铺满窗口，塔楼与路线一起适配；末端节点对齐楼梯，隐藏透出的主城入口。
3. 任务（图三）：扩大列表高度，随窗口和 UI 缩放调整容量，滚轮能继续查看下面的任务。
4. 图鉴：五个顶部分类整体居中并保留小间隔；整理进度条、底部说明和操作按钮，修复滚轮被滚动条状态锁住的问题。
5. 背包：分类按钮紧凑居中，点击范围与新间距同步。
6. 关卡预览：加宽奖励区，避免少量奖励被裁掉；体力、扫荡提示与进入按钮单独排列，100% 比例下避免盖住敌人信息。
7. 选卡：上方可选卡牌区扩展，五个出战位置居中下移，减少多余留白。

在 1920×1080 的 100%、85%、70%，1024×768 的 85%，2560×1080 的 70% 下做了原生界面回归。
同一分辨率下缩小 UI 会显示更多列表内容，保留原有选择、详情和战斗入口。
图鉴已验证列表空白处和卡牌上的滚轮、分类点击及卡牌选择。

本次只发布 PC 游戏客户端；手机 APK 和服务端未更换。
测试使用独立存档，正常 PC 存档文件校验未变化；测试已静音。
''',encoding='utf-8-sig')
release={'updatedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'PC desktop UI only','client':str(target/'BnsmLocal.exe'),'managedAssemblySha256':sha(target/assembly),'entry':'I:\\BnsmRecovery\\Start-LocalGame.cmd','nativeChecks':len(result['checks']),'nativePassed':True,'nativeScriptErrors':0,'evidence':str(evidence),'geometryChecks':len(contracts),'geometry':contracts,'productionSaveFilesChecked':len(save_checks),'realSaveChanged':False,'unchangedHashes':hashes,'androidRebuilt':False,'manualGuide':str(guide),'headlessLauncherPassed':False}
write(verification/'desktop-pages-release.json',release)
write(verification/'desktop-pages-save-checks.json',save_checks)
status=read(root/'migration-status.json');status['desktopPagesPatch']=release;write(root/'migration-status.json',status)
status=read(preview/'verification.json');status['desktopPagesPatch']=release;status['sha256']['UnityRebuilt\\WindowsBuild-DesktopPages\\'+assembly.replace('/','\\')]=release['managedAssemblySha256'];write(preview/'verification.json',status)
print(json.dumps({'client':release['client'],'nativeChecks':release['nativeChecks'],'geometryChecks':len(contracts),'saveFilesUnchanged':len(save_checks)},indent=2))
