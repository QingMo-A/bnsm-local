"""Run the actual packaged launcher using a fresh isolated save and retain evidence."""
import datetime,hashlib,json,pathlib,re,shutil,subprocess
root=pathlib.Path('I:/BnsmModernWork');stage=root/'Builds/.staging/Windows'
folder=root/'Verification'/('windows-launcher-viewport-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));folder.mkdir()
with (folder/'launcher.log').open('wb') as log:
    run=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(stage/'Start-LocalGame.ps1'),'-HeadlessTest','-SaveRoot',str(folder/'saves')],cwd=stage,stdout=log,stderr=subprocess.STDOUT,timeout=130,creationflags=subprocess.CREATE_NO_WINDOW)
timeline=(stage/'evidence/client-probe-headless/timeline.txt').read_text(encoding='utf-8-sig')
errors=[int(n) for n in re.findall(r'errors=(\d+)',timeline)]
reached='seconds=60; scene=Game;' in timeline
config=json.loads((stage/'LocalWorkspace.json').read_text(encoding='utf-8-sig'))
relative=not pathlib.Path(config['clientPath']).is_absolute() and not pathlib.Path(config['serverPath']).is_absolute()
shutil.copytree(stage/'evidence',folder/'evidence')
result=dict(passed=run.returncode==0 and reached and bool(errors) and max(errors)==0 and relative,exitCode=run.returncode,loginReachedGame=reached,errors=max(errors) if errors else -1,durationSeconds=60,fixture=str(folder/'saves'),evidence=str(folder/'evidence'),normalSaveUsed=False,relativeWorkspacePaths=relative,clientAssemblySha256=hashlib.sha256((stage/'UnityRebuilt/WindowsBuild-Latest/BnsmLocal_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),verifiedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
(root/'Verification/windows-full-launcher-latest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2));raise SystemExit(0 if result['passed'] else 1)
