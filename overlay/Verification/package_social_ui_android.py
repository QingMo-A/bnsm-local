from pathlib import Path
import json,shutil,subprocess,time

root=Path('I:/BnsmModernWork')
source=root/'Builds/Android-SocialUi/GradleExport'
destination=Path('C:/BnsmModernBuildCache/SocialUiGradle')
report=json.loads((source.parent/'build-result.json').read_text(encoding='utf-8-sig'))
assert report['result']=='Succeeded' and report['errors']==0
assert source.is_dir() and not destination.exists()
started=time.monotonic()
shutil.copytree(source,destination,ignore=shutil.ignore_patterns('build','.gradle'))
print('Android export copied to SSD in %.1fs'%(time.monotonic()-started),flush=True)
subprocess.run(['python',str(root/'build_ssd_gradle.py'),'--project',str(destination),'--label','social-ui-android-gradle'],check=True)
