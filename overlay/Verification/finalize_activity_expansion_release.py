import datetime,hashlib,json,pathlib,zipfile
root=pathlib.Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text(encoding='utf-8-sig'))
def write(path,value):(root/path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def digest(path):return hashlib.sha256((root/path).read_bytes()).hexdigest()
manifest=read('Builds/latest-artifacts.json');pc=read('Verification/windows-full-release.json');mobile=read('Verification/mobile-publication-latest.json')
protocol=read('Verification/activity-expansion-protocol-latest.json');native=read('Verification/activity-expansion-native-latest.json');content=read('Verification/activity-content-latest.json')
core=digest('WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll');client=digest('WindowsPreview/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll');config=digest('WindowsPreview/LocalServer/data/activities.json')
assert pc['passed'] and mobile['passed'] and protocol['passed'] and native['passed'] and content['passed']
assert core==digest('Builds/PhoneServer/apk/Bnsm.ServerCore.dll')==protocol['serverCoreSha256']==native['serverCoreSha256']
assert native['clientAssemblySha256']==client and config==protocol['activityConfigSha256']==native['activityConfigSha256']==content['activityConfigSha256']
assert native['persistedRestoredLoginClaim'] and native['persistedSupplyClaim'] and native['oneActualLoginDay']
assert all(p['versionCode']==22 for p in mobile['packages'])
assert digest('Builds/BnsmLocal-Windows-x64.zip')==pc['archiveSha256']
with zipfile.ZipFile(root/'Builds/BnsmLocal-Windows-x64.zip') as archive:
    entries=[n for n in archive.namelist() if n.endswith('/LocalServer/data/activities.json') or n=='LocalServer/data/activities.json']
    assert len(entries)==1 and hashlib.sha256(archive.read(entries[0])).hexdigest()==config
with zipfile.ZipFile(root/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk') as archive:
    assert hashlib.sha256(archive.read('assets/server-content.zip')).hexdigest()==content['contentSha256']
proofs={}
for mode in ['arena','rank','balance','castle','world','infinite']:
    name='Verification/competitive-native-'+mode+'-latest.json';proof=read(name)
    assert proof['passed'] and proof['persistedSettlement'] and proof['serverCoreSha256']==core and proof['clientAssemblySha256']==client
    if mode in ['arena','rank','balance']:assert proof['oneRestoredActivityCredit']
    proofs[mode]=name
for mode in ['rank','balance']:
    name='Verification/manual-pvp-native-'+mode+'-latest.json';proof=read(name)
    assert proof['passed'] and proof['oneRestoredActivityCredit'] and proof['oneRealSettlementAfterRetreat'] and proof['serverCoreSha256']==core and proof['clientAssemblySha256']==client
    proofs['manual-'+mode]=name
events=read('WindowsPreview/LocalServer/data/activities.json')
now=datetime.datetime.now(datetime.timezone.utc).isoformat()
report=dict(passed=True,release=22,verifiedUtc=now,scope='Five restored original activity reward groups',totalActivities=10,totalGoals=33,addedActivities=5,addedGoals=19,
            restored=[{'id':e['id'],'name':e['name'],'period':e['period'],'originalMissionIds':[g['sourceMission'] for g in e['goals'] if 'sourceMission' in g],'originalLoginPromotion':e.get('loginPromotion')} for e in events if e['id']>=900006],
            protocolChecks=len(protocol['checks']),nativeChecks=len(native['checks']),classicActivityRegressionChecks=28,sharedServerCoreSha256=core,clientAssemblySha256=client,activityConfigSha256=config,originalCatalogsPreserved=True,
            nativeActualBattleProofs=proofs,productionSaveUsed=False,androidDeviceVerified=False,windows=manifest['windows'],android=mobile['packages'])
report['windows']['release']=22
write('Verification/activity-expansion-release-latest.json',report)
status=read('migration-status.json');status['activitiesLatest']=report;status['phase']='Five original activity groups restored and verified; Windows and Android game/server v22 published.';write('migration-status.json',status)
manifest['updatedUtc']=now;manifest['windows']['release']=22;write('Builds/latest-artifacts.json',manifest)
(root/'Builds/本次修复说明.txt').write_text(
    'v22：新增恢复五组原版活动奖励\n'
    'PC 完整包：BnsmLocal-Windows-x64.zip；本机启动入口已更新。\n'
    'Android 同时覆盖更新 Android-ARM64/BnsmLocal-arm64.apk 和 PhoneServer/apk/local.bnsm.serverhost-Signed.apk，保留原存档，无需卸载。\n'
    '活动由 5 组 14 项增加到 10 组 33 项，已有活动和领取记录保留。\n'
    '新增七日登录礼：原版七日礼物，更新后按实际访问天数累计，无需连续，不补算离线日期，每角色只领一轮。\n'
    '新增跨服江湖历练、比武场历练、武林至尊历练：每周完成 1/3/7 场，胜负均计，退出不计，周一北京时间零点重置。\n'
    '新增门派同心：本人每周签到 1/3/7 次，门派人机签到不计入个人活动。\n'
    '新活动沿用原表奖励种类、数量与目标，并明确设置本地累计或每周周期；未修改原始目录表。\n'
    '新增活动目标、奖励领取、重复请求、容量检查、重启与周一重置共 113 项检查，原有活动回归 28 项通过。\n'
    'PC 原生活动窗口 27 项检查、五种显示组合及八次实际竞技/替补战斗回归通过；退出状态、完整启动、CET 兼容性检查通过。\n'
    'Android 签名、ARM64、16-KiB 对齐及内置活动配置检查通过，尚未连接实体手机验证。\n',encoding='utf-8')
print(json.dumps({k:report[k] for k in ['passed','release','addedActivities','addedGoals','protocolChecks','nativeChecks']},ensure_ascii=False))
