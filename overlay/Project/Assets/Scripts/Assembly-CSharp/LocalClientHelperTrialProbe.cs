using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public partial class LocalClientProbe
{
    private IEnumerator CheckHelperTrialPreview()
    {
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("-bnsm-helper-output="))folder=Path.GetFullPath(arg.Substring("-bnsm-helper-output=".Length));
        Directory.CreateDirectory(folder);
        float until=Time.realtimeSinceStartup+110;
        while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Game"&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForSecondsRealtime(4);
        for(int i=0;i<12;i++) {
            foreach(var n in UnityEngine.Object.FindObjectsOfType<NoticeDialog>())n.On1();
            foreach(var n in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>())n.DoHide();
            if(LocalGamePanel.Instance!=null)LocalGamePanel.Instance.CloseNotice();LocalDevPanel.Close();
            yield return new WaitForSecondsRealtime(.2f);
        }
        Dialog.CloseAllSpace();var ui=gameObject.AddComponent<LocalHelperTrialPreview>();
        var house=BscHelperHouseDialog.DoModal();yield return new WaitForSecondsRealtime(2);
        var checks=new Dictionary<string,bool>();
        foreach(uint type in new uint[]{1,3,2}) {
            yield return ui.Select(type);yield return new WaitForSecondsRealtime(.8f);
            string key=type==1?"molly":type==3?"miko":"sophia";
            var live=house._currentlive2DModel;
            checks[key+"NativeHouse"]=LocalHelperTrialPreview.Current(house)==type;
            checks[key+"TrialAsset"]=live!=null&&live.modelSetting!=null&&live.modelSetting.name==key;
            checks[key+"NativeModelValid"]=live!=null&&live.getLive2DModel()!=null&&live.getLive2DModel().getErrorFlags()==0;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,key+"-native.png"));yield return new WaitForSecondsRealtime(.3f);
        }
        yield return ui.Select(1);LocalHelperTrialPreview.UseTrial=false;house.SendMessage("ChangeHelperInfo",SendMessageOptions.RequireReceiver);
        yield return new WaitForSecondsRealtime(.6f);checks["OriginalToggle"]=house._currentlive2DModel.modelSetting.name=="1004B_05";
        LocalHelperTrialPreview.UseTrial=true;house.SendMessage("ChangeHelperInfo",SendMessageOptions.RequireReceiver);
        yield return new WaitForSecondsRealtime(.6f);checks["TrialToggle"]=house._currentlive2DModel.modelSetting.name=="molly";
        bool passed=errors==0;foreach(bool value in checks.Values)passed&=value;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"preview-ready.png"));
        File.WriteAllText(Path.Combine(folder,"preview-ready.json"),LitJson.JsonMapper.ToJson(new{passed,checks,errors,normalSaveUsed=false,artPreviewOnly=true}));
        Debug.Log("HELPER_TRIAL_NATIVE_READY passed="+passed);
        // Remain interactive until the user closes this real game window.
    }
}
