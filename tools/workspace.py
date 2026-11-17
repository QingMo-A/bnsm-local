"""Materialize the public overlay using an existing local development baseline."""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
LOCAL = REPO / "LocalBuild"
OVERLAY = REPO / "overlay"
REQUIRED = [
    "Project/Assets/BSC/Scenes/Game.unity",
    "Project/Assets/Scripts/Assembly-CSharp/BscBattle/BattleStage.cs",
    "Project/Packages/manifest.json",
    "Project/ProjectSettings/ProjectVersion.txt",
    "ServerBuildSource/build/Assembly-CSharp.dll",
    "ServerBuildSource/references/Managed/UnityEngine.dll",
    "ServerBuildSource/references/Managed/protobuf-net.dll",
    "WindowsPreview/LocalServer/data/tables/BattleAi.pb",
    "WindowsPreview/LocalServer/data/packet-keys.json",
]
LOCAL_INPUTS = (
    "Project/Assets", "Project/Packages", "Project/ProjectSettings",
    "ServerBuildSource/build", "ServerBuildSource/references/Managed",
    "WindowsPreview/LocalServer/data", "AndroidGameIcon",
)
PRIVATE_NAMES = {"players.json", "bot-world.json", ".git", ".env", "saves", "profiles", "backups"}


def digest(content):
    return hashlib.sha256(content).hexdigest()


def file_path(root, relative):
    if any(part in ("", ".", "..") or ":" in part or "\\" in part for part in relative.split("/")):
        raise ValueError(f"Invalid relative path: {relative}")
    return root.joinpath(*relative.split("/"))


def text_bytes(path):
    return path.read_bytes().replace(b"\r\n", b"\n")


def ignored(directory, entries):
    return [entry for entry in entries if entry.casefold() in PRIVATE_NAMES or
            entry.casefold().endswith((".bnsm-save.zip", ".jks", ".keystore", ".pfx", ".p12"))]


def apply_patches(project, rows):
    for row in rows:
        relative = row["path"]
        target = file_path(project, relative)
        if row["after"] is None and not target.exists():
            continue
        if not target.is_file():
            raise ValueError(f"Baseline missing patched file: {relative}")
        original = text_bytes(target)
        checksum = digest(original)
        if checksum == row["after"]:
            continue
        if checksum != row["before"]:
            raise ValueError(f"Unsupported baseline version for {relative}")
        if row["after"] is None:
            target.unlink()
            continue
        lines = original.decode("utf-8").splitlines(keepends=True)
        for offset, count, replacement in reversed(row["hunks"]):
            lines[offset:offset + count] = replacement
        patched = "".join(lines).encode("utf-8")
        if digest(patched) != row["after"]:
            raise ValueError(f"Patch output failed checksum: {relative}")
        target.write_bytes(patched)


def hydrate(args):
    workspace = args.workspace.resolve()
    dest = args.output.resolve()
    if LOCAL.resolve() not in dest.parents or dest.exists():
        raise ValueError("Choose a new output directory under LocalBuild.")
    if workspace == dest or workspace in dest.parents or dest in workspace.parents:
        raise ValueError("The input workspace and destination must be separate.")
    missing = [name for name in REQUIRED if not file_path(workspace, name).is_file()]
    if missing:
        raise ValueError("Local inputs missing:\n  " + "\n  ".join(missing))
    dest.parent.mkdir(parents=True, exist_ok=True)
    data = json.loads((REPO / "patches/client.json").read_text(encoding="utf-8"))
    required_generated = data["localGenerated"]
    missing = [name for name in required_generated if not file_path(workspace, name).is_file() and
               (args.local_extras is None or not file_path(args.local_extras, name).is_file())]
    if missing:
        raise ValueError("This baseline also needs the following local outputs (pass --local-extras):\n  " +
                         "\n  ".join(missing))

    with tempfile.TemporaryDirectory(prefix=".prepare-", dir=dest.parent) as temp:
        staging = Path(temp) / "workspace"
        for name in LOCAL_INPUTS:
            source = file_path(workspace, name)
            if source.is_dir():
                shutil.copytree(source, file_path(staging, name), ignore=ignored)
        for name in required_generated:
            target = file_path(staging, name)
            source = file_path(args.local_extras, name) if args.local_extras else None
            if source is None or not source.is_file():
                source = file_path(workspace, name)
                if target.is_file():
                    continue
            if source.is_file():
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
        apply_patches(staging, data["files"])
        shutil.copytree(OVERLAY, staging, dirs_exist_ok=True)
        (staging / "local-import.json").write_text(json.dumps({
            "sourceCompatibility": data["baseline"],
            "patchedFiles": len(data["files"]),
            "localGenerated": len(required_generated),
        }, indent=2) + "\n", encoding="utf-8")
        staging.rename(dest)
    print(dest)


def build_server(args):
    workspace = args.workspace.resolve()
    if not (workspace / "local-import.json").is_file():
        raise ValueError("Run hydrate before building the server.")
    subprocess.run(["dotnet", "build", str(workspace / "ServerBuildSource/ServerCore/Bnsm.ServerCore.csproj"),
                    "--configuration", "Release", "--nologo"], check=True, cwd=workspace)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("hydrate")
    setup.add_argument("--workspace", required=True, type=Path, help="Existing local developer workspace")
    setup.add_argument("--local-extras", type=Path, help="Optional root containing missing generated paths")
    setup.add_argument("--output", type=Path, default=LOCAL / "workspace")
    setup.set_defaults(run=hydrate)
    build = commands.add_parser("build-server")
    build.add_argument("--workspace", type=Path, default=LOCAL / "workspace")
    build.set_defaults(run=build_server)
    args = parser.parse_args()
    try:
        args.run(args)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"Workspace setup failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
