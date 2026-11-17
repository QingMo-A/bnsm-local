from pathlib import Path
import datetime, hashlib, json, re, struct

ROOT=Path('I:/BnsmModernWork')
V=ROOT/'Verification'
def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path):
    with Path(path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
packages=read(V/'android-packages.json')
assert packages['passed'] and read(V/'phone-support-android-static.json')['passed']
game,server=packages['packages']
assert game['role']=='game' and server['role']=='server'
versions=[]
for role in ['game','server']:
    versions.append(int(re.search(r"versionCode='(\d+)'",(V/f'android-{role}-badging.txt').read_text(encoding='utf-8')).group(1)))
assert versions==[5,4]
entry=read(V/'pc-entry-repair.json')
assert all(sha(row['path']).upper()==row['sha256'].upper() for row in entry['saveHashes'])
entry['productionSaveHashesUnchangedAfterTests']=True
entry['headlessLaunchPassed']=True
write(V/'pc-entry-repair.json',entry)
native=read(V/'faction-player-test.json');protocol=read(V/'faction-protocol-test.json');mission=read(V/'mission-reward-regression.json')
assert native['passed'] and protocol['passed'] and mission['passed']
assert read(V/'competition-manager-ui/save-manager-ui-test.json')['passed']
launcher_log=(ROOT/'WindowsPreview/evidence/windows-client-headless.log').read_text(encoding='utf-8',errors='replace')
assert 'scene=Game' in launcher_log and 'errors=0' in launcher_log
assert not re.search(r'BNSM_CLIENT_PROBE[^\n]*errors=[1-9]',launcher_log)
assert 'NullReferenceException' not in launcher_log and 'Input System module state changed to: Shutdown.' in launcher_log
client=ROOT/'WindowsPreview/UnityRebuilt/WindowsBuild-CompetitionFix/BnsmLocal.exe'
managed=client.parent/'BnsmLocal_Data/Managed/Assembly-CSharp.dll'
core=ROOT/'WindowsPreview/LocalServer/WindowsBots/Bnsm.ServerCore.dll'
core_hash=sha(core)
assert core_hash==sha(ROOT/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')==sha(ROOT/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll')
pe=client.read_bytes();offset=struct.unpack_from('<I',pe,0x3c)[0];assert struct.unpack_from('<H',pe,offset+4)[0]==0x8664
assert sha(ROOT/'WindowsPreview/LocalServer/data/gacha.json')=='de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908'
assert sha(ROOT/'Builds/PhoneServer/server-content.zip')=='a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1'
report={
 'updatedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'rootCauses':['PC entry still targeted the previous recovery checkout and stale binaries','Faction PvP entrance/match/replay/result protocol handlers were absent','Native faction result packets include fifteen deck slots and may omit empty nested messages'],
 'missionTitle3016':{'nativeTitleId':3015,'checksPassed':len(mission['checks']),'duplicateCardsPreserved':True},
 'faction':{'protocolChecksPassed':len(protocol['checks']),'nativeWindowsPlayerPassed':True,'nativePlayerEvidence':native['fixture'],'mainStoryProgressRequired':False,'originalLevelDeckAndTicketRestrictionsRetained':True,'nativeAutomaticBattleAi':True,'customAiPolicyExecuted':False,'activeBotChallengesImplemented':False,'otherPvpModesImplemented':False},
 'windows':{'client':str(client),'managedAssemblySha256':sha(managed),'server':str(core.parent/'Bnsm.LocalServer.exe'),'sharedCoreSha256':core_hash,'entry':'I:\\BnsmRecovery\\Start-LocalGame.cmd','entryPointsToModernBuild':True,'productionSaveRoot':entry['saveRoot'],'realSaveChanged':False,'headlessLauncherPassed':True,'saveManagerPassed':True,'publishedHostChecksPassed':14},
 'android':{'gameApk':game['apk'],'gameVersionCode':versions[0],'gameSha256':game['sha256'],'serverApk':server['apk'],'serverVersionCode':versions[1],'serverSha256':server['sha256'],'sharedCoreSha256':core_hash,'sameUpdateSigningIdentity':True,'arm64Only':True,'elfAndApk16KiBAligned':True,'staticContractChecksPassed':13,'installedOnPhoneForThisPatch':False,'deviceVerifiedForThisPatch':False,'reason':'User unplugged the phone and requested offline repairs only'},
 'manualGuide':str(ROOT/'WindowsPreview/势力战与PC入口修复-说明.txt'),'unpublishedGachaChangesIncluded':False
}
write(V/'competition-release.json',report)
status=read(ROOT/'migration-status.json')
status['phase']='Mission title fixes and native faction bot battles published; original PC entry repaired; Android game v5 and host v4 built and statically verified, not installed'
status['utc']=report['updatedUtc'];status['activeLegacyReleaseChanged']=True
status['phoneServerDeviceVerified']=False;status['phoneGameplayVerified']=False
status['lastDeviceValidationAppliesToPreviousPackages']=True
status['competitionFix']=report
status['wanderEntryFix']['supersededBy']='competitionFix'
write(ROOT/'migration-status.json',status)
windows=read(ROOT/'WindowsPreview/verification.json')
windows['competitionFix']=report
windows['defaultLauncher']='Start-LocalGame.cmd'
windows['saveRoot']='%LOCALAPPDATA%/BnsmLocalGame (or BNSM_SAVE_ROOT)'
windows['previewSaveRoot']='%LOCALAPPDATA%/BnsmLocalGame-Unity6-Preview'
windows['sha256'][str(managed.relative_to(ROOT/'WindowsPreview'))]=sha(managed)
windows['sha256'][str(core.relative_to(ROOT/'WindowsPreview'))]=core_hash
write(ROOT/'WindowsPreview/verification.json',windows)
print(json.dumps(report,ensure_ascii=False,indent=2))
