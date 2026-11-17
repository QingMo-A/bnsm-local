"""Maintainer tool for exporting reviewed changes from the private source repository."""
import argparse
import difflib
import json
import re
import shutil
import subprocess
from pathlib import Path

from workspace import OVERLAY, REPO, digest, file_path, text_bytes


BASELINE = "defe97bc"
PROJECT = "Project/"
SOURCE_PREFIXES = (
    "Project/Assets/Scripts/Assembly-CSharp/", "Project/Assets/Editor/",
    "Project/Assets/Plugins/Android/",
)
ROOT_FILES = {
    "build_android_save_manager.py", "prepare_embedded_android.py",
}
RUNTIME_CONFIG = {
    "activities.json", "ambient-chat.json", "attendance.json", "bot-life.json",
    "gacha.json", "local-rules.json", "notices.json",
}
STORY_RESOURCES = {
    "LocalStoryArt.meta", "LocalStoryCatalog.json", "LocalStoryCatalog.json.meta",
    "LocalStoryDialogue.json", "LocalStoryDialogue.json.meta",
    "prefabs/field/BG_BattleLocalBirch.prefab", "prefabs/field/BG_BattleLocalBirch.prefab.meta",
    "prefabs/field/BG_FieldLocalBirch.prefab", "prefabs/field/BG_FieldLocalBirch.prefab.meta",
    "shaders/birch battle scenery.shader", "shaders/birch battle scenery.shader.meta",
}


def standalone(path):
    ext = Path(path).suffix
    if path.startswith("ServerBuildSource/"):
        return ext in {".cs", ".csproj", ".xml"} and Path(path).name != "TableExtractor.cs"
    if path.startswith("AndroidSaveManager/"):
        return ext in {".java", ".xml", ".gradle"} and "/build/" not in path
    if path.startswith("WindowsPreview/LocalServer/data/"):
        return path.split("/")[4:] == [] and Path(path).name in RUNTIME_CONFIG
    if path.startswith("Verification/"):
        return ext in {".cs", ".csproj", ".py"}
    if path.startswith("SourceArt/"):
        return path == "SourceArt/原创剧情制作流程.md"
    return path in ROOT_FILES


def authored_project_asset(path):
    resources = path.removeprefix("Project/Assets/Resources/")
    if resources != path:
        if resources in STORY_RESOURCES:
            return True
        if re.fullmatch(r"prefabs/episode/LocalChapter5[3-6]_(Start|End)\.prefab(?:\.meta)?", resources):
            return True
        if resources.startswith("LocalStoryArt/") and Path(resources).suffix in {".png", ".meta", ".mat"}:
            return True
    return path.startswith("Project/Assets/Plugins/iOS/") and path.endswith((".mm", ".mm.meta"))


def prior_client_source(path):
    return (path.startswith(SOURCE_PREFIXES) and
            path.endswith((".cs", ".cs.meta", ".java", ".java.meta", ".xml", ".xml.meta"))) or (
            path.startswith("Project/ProjectSettings/") and path.endswith((".asset", ".txt")))


def git(root, *args, stdin=None):
    return subprocess.run(["git", "-C", str(root), *args], input=stdin, stdout=subprocess.PIPE, check=True).stdout


def paths(raw):
    return {value for value in raw.decode("utf-8").split("\0") if value}


def read_base(root, members):
    queries = b"".join(f"{BASELINE}:{item}\n".encode("utf-8") for item in members)
    output = git(root, "cat-file", "--batch", stdin=queries)
    offset = 0
    for item in members:
        end = output.index(b"\n", offset)
        _, kind, length = output[offset:end].split()
        if kind != b"blob":
            raise ValueError(f"Expected a baseline blob for {item}")
        start = end + 1
        offset = start + int(length) + 1
        yield item, output[start:offset - 1].replace(b"\r\n", b"\n")


def export(root):
    if OVERLAY.exists():
        raise ValueError("Export into an empty candidate overlay; existing output was kept.")
    base = paths(git(root, "ls-tree", "-r", "--name-only", "-z", BASELINE))
    current = paths(git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard"))
    changed = paths(git(root, "diff", "--name-only", "-z", BASELINE))
    changed.update(current - base)
    patched = sorted(path for path in changed & base if prior_client_source(path))
    rows = []
    for item, before in read_base(root, patched):
        target = file_path(root, item)
        if not target.exists():
            rows.append({"path": item, "before": digest(before), "after": None})
            continue
        after = text_bytes(target)
        if before == after:
            continue
        left = before.decode("utf-8").splitlines(keepends=True)
        right = after.decode("utf-8").splitlines(keepends=True)
        matches = difflib.SequenceMatcher(a=left, b=right, autojunk=False).get_opcodes()
        hunks = [[start, stop - start, right[lo:hi]] for tag, start, stop, lo, hi in matches if tag != "equal"]
        rows.append({"path": item, "before": digest(before), "after": digest(after), "hunks": hunks})

    copied = set()
    for item in sorted(current):
        target = file_path(root, item)
        if not target.is_file():
            continue
        if standalone(item) or authored_project_asset(item) or item not in base and prior_client_source(item):
            output = file_path(OVERLAY, item)
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(target, output)
            copied.add(item)

    private = sorted(item for item in changed - copied if file_path(root, item).is_file() and
                     (item.startswith("Project/Assets/") and not prior_client_source(item) or
                      item.startswith("AndroidSaveManager/") and Path(item).suffix == ".png"))
    (REPO / "patches/client.json").write_text(json.dumps({
        "baseline": "v28, revision defe97bc; accepts unchanged v28 or files already at the exported revision",
        "files": rows,
        "localGenerated": private,
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(copied)} project-authored files and {len(rows)} client patches; {len(private)} changed assets stay local.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--private-repository", required=True, type=Path)
    export(parser.parse_args().private_repository.resolve())
