from pathlib import Path
import hashlib, shutil
root=Path(__file__).resolve().parents[1]
source=root/'ServerBuildSource/AndroidHost/bin/Release/net10.0-android36.0/android-arm64'
target=root/'Builds/PhoneServer-PvpLadder/apk'
for item in source.iterdir():
    if item.is_file() and ((item.name.startswith('Bnsm.') and item.suffix in ['.dll','.pdb','.json','.xml']) or item.name in ['Assembly-CSharp.dll','protobuf-net.dll','UnityEngine.dll']):shutil.copy2(item,target/item.name)
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
core=digest(target/'Bnsm.ServerCore.dll')
assert core==digest(root/'Builds/.staging/Windows/LocalServer/WindowsLatest/Bnsm.ServerCore.dll')==digest(root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll')
print('Matching tested Windows/Android core: '+core)
