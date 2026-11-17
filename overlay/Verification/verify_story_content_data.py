"""Check story authoring files and scenery before opening Unity."""
import json
import re
import struct
from pathlib import Path


RESOURCES = Path(__file__).resolve().parents[1] / "Project/Assets/Resources"
SPEAKERS = {"主角", "助手", "洪玄公", "老人", "老小", "法器匠", "押运首领"}


def read_json(name):
    return json.loads((RESOURCES / name).read_text(encoding="utf-8-sig"))


def check_painting(name):
    if not re.fullmatch(r"[A-Za-z0-9]+", name):
        raise ValueError(f"Invalid story painting name {name!r}")
    image = RESOURCES / "LocalStoryArt" / f"{name}.png"
    with image.open("rb") as source:
        signature, width, height = struct.unpack(">16sII", source.read(24))
    if signature != b"\x89PNG\r\n\x1a\n\x00\x00\x00\x0dIHDR" or width < 1280 or height < 720 or width / height < 1.6:
        raise ValueError(f"Expected a landscape PNG with at least a 1280×720 canvas: {image}")
    if not image.with_suffix(".png.meta").is_file():
        raise ValueError(f"Missing Unity texture metadata: {image}")


def main():
    groups = read_json("LocalStoryCatalog.json")
    scenes = read_json("LocalStoryDialogue.json")["Scenes"]
    authored = {}
    for scene in scenes:
        stage = scene["Stage"]
        if stage in authored:
            raise ValueError(f"Duplicate dialogue stage {stage}")
        authored[stage] = scene
        for phase in ("Before", "After"):
            for line in scene.get(phase, []):
                if line["Speaker"] not in SPEAKERS or not line["Text"].strip():
                    raise ValueError(f"Unknown speaker or empty dialogue at {stage}/{phase}")
                if not 0 <= line.get("Motion", 0) <= 16:
                    raise ValueError(f"Invalid speaking gesture at {stage}/{phase}")
            npcs = {line["Speaker"] for line in scene.get(phase, [])} - {"主角", "助手"}
            if len(npcs) > 3:
                raise ValueError(f"Too many NPCs for the native actor positions at {stage}/{phase}")
    arts = set()
    for key, stages in groups.items():
        if not isinstance(stages, list):
            continue
        for stage in stages:
            arts.add(stage["BattleArt"])
            if key.endswith("PreviewStages") and not stage.get("BattleOnly") and not authored.get(stage["Id"], {}).get("Before"):
                raise ValueError(f"Preview story stage has no authored opening: {stage['Id']}")
    for art in sorted(arts):
        check_painting(art)
    print(f"Validated {len(arts)} battle paintings and {len(authored)} stages with authored dialogue.")


if __name__ == "__main__":
    main()
