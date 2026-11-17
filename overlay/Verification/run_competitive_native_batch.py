import pathlib,subprocess,time
root=pathlib.Path('I:/BnsmModernWork')
log=root/'Verification/competitive-unity-windows-5.log'
while 'Application will terminate with return code' not in log.read_text(encoding='utf-8-sig',errors='replace'):time.sleep(1)
assert 'return code 0' in log.read_text(encoding='utf-8-sig',errors='replace')
for mode in ['rank','balance','castle','world','infinite']:
 print('START '+mode,flush=True)
 p=subprocess.run(['python','Verification/verify_competitive_native.py',mode],cwd=root)
 print('END '+mode+' code='+str(p.returncode),flush=True)
