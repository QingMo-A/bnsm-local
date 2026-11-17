import ctypes,pathlib,subprocess,sys,time
root=pathlib.Path('I:/BnsmModernWork');pid=int(sys.argv[1]);kernel=ctypes.windll.kernel32
kernel.OpenProcess.restype=ctypes.c_void_p;kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
handle=kernel.OpenProcess(0x100000,False,pid)
if handle:
    try:
        while kernel.WaitForSingleObject(handle,0)==258:time.sleep(3)
    finally:kernel.CloseHandle(handle)
log=(root/'Verification/document-android-build.log').read_text(encoding='utf-8-sig',errors='replace')
if 'Application will terminate with return code 0' not in log:raise RuntimeError('Android export failed; inspect document-android-build.log')
with (root/'Verification/document-android-gradle.log').open('w',encoding='utf-8') as out:
    result=subprocess.run([sys.executable,str(root/'build_ssd_gradle.py'),'--project','C:/BnsmModernBuildCache/MobileLatest/Android-ARM64/GradleExport','--label','document-v13'],cwd=root,stdout=out,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
print('Android Gradle',result.returncode,flush=True);raise SystemExit(result.returncode)
