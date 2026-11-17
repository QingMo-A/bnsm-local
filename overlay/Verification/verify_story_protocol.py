"""Bind story and treasure regressions to the current Windows and portable cores."""
import hashlib
import json
import re
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
verification = root / "Verification"
results = {}
story_checks = set()
for platform, project, core in (
    ("windows", "BotWorldTests", "Bnsm.ServerCore.dll"),
    ("android", "EmbeddedServerTests", "Bnsm.EmbeddedServer.dll"),
):
    binaries = verification / project / "bin/Release/net10.0"
    for case, marker in (("story", "STORY_CHAPTERS_PASSED"), ("gacha", "PASS: 96 mixed gacha")):
        process = subprocess.run(
            ["dotnet", str(binaries / "Bnsm.LocalServer.dll"), f"--{case}-test"],
            cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120,
        )
        log = verification / f"story-{platform}-{case}.log"
        log.write_bytes(process.stdout)
        if process.returncode or marker.encode() not in process.stdout:
            raise RuntimeError(f"{platform} {case} regression failed. See {log}")
        if case == "story":
            count = int(re.search(rb"STORY_CHAPTERS_PASSED (\d+)", process.stdout)[1])
            assert count >= 49
            story_checks.add(count)
    results[platform] = hashlib.sha256((binaries / core).read_bytes()).hexdigest()
    print(f"{platform}: {count} story and 96 gacha checks passed", flush=True)

assert len(story_checks) == 1
(verification / "story-protocol-latest.json").write_text(
    json.dumps({"passed": True, "storyChecks": story_checks.pop(), "gachaChecks": 96, "coreSha256": results}, indent=2), encoding="utf-8",
)
