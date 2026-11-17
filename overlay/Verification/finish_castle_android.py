from pathlib import Path
import ctypes,subprocess,sys
root=Path('I:/BnsmModernWork')
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.OpenProcess.restype=ctypes.c_void_p
kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_uint32]
kernel.CloseHandle.argtypes=[ctypes.c_void_p]
handle=kernel.OpenProcess(0x100000,False,int(sys.argv[1]))
if handle:
    while kernel.WaitForSingleObject(handle,30000)==258:pass
    kernel.CloseHandle(handle)
log=(root/'Verification/castle-android-build.log').read_text('utf-8-sig')
assert 'BNSM_MODERN_BUILD_SUCCESS' in log and 'Exiting batchmode successfully now!' in log
subprocess.run([sys.executable,str(root/'build_ssd_gradle.py'),'--project','C:/BnsmModernBuildCache/MobileLatest/Android-ARM64/GradleExport','--label','castle-v15'],cwd=root,check=True)
