from pathlib import Path
import json,os,subprocess,sys,time
root=Path(__file__).resolve().parents[1]
result=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/build-result.json'
until=time.monotonic()+1800
selected_log=next((x.split('=',1)[1] for x in sys.argv if x.startswith('--build-log=')),None)
if '--final-scripts' in sys.argv or selected_log:
    final_log=root/(selected_log or 'Verification/original-card-windows-final-scripts.log')
    while True:
        text=final_log.read_text('utf-8-sig',errors='replace') if final_log.exists() else ''
        if 'Exiting batchmode successfully' in text:break
        if 'Application will terminate with return code 1' in text:raise RuntimeError('Final scripts build failed')
        if time.monotonic()>until:raise RuntimeError('Final scripts build timed out')
        time.sleep(3)
while not result.exists():
    if time.monotonic()>until:raise RuntimeError('Original-card Unity build timed out')
    log=(root/'Verification/original-card-windows-build.log').read_text('utf-8-sig',errors='replace')
    if 'Application will terminate with return code 1' in log or 'executeMethod method' in log:raise RuntimeError('Unity build failed; inspect original-card-windows-build.log')
    time.sleep(3)
proof=json.loads(result.read_text('utf-8-sig'));assert proof['result']=='Succeeded' and proof['errors']==0,proof
assembly=root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll'
assert '-bnsm-original-card-preview'.encode('utf-16-le') in assembly.read_bytes(),'Rebuild the final preview entry point'
env=os.environ.copy();env['PATH']='C:/Program Files/dotnet;'+env.get('PATH','')
commands=[['C:/Program Files/dotnet/dotnet.exe','build','Verification/BotWorldTests/BotWorldTests.csproj','-c','Release','--nologo','-v','quiet'],
    ['C:/Program Files/dotnet/dotnet.exe','Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll','--original-card-test'],
    [sys.executable,'Verification/verify_original_card_native.py']]
for command in commands:
    print('START '+command[-1],flush=True)
    run=subprocess.run(command,cwd=root,env=env,creationflags=subprocess.CREATE_NO_WINDOW,timeout=480)
    if run.returncode:raise SystemExit(run.returncode)
print('Original card native and server checks passed.',flush=True)
