"""Repeat the actual native-file and authoritative .NET interoperability checks."""
from pathlib import Path
import base64,hashlib,json,shutil,subprocess,urllib.request,uuid,time
ROOT=Path(__file__).resolve().parents[1]
JDK=Path('I:/BnsmTools/Unity6.3/6000.3.27f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin')
CACHE=Path('C:/BnsmModernBuildCache/SaveManager')
AOSP_COMMIT='4ef949a4fc8d9a0fd3c24aeb622e88a4388f6c00'
AOSP_HASHES={
'JSON':'ba5224f9ba7cc34497b9ebba6975cfb5c89eae0a6a09bb6756f0648421967550',
'JSONArray':'059d3112b41f0e35b24c80bd2d35e46137903686c7e87b82fd26baf18c704c04',
'JSONObject':'f61e85aea33982801e61d93be32ee40f838782b943d1b710a904a17bf3edfe40',
'JSONStringer':'4e15d19748dc7ba7051344093cf18beae6774b6a78b541c803d2045d4de397c4',
'JSONTokener':'08ce4beb3b6e35336798f182f9a5c09843fafba60933064f3d8f26cdc57d1601',
'JSONException':'3106d6c3b37c8d5b7f32b37d3337fc0c8ea2ab9f884f50eb56e09928cf829208'}
def aosp_json():
    folder=CACHE/'AospJson';folder.mkdir(parents=True,exist_ok=True)
    files=[]
    for name,want in AOSP_HASHES.items():
        path=folder/(name+'.java')
        if not path.exists():
            for attempt in range(4):
                try:
                    with urllib.request.urlopen('https://android.googlesource.com/platform/libcore/+/'+AOSP_COMMIT+'/json/src/main/java/org/json/'+name+'.java?format=TEXT',timeout=25) as reply:path.write_bytes(base64.b64decode(reply.read()))
                    break
                except OSError:
                    if attempt==3:raise
                    time.sleep(1)
        assert hashlib.sha256(path.read_bytes()).hexdigest()==want
        files.append(path)
    return files

def main():
    CACHE.mkdir(parents=True,exist_ok=True)
    sources=aosp_json()
    classes=CACHE/'aosp-contract';classes.mkdir(exist_ok=True)
    def run(args):subprocess.run(list(map(str,args)),cwd=ROOT,check=True)
    run([JDK/'javac.exe','-encoding','UTF-8','-d',classes,*sources,ROOT/'Project/Assets/Plugins/Android/JsonDocumentPatch.java',ROOT/'Project/Assets/Plugins/Android/SaveArchiveReader.java',ROOT/'Project/Assets/Plugins/Android/SharedWorkspace.java',ROOT/'Verification/AndroidManagerContract.java',ROOT/'Verification/AndroidSaveImportContract.java',ROOT/'Verification/AndroidBotStartupContract.java',ROOT/'Verification/AndroidConsoleExportContract.java'])
    fixture=ROOT/'Verification/android-manager-contract'/('java-'+uuid.uuid4().hex);fixture.mkdir(parents=True)
    run([JDK/'java.exe','-cp',str(classes),'AndroidManagerContract','test',fixture])
    run([JDK/'java.exe','-cp',str(classes),'AndroidSaveImportContract',fixture/'import-tests'])
    run([JDK/'javac.exe','-encoding','UTF-8','-cp',classes,'-d',classes,ROOT/'AndroidSaveManager/app/src/main/java/local/bnsm/savemanager/SaveBackups.java',ROOT/'Verification/AndroidSaveBackupContract.java'])
    run([JDK/'java.exe','-cp',str(classes),'local.bnsm.savemanager.AndroidSaveBackupContract',fixture/'backup-tests'])
    run([JDK/'javac.exe','-encoding','UTF-8','-cp',classes,'-d',classes,ROOT/'Verification/AndroidExportPerformanceContract.java'])
    run([JDK/'java.exe','-Xmx512m','-cp',classes,'AndroidExportPerformanceContract'])
    performance=ROOT/'Verification/android-manager-contract/export-performance.json'
    proof=json.loads(performance.read_text('utf-8'))
    proof['sourceSha256']={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'Project/Assets/Plugins/Android'/name for name in ['JsonDocumentPatch.java','SaveArchiveReader.java','SharedWorkspace.java']]}
    performance.write_text(json.dumps(proof,indent=2),encoding='utf-8')
    run(['dotnet','build',ROOT/'Verification/EmbeddedServerTests/EmbeddedServerTests.csproj','-c','Release','--nologo','-v','quiet'])
    # Exercise the same isolated assemblies that Unity packages, including when
    # the test build regenerates an equivalent core with a different file hash.
    test_bin=ROOT/'Verification/EmbeddedServerTests/bin/Release/net10.0'
    embedded=ROOT/'Project/Assets/Plugins/EmbeddedServer'
    for name in ('Bnsm.EmbeddedServer.dll','Bnsm.ServerGameModel.dll','Bnsm.ServerLegacyEngine.dll'):
        shutil.copyfile(embedded/name,test_bin/name)
    run(['dotnet',ROOT/'Verification/EmbeddedServerTests/bin/Release/net10.0/Bnsm.LocalServer.dll','--embedded-save-test'])
    run([JDK/'java.exe','-cp',classes,'AndroidConsoleExportContract'])
    run(['dotnet',ROOT/'Verification/EmbeddedServerTests/bin/Release/net10.0/Bnsm.LocalServer.dll','--bot-management-test'])
    run(['dotnet',ROOT/'Verification/EmbeddedServerTests/bin/Release/net10.0/Bnsm.LocalServer.dll','--bot-numeric-prepare'])
    run([JDK/'java.exe','-cp',classes,'AndroidBotStartupContract',ROOT/'Verification/android-manager-contract/numeric-fixture.json'])
    run(['dotnet',ROOT/'Verification/EmbeddedServerTests/bin/Release/net10.0/Bnsm.LocalServer.dll','--bot-numeric-test'])
if __name__=='__main__':main()
