"""Bind the interactive export optimization to its actual published APK and contracts."""
from pathlib import Path
import datetime, hashlib, json

ROOT=Path(__file__).resolve().parents[1]
def read(name): return json.loads((ROOT/name).read_text('utf-8-sig'))
def sha(path):
    with Path(path).open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()

game=read('Builds/Android-Integrated/latest.json')
manager=read('Builds/Android-SaveManager/latest.json')
build=read('Verification/android-export-performance-build.json')
before=read('Verification/android-manager-contract/export-performance-baseline.json')
after=read('Verification/android-manager-contract/export-performance.json')
regressions=read('Verification/android-embedded-regressions/result.json')
assert game['passed'] and game['versionCode']>=39 and game['exportPerformanceVerified']
assert game['sha256']==build['apkSha256']==sha(game['apk'])
assert manager['gameSha256']==game['sha256'] and manager['sameSigningCertificate']
assert sha(manager['apk'])==manager['sha256']
assert not game['deviceVerified'] and not game['installed']
assert game['exportTimingsIncluded'] and game['storedNestedSnapshot'] and game['workerExportHandoffSourceAndBuildVerified']
for group in [build['sourceSha256'],after['sourceSha256']]:
    for name,want in group.items(): assert sha(ROOT/name)==want,name
counts={}
for kind,minimum in [('csharp',16),('console-export',13),('numeric',19),('bot',10),('import-core',4)]:
    item=read('Verification/android-manager-contract/'+kind+'-result.json')
    assert item['passed'] and item['coreSha256']==game['embeddedCoreSha256']
    counts[kind]=len(item['checks']) if isinstance(item['checks'],list) else item['checks']
    assert counts[kind]>=minimum
assert after['passed'] and before['passed'] and after['checks']>=42 and not after['phoneUsed']
assert after['documentBytes']==before['documentBytes'] and after['logBytes']==before['logBytes']
assert regressions['passed'] and len(regressions['checks'])==15 and regressions['coreSha256']==game['embeddedCoreSha256']
windows=read('Verification/windows-full-release.json')
assert sha(windows['archive'])==windows['archiveSha256']
result=dict(passed=True,checkedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    apk=game['apk'],versionCode=game['versionCode'],sha256=game['sha256'],bytes=game['bytes'],embeddedCoreSha256=game['embeddedCoreSha256'],
    changes=['Worker-to-JNI packaging handoff without Unity Update','Allocation-light opaque JSON validation','Fast inner compression and streamed outer logs','Stored original inner ZIP with SHA-256/CRC/size checks','Per-stage export timing diagnostics'],
    hostAndFileContracts=counts,largeArchiveChecks=after['checks'],portableGameplayRegressions=len(regressions['checks']),
    desktopSyntheticComparison=game['desktopSyntheticExportComparison'],
    unchangedWindowsArchiveSha256=windows['archiveSha256'],managerApkUnchangedSha256=manager['sha256'],
    deviceVerified=False,installed=False,reportedUserArchiveReceived=False,
    verificationLimit='Synthetic desktop AOSP timings and full-host native-file contracts; no connected Android phone or reported user archive',
    sourceSha256=build['sourceSha256'])
(ROOT/'Verification/android-export-performance-final.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:result[k] for k in ['passed','apk','versionCode','sha256','largeArchiveChecks','portableGameplayRegressions','deviceVerified']},ensure_ascii=False))
