"""Exercise unchanged native clients against the new shared PvP difficulty core."""
from pathlib import Path
import os
import subprocess
import sys

root=Path(__file__).resolve().parents[1]
env=os.environ.copy()
env['PATH']='C:/Program Files/dotnet;'+env.get('PATH','')
checks=[['Verification/verify_competitive_native.py',mode] for mode in ['arena','rank','balance','castle','world','infinite']]
if '--world-already-verified' in sys.argv:
    # The native world victory is verified separately before this continuation.
    import hashlib,json
    proof=json.loads((root/'Verification/competitive-native-world-latest.json').read_text(encoding='utf-8'))
    assert proof['passed'] and proof['persistedSettlement']
    assert proof['serverCoreSha256']==hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
    checks=[item for item in checks if item[-1]!='world']
checks += [['Verification/verify_castle_lifecycle_native.py'],['Verification/verify_windows_full_host.py'],['Verification/verify_windows_launcher_latest.py']]
for args in checks:
    name=Path(args[0]).stem+('-'+args[1].lstrip('-') if len(args)>1 else '')
    log=root/('Verification/pvp-difficulty-native-'+name+'.log')
    with log.open('w',encoding='utf-8') as output:
        run=subprocess.run([sys.executable,*args],cwd=root,stdout=output,stderr=subprocess.STDOUT,env=env,creationflags=subprocess.CREATE_NO_WINDOW)
    print(name,'PASS' if run.returncode==0 else 'FAIL',flush=True)
    if run.returncode:
        print(log,flush=True)
        raise SystemExit(run.returncode)
