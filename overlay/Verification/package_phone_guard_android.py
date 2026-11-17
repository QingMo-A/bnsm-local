from pathlib import Path
import shutil,json,time,subprocess,hashlib
root=Path('I:/BnsmModernWork')
source=root/'Builds/Android-Diagnostics/GradleExport'
report=json.loads((source.parent/'build-result.json').read_text('utf-8-sig'));assert report['result']=='Succeeded' and report['errors']==0
for name in ['LocalServiceLink.java','LocalSupport.java']:
 p=root/'Project/Assets/Plugins/Android'/name
 target=source/'unityLibrary/src/main/java/local/bnsm/bridge'/name
 shutil.copy2(p,target)
 assert hashlib.sha256(p.read_bytes()).digest()==hashlib.sha256(target.read_bytes()).digest()
dest=Path('C:/BnsmModernBuildCache/DiagnosticsGradle')
assert not dest.exists()
started=time.monotonic();shutil.copytree(source,dest,ignore=shutil.ignore_patterns('.gradle'))
print('Copied full export, including IL2CPP toolchain, in',round(time.monotonic()-started,1),'seconds',flush=True)
subprocess.run(['python',str(root/'build_ssd_gradle.py'),'--project',str(dest),'--label','phone-guard-android-gradle'],check=True)
