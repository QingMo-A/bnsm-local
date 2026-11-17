import pathlib,subprocess,time
root=pathlib.Path('I:/BnsmModernWork');log=root/'Verification/competitive-unity-android.log'
while 'Application will terminate with return code' not in log.read_text(encoding='utf-8-sig',errors='replace'):time.sleep(2)
assert 'return code 0' in log.read_text(encoding='utf-8-sig',errors='replace'),'Android export failed'
subprocess.run(['python','build_ssd_gradle.py','--project','C:/BnsmModernBuildCache/MobileLatest/Android-ARM64/GradleExport','--label','competitive-android-gradle'],cwd=root,check=True)
