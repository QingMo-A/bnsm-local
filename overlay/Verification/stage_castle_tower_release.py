from pathlib import Path
import shutil
r=Path(__file__).resolve().parents[1]
for relative,a,b in [('ServerBuildSource/AndroidHost/Bnsm.AndroidHost.csproj','<ApplicationVersion>22<','<ApplicationVersion>23<'),('ServerBuildSource/AndroidHost/Bnsm.AndroidHost.csproj','0.22-activities','0.23-castle-tower'),('Project/Assets/Editor/LocalCompactBuild.cs','Math.Max(22,PlayerSettings.Android.bundleVersionCode)','Math.Max(23,PlayerSettings.Android.bundleVersionCode)')]:
 p=r/relative;s=p.read_text(encoding='utf-8-sig');p.write_text(s.replace(a,b),encoding='utf-8')
stage=r/'Builds/.staging/Windows';stage.mkdir(parents=True,exist_ok=True)
for name in ['Start-LocalGame.cmd','Start-RecoveryGame.ps1','Start-LocalGame.ps1','LocalRuntime.ps1','LocalWorkspace.json','bnsm-local-server.json','Start-SaveManager.cmd','SaveManager.ps1','Open-ServerLogs.cmd','Manage-LocalBots.cmd','Manage-LocalBots.py','README.txt']:shutil.copy2(r/'WindowsPreview'/name,stage/name)
for folder in ['UnityRebuilt/WindowsBuild-Latest','LocalServer/data']:shutil.copytree(r/'WindowsPreview'/folder,stage/folder,dirs_exist_ok=True)
print('Windows stage prepared.')
