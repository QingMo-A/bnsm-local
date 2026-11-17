"""Sequential native player regressions: the game intentionally uses one instance."""
import hashlib, json, os, pathlib, subprocess, sys, time
root=pathlib.Path(__file__).resolve().parents[1]
commands=[
    ['verify_infinite_defence_native.py'],
    ['verify_manual_pvp_native.py','rank'],
    ['verify_manual_pvp_native.py','balance'],
    ['verify_skin_shop_native.py'],
    ['verify_document_ui.py','--final'],
    ['verify_viewport_native.py','skin-shop'],
    ['verify_windows_full_native.py'],
    ['verify_battle_lifecycle_native.py'],
    ['verify_castle_lifecycle_native.py'],
]+[['verify_competitive_native.py',mode] for mode in ['arena','rank','balance','castle','world','infinite']]+[
    ['verify_windows_full_host.py'],
    ['verify_windows_launcher_latest.py'],
]
assembly=hashlib.sha256((root/'Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()
reports={'verify_infinite_defence_native.py':'infinite-defence-native-latest.json','verify_skin_shop_native.py':'skin-shop-native-latest.json','verify_document_ui.py':'document-ui-latest.json','verify_viewport_native.py':'viewport-latest.json','verify_windows_full_native.py':'windows-full-native-latest.json','verify_battle_lifecycle_native.py':'battle-lifecycle-native-latest.json','verify_castle_lifecycle_native.py':'castle-lifecycle-native-latest.json','verify_windows_launcher_latest.py':'windows-full-launcher-latest.json'}
for args in commands:
    started=time.monotonic();name='-'.join(x.removesuffix('.py').removeprefix('verify_').lstrip('-') for x in args)
    report=reports.get(args[0])
    if args[0]=='verify_manual_pvp_native.py':report='manual-pvp-native-'+args[1]+'-latest.json'
    if args[0]=='verify_competitive_native.py':report='competitive-native-'+args[1]+'-latest.json'
    if '--resume' in sys.argv and report and (root/'Verification'/report).exists():
        proof=json.loads((root/'Verification'/report).read_text('utf-8-sig'))
        if proof.get('passed') and proof.get('clientAssemblySha256')==assembly and (args[0]!='verify_document_ui.py' or len(proof['pages'])>=35):
            print('PASS current binary already verified: '+name,flush=True);continue
    print('START '+name,flush=True)
    with (root/'Verification'/('skin-shop-check-'+name+'.log')).open('w',encoding='utf-8') as log:
        result=subprocess.run([sys.executable,str(root/'Verification'/args[0])]+args[1:],cwd=root,stdout=log,stderr=subprocess.STDOUT,timeout=450,creationflags=subprocess.CREATE_NO_WINDOW)
    print(json.dumps(dict(check=name,exitCode=result.returncode,seconds=round(time.monotonic()-started,1))),flush=True)
    if result.returncode:raise SystemExit(result.returncode)
print('All required native release checks passed.',flush=True)
