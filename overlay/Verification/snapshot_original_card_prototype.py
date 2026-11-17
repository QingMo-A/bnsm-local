"""Bind the reviewed local trial to its source commit and tested binaries."""
from pathlib import Path
import hashlib,json,subprocess
from PIL import Image

root=Path(__file__).resolve().parents[1]
baseline='baseline/v28-before-new-card-20261107'
def git(*args):return subprocess.check_output(['git',*args],cwd=root).decode('utf-8').strip()
def digest(path):
    value=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):value.update(block)
    return value.hexdigest()
native=json.loads((root/'Verification/original-card-native-latest.json').read_text('utf-8-sig'))
server=json.loads((root/'Verification/original-card-server-latest.json').read_text('utf-8-sig'))
interactive=json.loads((root/'Verification/original-card-interactive-latest.json').read_text('utf-8-sig'))
trial=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest'
client=digest(trial/'BnsmLocal_Data/Managed/Assembly-CSharp.dll')
core=digest(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll')
tables={}
for relative in ['Project/Assets/StreamingAssets/DynamicAssets/CDT.bytes','WindowsPreview/LocalServer/data/tables/CardDataTable.pb']:
    original=subprocess.check_output(['git','show',baseline+':'+relative],cwd=root)
    if original.startswith(b'version https://git-lfs.github.com/spec/v1'):
        tables[relative]=digest(root/relative)==next(x.split(':',1)[1] for x in original.decode('ascii').splitlines() if x.startswith('oid sha256:'))
    else:
        tables[relative]=git('hash-object','--no-filters',relative)==git('rev-parse',baseline+':'+relative)
art={}
for name in ['shenyan-full.png','shenyan-crop.png']:
    path=root/'Project/Assets/Resources/LocalOriginalCard'/name
    with Image.open(path) as im:
        art[name]=dict(size=list(im.size),mode=im.mode,alphaExtrema=list(im.getchannel('A').getextrema()),sha256=digest(path))
official=json.loads((root/'Builds/latest-artifacts.json').read_text('utf-8-sig'))
win=official['windows']
untouched=dict(windowsArchive=digest(win['archive'])==win['archiveSha256'],windowsClient=digest(Path(win['client']).parent/'BnsmLocal_Data/Managed/Assembly-CSharp.dll')==win['clientAssemblySha256'],windowsServer=digest(Path(win['server']).parent/'Bnsm.ServerCore.dll')==win['serverCoreSha256'])
for key,item in official['mobile'].items():untouched['android'+key.capitalize()]=digest(item['path'])==item['sha256']
baseline_commit=git('rev-parse',baseline+'^{commit}')
checks=dict(nativePassed=native['passed'],serverPassed=server['passed'],interactivePreviewReady=interactive['passed'] and interactive['clientAssemblySha256']==client,visibleArtwork=native['checks']['nativeArtworkVisible'],testedClientMatches=client==native['clientAssemblySha256'],testedCoreMatches=core==native['serverCoreSha256']==server['serverCoreSha256'],originalTablesUnchanged=all(tables.values()),officialArtifactsUnchanged=all(untouched.values()),mainPreservesBaseline=git('rev-parse','main')==baseline_commit,cleanSourceTree=not git('status','--porcelain'))
report=dict(passed=all(checks.values()),checks=checks,commit=git('rev-parse','HEAD'),branch=git('branch','--show-current'),baselineCommit=baseline_commit,remoteConfigured=bool(git('remote')),cardId=1900000001,art=art,originalTables=tables,officialArtifacts=untouched,clientAssemblySha256=client,serverCoreSha256=core,nativeEvidence=native['folder'],scope=dict(pcNativeVerified=True,androidDeviceVerified=False,fullyLayeredCubismModel=False,editableCubismProject=False,sourceCombatRigAndMechanicsReused=True))
output=root/'Verification/original-card-prototype-result.json'
output.write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
print(json.dumps(dict(passed=report['passed'],checks=checks,commit=report['commit'],report=str(output)),ensure_ascii=False))
if not report['passed']:raise SystemExit(1)
