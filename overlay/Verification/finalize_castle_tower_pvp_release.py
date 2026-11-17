from pathlib import Path
import datetime,hashlib,json,zipfile
root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text(encoding='utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def digest(path):return hashlib.sha256((root/path).read_bytes()).hexdigest()
pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json');manifest=read('Builds/latest-artifacts.json')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll');client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll')
assert pc['passed'] and mobile['passed'] and all(x['versionCode']==23 for x in mobile['packages'])
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')
names=['castle-tower-server-latest.json','castle-tower-native-latest.json','castle-invasion-server-latest.json','rank-deck-server-latest.json','castle-lifecycle-server-latest.json','castle-lifecycle-native-latest.json','castle-monster-server-latest.json','castle-monster-native-latest.json','castle-explore-instant-server-latest.json','castle-explore-instant-native-latest.json','competitive-server-latest.json','pvp-difficulty-server-latest.json','manual-pvp-server-latest.json','battle-lifecycle-server-latest.json','battle-lifecycle-native-latest.json','activity-expansion-protocol-latest.json','activity-expansion-native-latest.json']
names += ['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]
names += ['manual-pvp-native-'+mode+'-latest.json' for mode in ['rank','balance']]
proofs={name:read('Verification/'+name) for name in names}
for name,item in proofs.items():
 assert item['passed'] and item['serverCoreSha256']==core,name
 if 'clientAssemblySha256' in item:assert item['clientAssemblySha256']==client,name
assert proofs['competitive-native-infinite-latest.json']['threeCardDefenceSavedOnServer'] and proofs['competitive-native-infinite-latest.json']['savedSparseTeamsReopen']
assert proofs['castle-lifecycle-native-latest.json']['emptyCastleDeckShowsHint'] and proofs['castle-lifecycle-native-latest.json']['fallenInvasionCardShowsRecoveryHint']
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as archive:
 for suffix,sha in [('LocalServer/WindowsLatest/Bnsm.ServerCore.dll',core),('UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll',client)]:
  assert hashlib.sha256(archive.read('BnsmLocal-Windows-x64/'+suffix)).hexdigest()==sha
notes=(
 'v23：攻城战状态、挑战塔提示、斗神争霸卡组与 PvP 排名修复\n'
 'Windows 完整包：BnsmLocal-Windows-x64.zip。本机 WindowsPreview 已同步更新。\n'
 'Android 游戏和手机服务端须同时覆盖更新为 v23，保留原存档，无需卸载。\n'
 '修复自己已占领城池重复点击不弹详情、缓存更新漏回调、关闭后迟到响应重新打开的问题。\n'
 '人机仅占领满足原版等级/VIP及守军条件的普通城池，不再将所有28块和魔物地块都标为占领。\n'
 '入侵等级不足会显示具体解锁等级，空卡组、缺失/修炼/濒死卡及阵营职业限制分别提示，日志记录具体分支。\n'
 '按原版统一濒死规则：入侵阵亡需更换或恢复；自己的占领、修复和防守可正常使用，不会偷偷清除入侵阵亡记录。\n'
 '挑战塔先发与保存的备用卡均校验本层永久/今日禁用，服务端区分禁用、阵亡、修炼、楼层和战斗状态错误。\n'
 '斗神争霸三组各至少一张卡即可保存，保留全部15个位置，不再额外要求11张；重复、修炼和空组仍正常拦截。\n'
 '通用卡组关闭/确认等待保存成功后隐藏，失败时保留编辑界面。\n'
 'PvP 对手按强/中/易提供不同跨度且不等距的名次；胜利取得开战时锁定的较高对手名次，主动挑战失败不降名次。\n'
 '真实回放和结算后才变更排名，预览、退出、重试不能晋升；名次与阵容保存、重启一致。\n'
 '保留已有活动、玩法、存档和配置；Windows CET兼容设置保持关闭。\n'
 'PC原生界面和战斗、隔离服务端状态回归通过；Android签名、ARM64与16-KiB兼容检查通过，本次未连接实体手机测试。\n')
(root/'Builds/本次修复说明.txt').write_text(notes,encoding='utf-8')
now=datetime.datetime.now(datetime.timezone.utc).isoformat();manifest['windows']['release']=23;manifest['updatedUtc']=now;write('Builds/latest-artifacts.json',manifest)
report=dict(passed=True,release=23,verifiedUtc=now,sharedServerCoreSha256=core,clientAssemblySha256=client,windows=manifest['windows'],android=mobile['packages'],proofs=names,serverChecks={name:len(item.get('checks',[])) for name,item in proofs.items() if 'native' not in name},androidDeviceVerified=False,productionSaveUsed=False,userLogAnalysis='Verification/castle-invasion-user-log-analysis.json')
write('Verification/castle-tower-pvp-release-latest.json',report)
status=read('migration-status.json');status['castleTowerPvpLatest']=report;status['phase']='Castle eligibility/casualties, tower feedback, sparse infinite decks and captured-rank PvP verified and published as v23.';write('migration-status.json',status)
print(json.dumps({k:report[k] for k in ['passed','release','sharedServerCoreSha256','clientAssemblySha256','serverChecks']},ensure_ascii=False))
