"""Launch a Windows story sample with an isolated local game account."""
import json
import msvcrt
import os
import subprocess
import sys
import time
from contextlib import contextmanager
from pathlib import Path
from tempfile import mkdtemp


@contextmanager
def preview_lease(workspace):
    with (workspace / "preview.lock").open("a+b") as lock:
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as error:
            raise SystemExit("This story preview is already open.") from error
        yield


def main():
    verify = "--verify" in sys.argv
    maknae = "--maknae" in sys.argv
    hong = "--hong" in sys.argv
    root = Path(__file__).resolve().parents[1]
    build = root / "Builds/.staging/Chapter53-Windows"
    game = build / "BnsmLocal.exe"
    fixture = root / "Verification/BotWorldTests/bin/Release/net10.0/Bnsm.LocalServer.dll"
    if not game.is_file() or not fixture.is_file():
        raise SystemExit("The story preview build or server fixture is missing.")

    preview = "hong" if hong else "maknae-act" if maknae else "birch-route"
    prefix = {"hong": "hong-preview-", "maknae-act": "maknae-act-preview-", "birch-route": "chapter53-preview-"}[preview]
    if (maknae or hong) and not verify:
        workspace = root / "Verification" / f"{prefix}player"
        workspace.mkdir(exist_ok=True)
    else:
        workspace = Path(mkdtemp(prefix=prefix, dir=root / "Verification"))
    print(f"Preview save and logs: {workspace}", flush=True)
    story_preview = maknae or hong
    env = dict(os.environ, BNSM_CHAPTER53_PREVIEW="0" if story_preview else "1",
               BNSM_MAKNAE_ACT_ONE_PREVIEW="1" if story_preview else "0",
               BNSM_HONG_PREVIEW="1" if story_preview else "0",
               BNSM_STORY_PREVIEW_FOCUS="hong" if hong else "maknae")
    endpoints = workspace / "endpoints.json"
    with preview_lease(workspace), (workspace / "server.log").open("w", encoding="utf-8") as log:
        endpoints.unlink(missing_ok=True)
        server = subprocess.Popen(
            ["dotnet", str(fixture), "--story-fixture", str(workspace)],
            cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        try:
            deadline = time.monotonic() + 40
            while not endpoints.exists():
                if server.poll() is not None or time.monotonic() >= deadline:
                    raise RuntimeError(f"Preview server did not start. See {log.name}.")
                time.sleep(.2)
            port = json.loads(endpoints.read_text(encoding="utf-8"))["directoryPort"]
            args = [str(game), f"-bnsm-test-directory-port={port}", "-screen-fullscreen", "0",
                    "-screen-width", "1280", "-screen-height", "720", "-logFile", str(workspace / "client.log")]
            if verify:
                args += ["-bnsm-probe", "-bnsm-autoconnect", f"-bnsm-{preview}-test",
                         f"-bnsm-{preview}-output={workspace}"]
            with subprocess.Popen(args, cwd=build, env=env) as client:
                try:
                    code = client.wait(timeout=800 if verify else None)
                except subprocess.TimeoutExpired:
                    client.terminate()
                    client.wait(timeout=10)
                    raise
                if verify:
                    report = workspace / f"{preview}.json"
                    if not report.is_file():
                        raise RuntimeError(f"Native {preview} did not finish. See {workspace / 'client.log'}.")
                    result = json.loads(report.read_text(encoding="utf-8-sig"))
                    player = json.loads((workspace / "saves/players.json").read_text(encoding="utf-8-sig"))[0]
                    if hong:
                        persisted = set(range(8641, 8653)).union(range(8661, 8667)).issubset(player["StoryClears"]) and \
                            player["CardIds"].count(1900040001) == 1 and player["ClearStage"] == 179 and \
                            player["ClearEpisodeCutScene"] == (179 << 8) + 1
                    elif maknae:
                        bosses = {"8625": 1, "8630": 2, "8636": 3}
                        fragments = 18 + sum(bosses.values()) + sum(count for stage, count in bosses.items()
                                                                  if player["StoryStars"].get(stage) == 3)
                        persisted = set(range(8621, 8637)).issubset(player["StoryClears"]) and \
                            all(player["StorySweeps"].get(stage) == 1 for stage in bosses) and \
                            player["Inventory"].get("205081313") == fragments and player["CardIds"].count(30181) == 1
                    else:
                        persisted = player["ClearStage"] == 185 and player["ClearEpisodeCutScene"] == (185 << 8) + 1
                    result["persistedProgress"] = persisted and player["ActiveStage"] is None
                    print(json.dumps(result, ensure_ascii=False), flush=True)
                    if not result["passed"] or not result["persistedProgress"]:
                        raise RuntimeError(f"Native {preview} check failed.")
                if code:
                    raise RuntimeError(f"Preview game exited with error. See {workspace / 'client.log'}.")
        finally:
            if server.poll() is None:
                server.terminate()
                server.wait(timeout=10)


if __name__ == "__main__":
    main()
