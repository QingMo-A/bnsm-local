"""Encode untouched native render frames and assemble the helper art trial pack."""
from pathlib import Path
import datetime, hashlib, json, shutil, subprocess, sys, zipfile

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'evidence/helper-trial'
RENDERS = OUTPUT / 'renders'
TOOLS = Path('C:/BnsmModernBuildCache/HelperTrialTools')
sys.path.insert(0, str(TOOLS))
import imageio_ffmpeg

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def run(*arguments):
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error', '-y', *map(str, arguments)], check=True)

def main():
    proof = json.loads((OUTPUT / 'native-asset-validation.json').read_text('utf-8-sig'))
    assert proof['passed'] and len(proof['checks']) == 3
    assert 'HELPER_TRIAL_SHOWCASE_OK' in (OUTPUT / 'showcase.log').read_text('utf-8-sig')
    assert 'error CS' not in (OUTPUT / 'showcase.log').read_text('utf-8-sig')
    keys = ('molly', 'miko', 'sophia')
    for key in keys:
        frames = sorted((RENDERS / (key + '-frames')).glob('*.png'))
        assert len(frames) == 48 and len({sha(p) for p in frames}) > 40
        run('-framerate', 12, '-i', frames[0].parent / '%03d.png', '-vf', 'scale=384:512', '-c:v', 'libx264', '-preset', 'slow', '-crf', 18, '-pix_fmt', 'yuv420p', '-movflags', '+faststart', RENDERS / (key + '.mp4'))
    inputs = []
    for key in keys:
        inputs += ['-i', RENDERS / (key + '.mp4')]
    run(*inputs, '-filter_complex', 'hstack=inputs=3', '-c:v', 'libx264', '-preset', 'slow', '-crf', 18, '-pix_fmt', 'yuv420p', '-movflags', '+faststart', RENDERS / 'collection.mp4')
    run('-i', RENDERS / 'collection.mp4', '-f', 'null', '-')
    summary = dict(passed=True, utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), mode='built-in image_gen edit', scope='art-prototype-only', helpers=list(keys), nativeFramesPerHelper=48, videoDecoded=True, originalRigReused=True, formalPackagesReplaced=False, playerSaveTouched=False, nativeProof=proof, files={})
    files = [OUTPUT / 'HelperNightSakura.unitypackage', OUTPUT / 'native-asset-validation.json', RENDERS / 'collection.png', RENDERS / 'collection.mp4']
    files += [RENDERS / (key + ext) for key in keys for ext in ('.png', '-left.png', '-right.png', '.mp4')]
    files += sorted((ROOT / 'SourceArt/HelperNightSakura').glob('*'))
    for p in files:
        assert p.is_file()
        summary['files'][str(p.relative_to(ROOT)).replace('\\', '/')] = dict(bytes=p.stat().st_size, sha256=sha(p))
    manifest = OUTPUT / 'trial-manifest.json'
    manifest.write_text(json.dumps(summary, ensure_ascii=False, indent=2), 'utf-8')
    package = OUTPUT / 'HelperNightSakura-Trial.zip'
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for p in files + [manifest]:
            name = 'SourceArt/HelperNightSakura/' + p.name if p.parent == ROOT / 'SourceArt/HelperNightSakura' else p.relative_to(OUTPUT).as_posix()
            archive.write(p, name)
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(files) + 1
    print(json.dumps(dict(passed=True, package=str(package), bytes=package.stat().st_size, sha256=sha(package)), ensure_ascii=False))

if __name__ == '__main__':
    main()
