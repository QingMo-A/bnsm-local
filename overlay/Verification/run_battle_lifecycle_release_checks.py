from pathlib import Path
import subprocess,sys
root=Path('I:/BnsmModernWork')
checks=[['Verification/verify_big_boss_native.py','2635101'],['Verification/verify_big_boss_native.py','2637101'],['Verification/verify_stage_preview.py'],['Verification/verify_resource_missions_native.py'],['Verification/verify_windows_launcher_latest.py']]
for args in checks:
 name=Path(args[0]).stem+('-'+args[1] if len(args)>1 else '')
 log=root/('Verification/battle-lifecycle-check-'+name+'.log')
 with log.open('w',encoding='utf-8') as out:
  result=subprocess.run([sys.executable,*args],cwd=root,stdout=out,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
 print(name,'PASS' if result.returncode==0 else 'FAIL',flush=True)
 if result.returncode:print(log,flush=True);raise SystemExit(result.returncode)
