from pathlib import Path
import subprocess,sys
root=Path('I:/BnsmModernWork')
checks=[['Verification/verify_viewport_native.py'],['Verification/verify_windows_full_native.py']]
checks += [['Verification/verify_competitive_native.py',mode] for mode in ['arena','rank','balance','castle','world','infinite']]
checks += [['Verification/verify_battle_lifecycle_native.py'],['Verification/verify_big_boss_native.py','2635101'],['Verification/verify_big_boss_native.py','2637101'],['Verification/verify_stage_preview.py'],['Verification/verify_resource_missions_native.py'],['Verification/verify_windows_full_host.py'],['Verification/verify_windows_launcher_latest.py']]
for args in checks:
    name=Path(args[0]).stem+('-'+args[1] if len(args)>1 else '')
    log=root/('Verification/document-check-'+name+'.log')
    with log.open('w',encoding='utf-8') as out:
        run=subprocess.run([sys.executable,*args],cwd=root,stdout=out,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
    print(name,'PASS' if run.returncode==0 else 'FAIL',flush=True)
    if run.returncode:print(log,flush=True);raise SystemExit(run.returncode)
