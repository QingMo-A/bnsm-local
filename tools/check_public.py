"""Fail on artifacts, local saves and unexpected paths before publishing."""
import json
import re
import subprocess
import sys
from pathlib import Path

from export_overlay import authored_project_asset, prior_client_source, standalone
from workspace import REPO, file_path


ROOT_ALLOWED = {".gitignore", ".gitattributes", "README.md", "patches/client.json"}
PROTECTED = re.compile(rb"-----BEGIN (?:OPENSSH |RSA |ECDSA |EC )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{35,}|github_pat_[A-Za-z0-9_]{60,}")
MAX_BYTES = 6 * 1024 * 1024


def allowed(path):
    if path in ROOT_ALLOWED:
        return True
    if path.startswith("overlay/"):
        file = path.removeprefix("overlay/")
        return standalone(file) or authored_project_asset(file) or prior_client_source(file)
    if path.startswith("tools/"):
        return Path(path).suffix == ".py" or path == "tools/original-apk.json"
    if path.startswith("docs/"):
        return Path(path).suffix == ".md"
    return path == ".github/workflows/public-tree.yml"


def main():
    data = subprocess.check_output(["git", "-C", str(REPO), "ls-files", "-z", "--cached", "--others", "--exclude-standard"])
    files = sorted(set(data.decode("utf-8").split("\0")) - {""})
    errors = []
    for relative in files:
        target = file_path(REPO, relative)
        if not allowed(relative) or not target.is_file() or target.is_symlink():
            errors.append(f"Unexpected public path: {relative}")
            continue
        if target.stat().st_size > MAX_BYTES:
            errors.append(f"Unexpected large file: {relative}")
            continue
        data = target.read_bytes()
        if data.startswith((b"version https://git-lfs.github.com/spec/v1", b"MZ", b"PK\x03\x04", b"7z\xbc\xaf\x27\x1c")):
            errors.append(f"Opaque release/build artifact: {relative}")
        elif PROTECTED.search(data) and relative != "tools/check_public.py":
            errors.append(f"Possible private key or credential: {relative}")
    patch = json.loads((REPO / "patches/client.json").read_text(encoding="utf-8"))
    for row in patch["files"]:
        if not prior_client_source(row["path"]):
            errors.append(f"Unexpected client patch path: {row['path']}")
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"Checked {len(files)} public files and {len(patch['files'])} client patch paths.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
