"""Retain the verified v27 client and prepare the v28 shared server release."""
from pathlib import Path
import shutil
root=Path(__file__).resolve().parents[1];stage=root/'Builds/.staging/Windows'
for name in ['Start-LocalGame.cmd','Start-RecoveryGame.ps1','Start-LocalGame.ps1','LocalRuntime.ps1','LocalWorkspace.json','bnsm-local-server.json','Start-SaveManager.cmd','SaveManager.ps1','Open-ServerLogs.cmd','Manage-LocalBots.cmd','Manage-LocalBots.py','README.txt']:
    (stage/name).parent.mkdir(parents=True,exist_ok=True);shutil.copy2(root/'WindowsPreview'/name,stage/name)
for relative in ['UnityRebuilt/WindowsBuild-Latest','LocalServer/data']:
    shutil.copytree(root/'WindowsPreview'/relative,stage/relative,dirs_exist_ok=True)
print('Verified v27 client and current content staged for v28 server.')
