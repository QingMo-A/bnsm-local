import datetime,json,pathlib,hashlib
r=pathlib.Path('I:/BnsmModernWork')
server=json.loads((r/'Verification/big-boss-entry-latest.json').read_text(encoding='utf-8-sig'))
release=json.loads((r/'Verification/competitive-release.json').read_text(encoding='utf8'))
assert server['passed'] and server['serverCoreSha256']==release['serverCoreSha256']
assert server['serverCoreSha256']==hashlib.sha256((r/'WindowsPreview/LocalServer/WindowsLatest/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
proof={'verifiedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'serverPassed':True,'serverChecks':len(server['checks']),'bosses':[2635101,2637101],'serverCoreSha256':server['serverCoreSha256'],'publishedVersion':11,'newBinaryRequired':False,'nativeRetestCompleted':False,'nativeRetestReason':'Published game is currently running; Unity single-instance launch rejected the diagnostic second process. Existing user game was not interrupted.','productionSaveUsed':False,'userConfirmedErrorWasBeforeUpdate':True}
(r/'Verification/big-boss-v11-recheck.json').write_text(json.dumps(proof,indent=2),encoding='utf8')
print(json.dumps({'currentPublishedServerVerified':True,'serverChecks':len(server['checks']),'newBuildNeeded':False}))
