"""Run the native PC story entry, three portraits and a real chapter victory."""
import sys
import datetime
import hashlib
import json
import subprocess
import time
from pathlib import Path
from PIL import Image, ImageChops

root = Path(__file__).resolve().parents[1]
maknae = "--maknae" in sys.argv
hong_growth = "--hong-growth" in sys.argv
closed = "--closed" in sys.argv
label = "story-closed-native" if closed else "hong-growth-native" if hong_growth else "maknae-native" if maknae else "story-native"
folder = root / "Verification" / (label + "-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S"))
folder.mkdir()
fixture_dll = root / "Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll"
game = root / "Builds/.staging/Windows/UnityRebuilt/WindowsBuild-Latest"


def wait_file(path, process, timeout):
    deadline = time.monotonic() + timeout
    while not path.exists():
        if process.poll() is not None or time.monotonic() >= deadline:
            raise RuntimeError(f"Timed out or process exited while waiting for {path}")
        time.sleep(.25)
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


client = None
with (folder / "fixture.log").open("w", encoding="utf-8") as fixture_log:
    fixture = subprocess.Popen(
        ["dotnet", str(fixture_dll), "--story-fixture", str(folder)],
        cwd=root, stdout=fixture_log, stderr=subprocess.STDOUT,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    try:
        port = wait_file(folder / "endpoints.json", fixture, 40)["directoryPort"]
        client = subprocess.Popen(
            [str(game / "BnsmLocal.exe"), "-bnsm-probe", "-bnsm-autoconnect", "-bnsm-story-test",
             *(["-bnsm-maknae-native"] if maknae else []), f"-bnsm-test-directory-port={port}", f"-bnsm-competitive-output={folder}",
             *(["-bnsm-hong-growth-native"] if hong_growth else []),
             *(["-bnsm-story-closed-test"] if closed else []),
             "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
             "-logFile", str(folder / "client.log")],
            cwd=game, creationflags=subprocess.CREATE_NO_WINDOW,
        )
        report = wait_file(folder / "story-native.json", client, 500 if maknae else 320)
        client.wait(timeout=20)
        if not closed and not maknae and not hong_growth:
            with Image.open(folder / "hong-rank-1.png") as before, Image.open(folder / "hong-rank-1-rain.png") as after:
                width, height = before.size
                art = (int(width * .41), int(height * .18), int(width * .59), int(height * .73))
                difference = ImageChops.difference(before.crop(art).convert("RGB"), after.crop(art).convert("RGB")).convert("L")
                backdrop = (int(width * .27), int(height * .18), int(width * .35), int(height * .73))
                still = ImageChops.difference(before.crop(backdrop).convert("RGB"), after.crop(backdrop).convert("RGB"))
                report["checks"]["rainMovesBetweenFrames"] = still.getbbox() is None and sum(difference.histogram()[4:]) > difference.width * difference.height * .005
        player = json.loads((folder / "saves/players.json").read_text(encoding="utf-8-sig"))[0]
        if closed:
            report["storyProgressUntouched"] = not player.get("StoryClears") and not player.get("StorySweeps")
            report["passed"] &= report["storyProgressUntouched"]
        elif hong_growth:
            seq = str(player["CardIds"].index(1900040016) + 1)
            card = player["CardStates"][seq]
            report["hongGrowthSaved"] = card["SkillEnchants"][:6] == [1] * 6 and len(card["TranscendenceEffects"]) == 3
            report["passed"] &= report["hongGrowthSaved"]
        else:
            report["savedChapterAndCard"] = 8601 in player["StoryClears"] and 1900040001 in player["CardIds"]
            report["passed"] &= report["savedChapterAndCard"]
        if maknae:
            report["maknaeNativeSaved"] = (
                all(stage in player["StoryClears"] for stage in (8613, 8616, 8619))
                and 30181 in player["CardIds"]
                and player.get("StorySweeps", {}).get("8619") == 1
            )
            report["passed"] &= report["maknaeNativeSaved"]
        report["activeStageCleared"] = player["ActiveStage"] is None
        report["passed"] &= report["activeStageCleared"] and client.returncode == 0
        if not closed and not maknae and not hong_growth:
            report["passed"] &= report["checks"]["rainMovesBetweenFrames"]
        report["clientAssemblySha256"] = sha(game / "BnsmLocal_Data/Managed/Assembly-CSharp.dll")
        report["serverCoreSha256"] = sha(fixture_dll.with_name("Bnsm.ServerCore.dll"))
        report["folder"] = str(folder)
        (root / "Verification" / (label + "-latest.json")).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(report, ensure_ascii=False), flush=True)
        if not report["passed"]:
            raise RuntimeError("Native story chapter failed")
    finally:
        for process in [client, fixture]:
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    process.kill()
