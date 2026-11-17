"""Verify Home checkpoint, then an unclean stop after that checkpoint completes.

No login inputs, battles, profile changes or bot edits are submitted.
"""
from pathlib import Path
import argparse, datetime, json, re, subprocess, time
ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--serial',required=True);args=parser.parse_args()
def adb(*parts,check=True):
    return subprocess.run(['C:/Android/platform-tools/adb.exe','-s',args.serial,*parts],capture_output=True,
        text=True,encoding='utf-8',errors='replace',check=check).stdout.strip()
def pid():return adb('shell','pidof','local.bnsm.integratedtest',check=False)
def state():
    return json.loads(adb('shell','cat','/sdcard/Android/data/local.bnsm.integratedtest/files/Diagnostics/embedded-state.json'))
game=json.loads((ROOT/'Verification/android-integrated-phone/apk-inspection.json').read_text())
path=adb('shell','pm','path','local.bnsm.integratedtest').removeprefix('package:')
assert re.fullmatch(r'/data/app/[A-Za-z0-9_~=./-]+/base\.apk',path)
assert adb('shell','sha256sum',path).split()[0]==game['sha256']
assert not pid(),'Game is already running; do not interrupt a player session'
print('Opening the actual game; no input or profile changes',flush=True)
adb('shell','am','start','-W','-n','local.bnsm.integratedtest/com.unity3d.player.UnityPlayerActivity')
deadline=time.monotonic()+120
while True:
    try:
        before=state()
        if before['ready'] and str(before['processId'])==pid():break
    except (ValueError,KeyError):pass
    assert time.monotonic()<deadline,'Host did not become ready; left app intact for diagnosis'
    time.sleep(1)
print('Host ready; sending Home to check automatic save',flush=True)
adb('shell','input','keyevent','3')
deadline=time.monotonic()+30
while True:
    after=state()
    assert after['instanceId']==before['instanceId'] and after['ready'],'Host restarted or stopped on Home'
    if after.get('lastCheckpointUtc') and after.get('lastCheckpointUtc')!=before.get('lastCheckpointUtc'):break
    assert time.monotonic()<deadline,'Home checkpoint did not complete; left app intact for diagnosis'
    time.sleep(1)
assert pid()==str(before['processId'])
print('Home checkpoint completed; stopping only this test game process',flush=True)
# Deliberately after the confirmed durable checkpoint. This tests force-stop
# fallback, not Android's onTaskRemoved callback or an in-progress battle.
adb('shell','am','force-stop','local.bnsm.integratedtest')
assert not pid()
proof=dict(passed=True,gameSha256=game['sha256'],homeCheckpointPassed=True,
    checkpointUtc=after['lastCheckpointUtc'],checkpointMilliseconds=after['lastCheckpointMilliseconds'],
    sameInstanceInBackground=True,hostReadyWhileBackground=True,
    forceStopAfterCompletedCheckpoint=True,taskRemovalCallbackDeviceVerified=False,
    scope='Actual full game startup and Home checkpoint; force-stop only after completion; no battle, profile or bot changes',
    checkedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
(ROOT/'Verification/android-manager-pause-device-result.json').write_text(json.dumps(proof,indent=2),'utf-8')
print(json.dumps(proof,indent=2),flush=True)
