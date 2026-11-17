"""Versioned isolated server and native checks for the ranked loadout update."""
from pathlib import Path
import os, subprocess, sys, time
root=Path(__file__).resolve().parents[1]
env=os.environ.copy();env['PATH']='C:/Program Files/dotnet;'+env.get('PATH','')
server='--server' in sys.argv
if server:
    commands=[['C:/Program Files/dotnet/dotnet.exe',str(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),flag] for flag in ['--pvp-difficulty-test','--rank-deck-test','--competitive-test','--castle-lifecycle-test','--battle-lifecycle-test','--manual-pvp-test','--document-test']]
else:
    commands=[[sys.executable,str(root/'Verification'/script),*args] for script,args in [
        *[('verify_competitive_native.py',[mode]) for mode in ['arena','rank','balance','castle','world','infinite']],
        ('verify_competitive_native.py',['arena','--ladder-rank=30']),
        ('verify_competitive_native.py',['infinite','--ladder-rank=100']),
        ('verify_manual_pvp_native.py',['rank']),('verify_manual_pvp_native.py',['balance']),
        ('verify_battle_lifecycle_native.py',[]),('verify_castle_lifecycle_native.py',[]),
        ('verify_windows_full_native.py',[]),('verify_windows_full_host.py',[]),('verify_windows_launcher_latest.py',[])]]
for command in commands:
    name='-'.join(Path(part).stem.lstrip('-').replace('=','-') for part in (command[2:] if server else command[1:]))
    log=root/'Verification'/('pvp-ladder-check-'+name+'.log')
    print('START '+name,flush=True);started=time.monotonic()
    with log.open('w',encoding='utf-8') as output:
        result=subprocess.run(command,cwd=root,env=env,stdout=output,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW,timeout=600)
    print(name+' exit='+str(result.returncode)+' seconds='+str(round(time.monotonic()-started,1)),flush=True)
    if result.returncode:raise SystemExit(result.returncode)
print('All '+('server' if server else 'native')+' ladder checks passed.',flush=True)
