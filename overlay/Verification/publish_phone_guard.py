from pathlib import Path
import datetime,json,hashlib,shutil
root=Path('I:/BnsmModernWork');v=root/'Verification'
checked=json.loads((v/'android-packages.json').read_text('utf-8-sig'));assert checked['passed']
def digest(path):
 with Path(path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
for test in ['phone-guard-regression.json','phone-host-guard-regression.json','phone-support-regression.json','phone-guard-apk-static.json']:
 assert json.loads((v/test).read_text('utf8'))['passed'],test
items=checked['packages']
for item,target,previous,code in [
 (items[0],root/'Builds/Android-ARM64/BnsmLocal-arm64.apk',6,7),
 (items[1],root/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk',5,6)]:
 source=Path(item['apk']);assert digest(source)==item['sha256']
 assert target.resolve().is_relative_to(root.resolve())
 temporary=target.with_suffix('.diagnostics.tmp');shutil.copy2(source,temporary);assert digest(temporary)==item['sha256']
 temporary.replace(target)
 item.update(sourceApk=str(source),apk=str(target),versionCode=code)
for p in (root/'Builds/PhoneServer-Diagnostics/apk').iterdir():
 if p.is_file() and p.name!='local.bnsm.serverhost-Signed.apk':shutil.copy2(p,root/'Builds/PhoneServer/apk'/p.name)
assert digest(root/'Builds/PhoneServer/server-content.zip')=='a6b3effbbadd49954ae2343df083fcb5a3b4cd1375c37982ee3683b2f39f3ab1'
assert digest(root/'WindowsPreview/LocalServer/data/gacha.json')=='de7402c41bbd586a67043819331776fea1a8d529e263a77364e2da0c8d12d908'
tests={n:json.loads((v/n).read_text('utf8')) for n in ['phone-guard-regression.json','phone-host-guard-regression.json','phone-support-regression.json','phone-guard-apk-static.json']}
report={'passed':True,'completedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'packages':items,'tests':tests,
 'sourceLogAnalyses':['android-connection-log-analysis-20261101.json','android-connection-second-log-analysis-20261101.json'],
 'features':['Native connection/log entry before login and during play','Explicit export of client log, server log, versions, device, ports, binding and prior exits','Bounded asynchronous client logs, redaction and disk-failure tail','Visible-client process binding retry and user-stop-aware recovery','Foreground stop confirmation and stop/start UI serialization','Monotonic connect completion and timeout; no early BeginConnect callback race','Service instance change notice and return-to-title recovery','Power/background restriction and process priority metadata; monotonic heartbeat scheduling delay'],
 'deviceVerified':False,'phoneConnectedRequiredForRemainingValidation':True,'systemForceStopCannotBeGuaranteedAgainst':True,'windowsPackagesChanged':False,'serverGameContentChanged':False,'saveMigrationRequired':False,
 'serverCoreSha256':digest(root/'Builds/PhoneServer/apk/Bnsm.ServerCore.dll')}
(v/'phone-guard-release.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf8')
status=json.loads((root/'migration-status.json').read_text('utf-8-sig'));status['phase']='Android diagnostics and recovery published: game versionCode7, server versionCode6; static and host regressions pass; pending device background validation. Windows packages unchanged.';status['androidDiagnostics']=report
(root/'migration-status.json').write_text(json.dumps(status,ensure_ascii=False,indent=2),'utf8')
notes='''手机连接排障更新（游戏 versionCode 7 / 服务端 versionCode 6、0.6-diagnostics）

请配套覆盖安装两个 APK，不要卸载或清除应用数据：
游戏：I:/BnsmModernWork/Builds/Android-ARM64/BnsmLocal-arm64.apk
服务端：I:/BnsmModernWork/Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk

1. 先打开手机服务端，看到启动成功后，再打开游戏。
2. 游戏右上方“连接/日志”在登录前也可打开：检查端口、查看版本和绑定状态、打开服务端设置。
3. 出现连接失败时，先点“导出客户端 + 服务端日志 ZIP”，在系统文件选择器中保存，再把 ZIP 发给维护者。
4. 服务端离线时仍可导出客户端记录，ZIP 会标明缺少服务端日志。再打开服务端，通过“查看 / 导出服务端日志”补导一份。
5. 服务端重启后，点“连接/日志”中的“返回标题重新连接”，再正常登录。不会清除存档。
6. 不要从最近任务上划清除服务端；可将服务端锁定后台，按系统提供的选项允许后台运行与自启动。无需悬浮窗权限。

ZIP 包含版本号、手机型号、Android 版本、端口探测、连接阶段、进程绑定、近期异常和系统退出原因。服务器记录电池/后台限制、进程优先级、心跳延迟。日志有容量上限，敏感凭据会遮盖，不包含存档，也不会自动上传。

验证范围：两个 APK 已构建、签名和 ARM64/16 KiB 对齐已检查；客户端日志/网络与服务端诊断接口有自动回归验证。当前这批安装包尚未做手机实机验证，尤其需要在出现问题的机型上检查后台稳定性。系统强行停止仍可能中断服务。
'''
(root/'Builds/手机连接排障说明.txt').write_text(notes,'utf-8-sig')
(root/'Builds/Android-ARM64/使用说明.txt').write_text('战斗吧剑灵 · Unity 6.3 LTS Android ARM64 预览版\n\n'+notes+'\n兼容范围：Android 8.0/API 26 及以上、ARM64、OpenGL ES 3；目标 API 36。使用测试签名。\n','utf-8-sig')
print(json.dumps({'published':True,'gameVersion':7,'serverVersion':6,'game':items[0]['apk'],'server':items[1]['apk'],'deviceVerified':False,'report':str(v/'phone-guard-release.json')},ensure_ascii=False,indent=2))

import sys
sys.path.insert(0,str(root))
from artifact_retention import record_mobile,prune_mobile
record_mobile(items)
prune_mobile(True)
