"""Non-destructive Home/resume check; never clears, kills or uninstalls a user app."""
import json, time
import embedded_phone_tools as phone

before=phone.state()
phone.adb('shell','input keyevent 3')
time.sleep(30)
background=phone.state()
phone.adb('shell','am start -n local.bnsm.integratedtest/com.unity3d.player.UnityPlayerActivity')
time.sleep(4)
resumed=phone.state()
result=dict(before=before,background=background,resumed=resumed,
    sameProcess=len({x['processId'] for x in [before,background,resumed]})==1,
    heartbeatsContinue=background['heartbeat']>before['heartbeat'],
    readyOnResume=resumed['ready'])
result['passed']=result['sameProcess'] and result['heartbeatsContinue'] and result['readyOnResume']
(phone.OUT/'background.json').write_text(json.dumps(result,indent=2))
print(json.dumps({k:v for k,v in result.items() if isinstance(v,bool)}))
