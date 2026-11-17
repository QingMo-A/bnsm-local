"""Unpack a user-supplied original APK and prepare a local Unity source export."""
import argparse
import hashlib
import json
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request
import zipfile
from html.parser import HTMLParser
from pathlib import Path

from workspace import LOCAL, REPO, file_path


APK_PREFIX = "assets/bin/Data/"


def output_directory(path):
    resolved = path.resolve()
    if LOCAL.resolve() not in resolved.parents or resolved.exists():
        raise ValueError("Choose a new output directory under LocalBuild.")
    return resolved


def sha_file(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def is_pe(path):
    with path.open("rb") as source:
        header = source.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            return False
        source.seek(struct.unpack_from("<I", header, 60)[0])
        return source.read(4) == b"PE\0\0"


def unpack(args):
    metadata = json.loads((REPO / "tools/original-apk.json").read_text(encoding="utf-8"))
    checksum = sha_file(args.apk)
    if checksum != metadata["sourceApkSha256"]:
        raise ValueError(f"Unsupported original APK SHA-256: {checksum}")
    dest = output_directory(args.output)
    dest.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".apk-", dir=dest.parent) as temporary:
        staging = Path(temporary) / "input"
        with zipfile.ZipFile(args.apk) as apk:
            names = {}
            for item in apk.infolist():
                if item.is_dir() or not item.filename.startswith(APK_PREFIX):
                    continue
                relative = item.filename[len(APK_PREFIX):]
                target = file_path(staging / "ClientData", relative)
                if target.as_posix().casefold() in names:
                    raise ValueError(f"Duplicate APK entry: {item.filename}")
                names[target.as_posix().casefold()] = item.filename
                target.parent.mkdir(parents=True, exist_ok=True)
                with apk.open(item) as source, target.open("xb") as destination:
                    shutil.copyfileobj(source, destination)
            assembly = staging / "ClientData/Managed/Assembly-CSharp.dll"
            if not assembly.is_file():
                raise ValueError("APK does not contain the expected Unity Mono layout.")
            for icon in metadata["entries"]:
                content = apk.read(icon["sourceEntry"])
                if hashlib.sha256(content).hexdigest() != icon["sha256"]:
                    raise ValueError("Original launcher icon checksum did not match.")
                target = file_path(staging / "AndroidGameIcon", icon["path"].replace("\\", "/"))
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
            shutil.copy2(REPO / "tools/original-apk.json", staging / "AndroidGameIcon/origin.json")
        (staging / "input.json").write_text(json.dumps({
            "apkSha256": checksum, "extractedEntries": len(names),
            "managedEntryIsPE": is_pe(assembly),
        }, indent=2) + "\n", encoding="utf-8")
        staging.rename(dest)
    print(dest)


def unpack_windows(args):
    source = args.game_data.resolve()
    assembly = source / "Managed/Assembly-CSharp.dll"
    if not source.name.endswith("_Data") or not assembly.is_file() or not is_pe(assembly):
        raise ValueError("Choose the *_Data directory of a local Windows Mono client.")
    if not ((source / "data.unity3d").is_file() or (source / "globalgamemanagers").is_file()):
        raise ValueError("No Unity serialized data found.")
    dest = output_directory(args.output)
    if source == dest or source in dest.parents:
        raise ValueError("The input and output must be separate.")
    dest.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".windows-", dir=dest.parent) as temporary:
        staging = Path(temporary) / "input"
        shutil.copytree(source, staging / "ClientData")
        (staging / "input.json").write_text(json.dumps({
            "source": "local Windows Mono distribution",
            "assemblySha256": sha_file(staging / "ClientData/Managed/Assembly-CSharp.dll"),
        }, indent=2) + "\n", encoding="utf-8")
        staging.rename(dest)
    print(dest)


def decompile(args):
    if not is_pe(args.assembly):
        raise ValueError("Supply a readable local .NET assembly. This APK entry cannot be opened by ILSpy as supplied.")
    dest = output_directory(args.output)
    dest.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".decompile-", dir=dest.parent) as temporary:
        staging = Path(temporary) / "source"
        command = [args.ilspy, "-p", "--nested-directories", "--disable-updatecheck",
                   "-lv", args.language, "-o", str(staging)]
        if args.references:
            command += ["-r", str(args.references.resolve())]
        subprocess.run([*command, str(args.assembly.resolve())], check=True)
        if not list(staging.glob("*.csproj")):
            raise ValueError("ILSpy did not produce a C# project.")
        staging.rename(dest)
    print(dest)


class RipperSettings(HTMLParser):
    def __init__(self):
        super().__init__()
        self.active = False
        self.select = None
        self.values = {}

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if tag == "form" and attributes.get("action") == "/Settings/Update":
            self.active = True
        if not self.active:
            return
        if tag == "select":
            self.select = attributes.get("name")
        if tag == "option" and "selected" in attributes and self.select:
            self.values[self.select] = attributes["value"]
        if tag == "input" and "name" in attributes and (attributes.get("type") != "checkbox" or "checked" in attributes):
            self.values[attributes["name"]] = attributes.get("value", "true")

    def handle_endtag(self, tag):
        if tag == "form":
            self.active = False
        elif tag == "select":
            self.select = None


def export_unity(args):
    source = args.input.resolve() / "ClientData"
    if not source.is_dir():
        raise ValueError("Run unpack first or supply its local output directory.")
    if not 1 <= args.ripper_port <= 65535:
        raise ValueError("AssetRipper loopback port must be 1 through 65535.")
    dest = output_directory(args.output)
    dest.parent.mkdir(parents=True, exist_ok=True)
    base = f"http://127.0.0.1:{args.ripper_port}"

    class LocalRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, request, fp, code, msg, headers, newurl):
            if not newurl.startswith(base + "/"):
                raise ValueError("AssetRipper redirected outside its local endpoint.")
            return super().redirect_request(request, fp, code, msg, headers, newurl)

    client = urllib.request.build_opener(urllib.request.ProxyHandler({}), LocalRedirect())

    def api(path, values=None):
        url = base + path
        request = url if values is None else urllib.request.Request(
            url, urllib.parse.urlencode(values, doseq=True).encode("utf-8"), method="POST")
        with client.open(request, timeout=3600) as response:
            if urllib.parse.urlparse(response.geturl()).hostname != "127.0.0.1":
                raise ValueError("Expected a loopback AssetRipper endpoint.")
            return response.read().decode("utf-8", "replace")

    with tempfile.TemporaryDirectory(prefix=".ripper-", dir=dest.parent, ignore_cleanup_errors=True) as temporary:
        staging = Path(temporary) / "output"
        load = source
        if args.assembly:
            if not is_pe(args.assembly):
                raise ValueError("--assembly must be a readable local .NET assembly.")
            load = Path(temporary) / "ClientData"
            shutil.copytree(source, load)
            shutil.copy2(args.assembly, load / "Managed/Assembly-CSharp.dll")
        paths = [str(load), str(load / "Managed")]
        if args.hot_update:
            if not args.hot_update.is_dir():
                raise ValueError("--hot-update must be a local bundle directory.")
            paths.append(str(args.hot_update.resolve()))
        settings = RipperSettings()
        settings.feed(api("/Settings/Edit"))
        if not settings.values:
            raise ValueError("AssetRipper settings form does not match the supported local Web API.")
        settings.values.update(ScriptContentLevel="Level2", ScriptExportMode="Decompiled",
                               ScriptLanguageVersion="CSharp4", TargetVersion="5.6.6f2",
                               TextExportMode="Bytes", ShaderExportMode="Decompile", AudioExportFormat="PreferWav")
        api("/Settings/Update", settings.values)
        try:
            api("/LoadFolder", {"Path": paths})
            api("/Export/UnityProject", {"Path": str(staging)})
        finally:
            api("/Reset", {})
        project = staging / "ExportedProject"
        if not (project / "Assets").is_dir() or not (project / "ProjectSettings").is_dir():
            raise ValueError("AssetRipper did not produce an editable Unity project.")
        staging.rename(dest)
    print(dest / "ExportedProject")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    command = commands.add_parser("unpack")
    command.add_argument("--apk", required=True, type=Path)
    command.add_argument("--output", type=Path, default=LOCAL / "original")
    command.set_defaults(run=unpack)
    command = commands.add_parser("unpack-windows")
    command.add_argument("--game-data", required=True, type=Path)
    command.add_argument("--output", type=Path, default=LOCAL / "previous-windows")
    command.set_defaults(run=unpack_windows)
    command = commands.add_parser("decompile")
    command.add_argument("--assembly", required=True, type=Path)
    command.add_argument("--references", type=Path)
    command.add_argument("--ilspy", default="ilspycmd")
    command.add_argument("--language", default="CSharp3")
    command.add_argument("--output", type=Path, default=LOCAL / "decompiled")
    command.set_defaults(run=decompile)
    command = commands.add_parser("export-unity")
    command.add_argument("--input", type=Path, default=LOCAL / "original")
    command.add_argument("--ripper-port", required=True, type=int)
    command.add_argument("--assembly", type=Path)
    command.add_argument("--hot-update", type=Path)
    command.add_argument("--output", type=Path, default=LOCAL / "ripper")
    command.set_defaults(run=export_unity)
    args = parser.parse_args()
    try:
        args.run(args)
    except (OSError, ValueError, subprocess.CalledProcessError, zipfile.BadZipFile) as exc:
        print(f"Original game import failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
