"""Build an isolated native bridge fixture and instrumentation; never ship these APKs."""
from pathlib import Path
import json,os,shutil,subprocess,hashlib
ROOT=Path(__file__).resolve().parents[1]
CACHE=Path('C:/BnsmModernBuildCache/AndroidManagementTest/GradleProject')
ANDROID=Path('I:/BnsmTools/Unity6.3/6000.3.27f1/Editor/Data/PlaybackEngines/AndroidPlayer')
def write(name,text):
    p=CACHE/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,'utf-8')
write('settings.gradle',"pluginManagement { repositories { google(); mavenCentral(); gradlePluginPortal() } }\ndependencyResolutionManagement { repositories { google(); mavenCentral() } }\ninclude ':fixture', ':tests'\n")
write('build.gradle',"plugins { id 'com.android.application' version '9.1.1' apply false }\n")
write('gradle.properties','org.gradle.jvmargs=-Xmx2048m\norg.gradle.workers.max=2\n')
write('local.properties','sdk.dir=C\\:/Android\n')
common="""plugins { id 'com.android.application' }
android {
 namespace '%s'; compileSdk 36
 defaultConfig { applicationId '%s'; minSdk 26; targetSdk 36; versionCode = %s; versionName 'test' }
 signingConfigs { test { storeFile file(new File(System.getProperty('user.home'), '.android/debug.keystore')); storePassword 'android'; keyAlias 'androiddebugkey'; keyPassword 'android' } }
 buildTypes { release { signingConfig signingConfigs.test; debuggable true } }
 compileOptions { sourceCompatibility JavaVersion.VERSION_17; targetCompatibility JavaVersion.VERSION_17 }
}
"""
write('fixture/build.gradle',common%('local.bnsm.integratedtest','local.bnsm.integratedtest',"(project.findProperty('fixtureVersion') ?: '33').toInteger()"))
write('tests/build.gradle',common%('local.bnsm.tests','local.bnsm.managementtests','1'))
write('fixture/src/main/AndroidManifest.xml','''<manifest xmlns:android="http://schemas.android.com/apk/res/android">
<permission android:name="local.bnsm.integratedtest.permission.MANAGE_SAVES" android:protectionLevel="signature" />
<queries><package android:name="local.bnsm.savemanager" /></queries>
<application android:label="Native management fixture" android:theme="@android:style/Theme.Material.Light.NoActionBar">
<activity android:name="local.bnsm.bridge.SaveManagerGateway" android:process=":management" android:exported="true" android:permission="local.bnsm.integratedtest.permission.MANAGE_SAVES" android:theme="@android:style/Theme.Material.Light.Dialog.NoActionBar" android:enableOnBackInvokedCallback="false" android:configChanges="orientation|screenSize|smallestScreenSize|keyboardHidden|fontScale|density|uiMode" />
<provider android:name="local.bnsm.bridge.SaveBridge" android:process=":management" android:authorities="local.bnsm.integratedtest.management" android:exported="true" android:permission="local.bnsm.integratedtest.permission.MANAGE_SAVES" />
<service android:name="local.bnsm.bridge.GameLeaseService" android:process=":lease-fixture" android:exported="true" android:permission="local.bnsm.integratedtest.permission.MANAGE_SAVES" />
<service android:name="local.bnsm.bridge.EmbeddedService" android:exported="false" />
</application></manifest>''')
write('tests/src/main/AndroidManifest.xml','''<manifest xmlns:android="http://schemas.android.com/apk/res/android"><application android:label="Native management tests" /><instrumentation android:name="local.bnsm.tests.TransportInstrumentation" android:targetPackage="local.bnsm.savemanager" android:functionalTest="true" /></manifest>''')
sources={}
for name in ['ManagementActions.java','SaveManagerGateway.java','SaveBridge.java','SharedWorkspace.java','JsonDocumentPatch.java','SaveArchiveReader.java','EmbeddedService.java']:
    source=ROOT/'Project/Assets/Plugins/Android'/name
    write('fixture/src/main/java/local/bnsm/bridge/'+name,source.read_text('utf-8'))
    sources[name]=hashlib.sha256(source.read_bytes()).hexdigest()
write('fixture/src/main/java/local/bnsm/bridge/LocalSupport.java','package local.bnsm.bridge; public final class LocalSupport { public static void notice(String s){android.util.Log.i("Fixture",s);} }')
write('fixture/src/main/java/local/bnsm/bridge/GameLeaseService.java',(ROOT/'Verification/AndroidBridgeTests/GameLeaseService.java').read_text('utf-8'))
write('tests/src/main/java/local/bnsm/tests/TransportInstrumentation.java',(ROOT/'Verification/AndroidBridgeTests/TransportInstrumentation.java').read_text('utf-8'))
env=dict(os.environ,GRADLE_USER_HOME='C:/BnsmModernBuildCache/GradleHome')
command=[str(ANDROID/'OpenJDK/bin/java.exe'),'-classpath',str(ANDROID/'Tools/gradle/lib/gradle-launcher-9.3.1.jar'),'org.gradle.launcher.GradleMain','--console=plain','--max-workers=2','assembleRelease']
with (ROOT/'Verification/android-manager-native-build.log').open('wb') as out:subprocess.run(command,cwd=CACHE,env=env,stdout=out,stderr=subprocess.STDOUT,check=True)
fixture=CACHE/'fixture/build/outputs/apk/release/fixture-release.apk'
shutil.copyfile(fixture,CACHE.parent/'fixture-v33.apk')
with (ROOT/'Verification/android-manager-native-old-build.log').open('wb') as out:subprocess.run(command[:-1]+['-PfixtureVersion=31',':fixture:assembleRelease'],cwd=CACHE,env=env,stdout=out,stderr=subprocess.STDOUT,check=True)
shutil.copyfile(fixture,CACHE.parent/'fixture-v31.apk')
(ROOT/'Verification/android-manager-native-sources.json').write_text(json.dumps(sources,indent=2),'utf-8')
print('Native fixture and instrumentation built')
