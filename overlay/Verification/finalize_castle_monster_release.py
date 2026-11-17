"""Bind v20 castle monster and resident-presence checks to delivered artifacts."""
from pathlib import Path
import datetime,hashlib,json

root=Path(__file__).resolve().parents[1];verification=root/'Verification'
def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def write(path,data):Path(path).write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
def digest(path):
 with Path(path).open('rb') as source:return hashlib.file_digest(source,'sha256').hexdigest()

manifest=read(root/'Builds/latest-artifacts.json');windows=read(verification/'windows-full-release.json');mobile=read(verification/'mobile-publication-latest.json')
assert windows['passed'] and mobile['passed'] and windows['sameServerCoreAsAndroid']
core=windows['serverCoreSha256'];client=windows['clientAssemblySha256']
assert digest(root/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')==core
assert digest(Path(windows['server']).with_name('Bnsm.ServerCore.dll'))==core
assert digest(windows['archive'])==windows['archiveSha256']
for role in ['game','server']:
 package=manifest['mobile'][role];assert package['versionCode']==20 and digest(package['path'])==package['sha256']
names=['castle-monster-server-latest.json','castle-monster-native-latest.json','guild-bots-server-latest.json',
 'guild-protocol-latest.json','guild-native-latest.json','pvp-difficulty-server-latest.json','competitive-server-latest.json',
 'castle-lifecycle-server-latest.json','castle-lifecycle-native-latest.json','castle-explore-instant-server-latest.json',
 'castle-explore-instant-native-latest.json','battle-lifecycle-server-latest.json','battle-lifecycle-native-latest.json',
 'document-server-latest.json','castle-ui-native-latest.json','viewport-latest.json','windows-full-native-latest.json',
 'windows-full-host-latest.json','windows-full-launcher-latest.json','windows-cet-latest.json']
proofs={name:read(verification/name) for name in names}
for name,proof in proofs.items():
 assert proof['passed'],name
 if 'serverCoreSha256' in proof:assert proof['serverCoreSha256']==core,name
 if 'clientAssemblySha256' in proof:assert proof['clientAssemblySha256']==client,name
native=proofs['castle-monster-native-latest.json']
assert native['errors']==0 and all(native['checks'].values())
assert all(native[k] for k in ['persistedActualCooperativeDamage','persistedActualFinalClear','persistedIdle'])
assert all(not item['cetCompatible'] for item in proofs['windows-cet-latest.json']['entries'])
report={'passed':True,'release':20,'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'scope':'Resident online presence and native castle monster summon, cooperative/final combat, retreat and durable rewards',
 'normalSaveUsed':False,'androidDeviceVerified':False,'sharedServerCoreSha256':core,'clientAssemblySha256':client,
 'monsterProtocolChecks':len(proofs['castle-monster-server-latest.json']['checks']),
 'monsterNativeChecks':len(native['checks']),'guildBotChecks':len(proofs['guild-bots-server-latest.json']['checks']),
 'rules':[
  'Human last activity is preserved; local resident presence uses the current server clock in guild/friend/search/recommendation packets.',
  'Both native keyInfo/KeyInfo session names are recognized. Monster count packets carry remaining entries.',
  'Summon requires the discovered home-map tile, linked occupied castle and original level/material/VIP limits.',
  'Cooperative and owner-only final phases use original HP, thresholds and reward lists; receipts cannot repeat damage or rewards.',
  'Retreat acknowledges the bound token without damage or fabricated rewards; restart retains boss generation, HP and participants.',
  'The native final-phase fixture seeds the original phase HP only; actual final combat and persisted settlement are verified.',
  'NPC assistance is not fabricated. Original castle UI, exploration/instant actions and tiered PvP remain covered by regressions.'
 ],'windows':manifest['windows'],'android':mobile['packages'],'proofs':proofs}
write(verification/'castle-monster-release-latest.json',report)
status=read(root/'migration-status.json');status['castleMonsterLatest']=report;write(root/'migration-status.json',status)
manifest['updatedUtc']=report['verifiedUtc'];manifest['windows']['release']=20;write(root/'Builds/latest-artifacts.json',manifest)
(root/'Builds/本次修复说明.txt').write_text(
 'v20：攻城战魔物召唤/战斗与人机在线状态修复\n'
 'PC 完整包：BnsmLocal-Windows-x64.zip；本机原入口 I:/BnsmRecovery/Start-LocalGame.cmd 已更新。\n'
 'Android 请同时覆盖更新 Android-ARM64/BnsmLocal-arm64.apk 和 PhoneServer/apk/local.bnsm.serverhost-Signed.apk。\n'
 '保留原存档，不需要卸载或清除数据；PC 包保留界面修订 19 的攻城战 UI，CET 继续关闭。\n'
 '门派、好友列表、好友搜索和推荐的人机使用当前服务端在线时间，真人离线时间保持原记录。\n'
 '修复召唤包 KeyInfo 大小写兼容与挑战次数的剩余/已用混淆，补齐两只魔物的召唤、协力血量、最终战、原表奖励及战绩。\n'
 '魔物战中途退出需服务端确认，重进和重启保留有效状态；重复请求不重复扣次数、发奖励或清除新战斗。\n'
 '保留原版等级、相连城池占领、召唤次数、协力参与及最终战城主限制。\n'
 'PC 原生窗口已检查在线显示、两只魔物召唤、协力战/最终战的退出重进与真实结算；另通过门派、竞技、攻城战状态、界面缩放及启动检查。\n'
 'Android 已检查签名、ARM64 与 16-KiB 对齐；未连接实体手机执行现场测试。\n',encoding='utf-8')
print(json.dumps({k:report[k] for k in ['passed','release','monsterProtocolChecks','monsterNativeChecks','guildBotChecks','sharedServerCoreSha256']},ensure_ascii=False))
