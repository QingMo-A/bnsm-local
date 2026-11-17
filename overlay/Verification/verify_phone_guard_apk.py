from pathlib import Path
import hashlib,json,zipfile,subprocess,re,datetime
root=Path('I:/BnsmModernWork');v=root/'Verification'
packages=json.loads((v/'android-packages.json').read_text('utf-8-sig'));assert packages['passed']
checks=[]
def check(value,note):
 assert value,note
 checks.append(note)
app=Path(packages['packages'][0]['apk'])
with zipfile.ZipFile(app) as z:
 dex=b''.join(z.read(n) for n in z.namelist() if re.fullmatch(r'classes\d*\.dex',n))
 for name in [b'Llocal/bnsm/bridge/LocalSupport;',b'Llocal/bnsm/bridge/LocalSupport$Panel;',b'Llocal/bnsm/bridge/LocalServiceLink;']:
  check(name in dex,'APK includes '+name.decode())
 check(b'server-logs.zip' in dex and b'client/recent-tail.log' in dex,'Actual Android exporter includes both server logs and client records')
 check(b'android.intent.action.CREATE_DOCUMENT' in dex,'Export uses Android document picker')
manifest=subprocess.check_output(['C:/Android/build-tools/36.0.0/aapt.exe','dump','xmltree',str(app),'AndroidManifest.xml'],text=True,encoding='utf8')
segment=manifest.split('local.bnsm.bridge.LocalSupport$Panel',1)[1].split('E: activity',1)[0]
check('android:exported(0x01010010)=(type 0x12)0x0' in segment,'Diagnostics activity is internal to the game')
for role,code,cert in [('game',8,'561ac9bd66ab120c4d45f637bb8d31e99909be9c31cf190a5ce5bdb2894c05cf'),('server',7,'a2ba085adc252f58e99865735c7107cb2921e20e40ddc087753a2773f0c240b6')]:
 actual=int(re.search(r"versionCode='(\d+)'",(v/f'android-{role}-badging.txt').read_text('utf8')).group(1))
 check(actual>=code,role+' install version increments')
 check('certificate SHA-256 digest: '+cert in (v/f'android-{role}-signature.txt').read_text('utf8'),role+' retains the existing signing certificate')
service=(root/'ServerBuildSource/AndroidHost/ServerService.cs').read_text('utf8')
check('RecoverWhenBound' in service and 'runRequested' in service.split('internal static void RecoverWhenBound',1)[1],'Visible-game recovery retains the manual stop guard')
client=(root/'Project/Assets/Scripts/Assembly-CSharp/LocalAndroidDiagnostics.cs').read_text('utf8')
check('BeforeSceneLoad' in client and 'Application.logMessageReceivedThreaded' in client,'Client diagnostics initialize before login')
check('LocalSupport' in manifest and 'SYSTEM_ALERT_WINDOW' not in manifest,'Support entry requires no floating window permission')
(v/'phone-guard-apk-static.json').write_text(json.dumps({'passed':True,'checks':checks,'deviceVerified':False},indent=2),'utf8')
print('PHONE_GUARD_APK_STATIC_PASSED',len(checks))
