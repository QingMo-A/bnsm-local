"""Bind the portable protocol regressions to the current Android embedded binary."""
from pathlib import Path
import hashlib,json,subprocess,time
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Verification/android-embedded-regressions';OUT.mkdir(exist_ok=True)
CASES=['chain-test','world-war-test','competitive-test','manual-pvp-test','castle-lifecycle-test','castle-invasion-test','castle-monster-test','world-raid-test','rank-deck-test','skill-enchant-test','loot-regression','resource-missions-test','activity-expansion-test','guild-battle-test','guild-bots-test','battle-lifecycle-test']
checks=[]
for name in CASES:
    started=time.monotonic()
    with (OUT/(name+'.log')).open('wb') as log:
        result=subprocess.run(['dotnet',str(ROOT/'Verification/EmbeddedServerTests/bin/Release/net10.0/Bnsm.LocalServer.dll'),'--'+name],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
    checks.append(dict(name=name,exitCode=result.returncode,seconds=round(time.monotonic()-started,2)))
    print(name+': '+str(result.returncode),flush=True)
    if result.returncode:break
passed=len(checks)==len(CASES) and all(x['exitCode']==0 for x in checks)
report=dict(passed=passed,checks=checks,coreSha256=hashlib.sha256((ROOT/'Project/Assets/Plugins/EmbeddedServer/Bnsm.EmbeddedServer.dll').read_bytes()).hexdigest(),fixtureClock='world-war break disabled only in isolated protocol fixture; production clocks untouched')
(OUT/'result.json').write_text(json.dumps(report,indent=2),'utf-8')
raise SystemExit(0 if passed else 1)
