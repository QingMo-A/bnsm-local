"""Build the small, same-certificate native archive manager on the SSD."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,datetime
ROOT=Path(__file__).resolve().parent
CACHE=Path('C:/BnsmModernBuildCache/SaveManager/GradleProject')
ANDROID=Path('I:/BnsmTools/Unity6.3/6000.3.27f1/Editor/Data/PlaybackEngines/AndroidPlayer')
def main():
    source=ROOT/'AndroidSaveManager'
    shutil.copytree(source,CACHE,dirs_exist_ok=True,ignore=shutil.ignore_patterns('build','.gradle','local.properties'))
    bridge=CACHE/'app/src/main/java/local/bnsm/bridge';bridge.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(ROOT/'Project/Assets/Plugins/Android/ExportProvider.java',bridge/'ExportProvider.java')
    (CACHE/'local.properties').write_text('sdk.dir=C\\:/Android\n',encoding='utf-8')
    env=dict(os.environ,GRADLE_USER_HOME='C:/BnsmModernBuildCache/GradleHome',BNSM_TRIAL_KEYSTORE='C:/Users/Altsc/.android/debug.keystore')
    command=[str(ANDROID/'OpenJDK/bin/java.exe'),'-classpath',str(ANDROID/'Tools/gradle/lib/gradle-launcher-9.3.1.jar'),'org.gradle.launcher.GradleMain','--console=plain','--max-workers=2','assembleRelease']
    log=ROOT/'Verification/android-save-manager-build.log'
    with log.open('wb') as out:subprocess.run(command,cwd=CACHE,env=env,stdout=out,stderr=subprocess.STDOUT,check=True)
    apk=CACHE/'app/build/outputs/apk/release/app-release.apk'
    report=dict(apk=str(apk),bytes=apk.stat().st_size,sha256=hashlib.sha256(apk.read_bytes()).hexdigest(),builtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    report['sourceSha256']={name:hashlib.sha256((source/name).read_bytes()).hexdigest() for name in ['app/src/main/java/local/bnsm/savemanager/MainActivity.java','app/src/main/java/local/bnsm/savemanager/SaveBackups.java','app/src/main/java/local/bnsm/savemanager/IncomingProvider.java','app/src/main/AndroidManifest.xml','app/build.gradle']}
    (ROOT/'Verification/android-save-manager-build.json').write_text(json.dumps(report,indent=2),'utf-8')
    print(json.dumps(report),flush=True)
if __name__=='__main__':main()
