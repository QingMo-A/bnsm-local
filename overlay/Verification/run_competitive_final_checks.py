import pathlib,subprocess,time
root=pathlib.Path('I:/BnsmModernWork');log=root/'Verification/competitive-unity-windows-8.log'
while not log.exists():time.sleep(.3)
while 'Application will terminate with return code' not in log.read_text(encoding='utf-8-sig',errors='replace'):time.sleep(1)
assert 'return code 0' in log.read_text(encoding='utf-8-sig',errors='replace')
for mode in ['castle','arena','rank','balance','world','infinite']:
 print('START '+mode,flush=True)
 subprocess.run(['python','Verification/verify_competitive_native.py',mode],cwd=root,check=True)
for script in ['verify_windows_full_native.py','verify_windows_full_host.py','verify_windows_launcher_latest.py']:
 print('START '+script,flush=True)
 subprocess.run(['python','Verification/'+script],cwd=root,check=True)
