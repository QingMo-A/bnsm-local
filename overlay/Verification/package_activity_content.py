"""Update only the local activity configuration in the retained Android content archive."""
import hashlib,json,pathlib,shutil,zipfile
root=pathlib.Path(__file__).resolve().parents[1]
archive=root/'Builds/PhoneServer/server-content.zip'
backup=root/'SourceBackups/ActivityExpansionBeforeFix-20261105/server-content.zip'
if not backup.exists():shutil.copy2(archive,backup)
target='LocalServer/data/activities.json'
data=(root/'WindowsPreview'/target).read_bytes()
temporary=archive.with_suffix('.activities.tmp')
with zipfile.ZipFile(archive) as previous,zipfile.ZipFile(temporary,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as output:
    assert target in previous.namelist()
    for entry in previous.infolist():output.writestr(entry,data if entry.filename==target else previous.read(entry.filename))
with zipfile.ZipFile(archive) as previous,zipfile.ZipFile(temporary) as updated:
    assert updated.testzip() is None and previous.namelist()==updated.namelist()
    changed=[name for name in previous.namelist() if previous.read(name)!=updated.read(name)]
    assert changed in [[],[target]],changed
    assert updated.read(target)==data
temporary.replace(archive)
(root/'Verification/activity-content-latest.json').write_text(json.dumps(dict(passed=True,changedEntries=changed,originalCatalogsPreserved=True,contentSha256=hashlib.sha256(archive.read_bytes()).hexdigest(),activityConfigSha256=hashlib.sha256(data).hexdigest()),indent=2),encoding='utf-8')
print('Android content updated; all other entries preserved.')
