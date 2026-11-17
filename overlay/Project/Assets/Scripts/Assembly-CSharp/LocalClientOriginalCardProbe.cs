using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTO;
using UnityEngine;

public partial class LocalClientProbe
{
    private IEnumerator CheckOriginalCard()
    {
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("-bnsm-competitive-output="))folder=Path.GetFullPath(arg.Substring("-bnsm-competitive-output=".Length));
        Directory.CreateDirectory(folder);var checks=new Dictionary<string,bool>();
        float until=Time.realtimeSinceStartup+100;
        while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Game"&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForSecondsRealtime(4);
        for(int i=0;i<8;i++){foreach(var n in UnityEngine.Object.FindObjectsOfType<NoticeDialog>())n.On1();foreach(var n in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>())n.DoHide();if(LocalGamePanel.Instance!=null)LocalGamePanel.Instance.CloseNotice();LocalDevPanel.Close();yield return new WaitForSecondsRealtime(.2f);}
        Dialog.CloseAllSpace();var p=MonoSingleton<MainClient>.Instance.Player;var card=p.GetCard(1);
        checks["serverOwnedIndependentCard"]=card!=null&&card.cardData.Id==LocalOriginalCardDefinition.CardId;
        var row=Singleton<DataTable>.Instance.GetCardData(LocalOriginalCardDefinition.CardId,false);
        checks["nativeCardName"]=row!=null&&Singleton<TextTable>.Instance.GetText(row.NameAlias)==LocalOriginalCardDefinition.Name;
        checks["newSkillNames"]=Singleton<TextTable>.Instance.GetText(Singleton<DataTable>.Instance.GetSkillData(LocalOriginalCardDefinition.FirstSkillId,false).NameAlias)=="听雨剑势";
        checks["newPortraitTexture"]=AssetManager.Load(row.GetCropCardImageName(0)) is Texture2D;
        var layout=Singleton<DataTable>.Instance.GetCardLayout(row,0);
        checks["portraitUvValidWithRuntimeSeed"]=Mathf.Abs(layout.CropTilling_X-.72f)<.001f&&layout.CropTilling_Y==1f&&Mathf.Abs(layout.CropOffset_X-.24f)<.001f&&layout.CropOffset_Y==0f&&layout.ListOffset_Y==0f&&layout.NonCropPos_X==0f&&layout.NonCropPos_Y==0f;
        BscFullCard.DoModal(row,8,76,5,0);yield return new WaitForSecondsRealtime(2);
        var viewer=UnityEngine.Object.FindObjectOfType<BscFullCard>();checks["nativeFullCardViewer"]=viewer!=null;
        var model=viewer==null?null:viewer.coCardLive2D.getLive2DModel();
        checks["actualOriginalLive2DLoaded"]=model!=null&&model.getErrorFlags()==0&&model.getCanvasWidth()==1024;
        checks["nonzeroModelLayout"]=viewer!=null&&viewer.coCardLive2D.modelSetting.layout.height>0;
        yield return new WaitForEndOfFrame();
        var rendered=ScreenCapture.CaptureScreenshotAsTexture();int colored=0,sampled=0;
        for(int y=rendered.height/5;y<rendered.height*4/5;y+=4)for(int x=rendered.width/3;x<rendered.width*2/3;x+=4){var c=rendered.GetPixel(x,y);float hi=Mathf.Max(c.r,Mathf.Max(c.g,c.b)),lo=Mathf.Min(c.r,Mathf.Min(c.g,c.b));sampled++;if(hi>.12f&&hi-lo>.055f)colored++;}
        checks["nativeArtworkVisible"]=colored>sampled*.03f;UnityEngine.Object.Destroy(rendered);
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-bnsm-original-card-preview")>=0){
            File.WriteAllText(Path.Combine(folder,"preview-ready.json"),LitJson.JsonMapper.ToJson(new{cardId=LocalOriginalCardDefinition.CardId,name=LocalOriginalCardDefinition.Name,checks,normalSaveUsed=false}));yield break;
        }
        float[] before=model==null?null:model.getTransformedPoints(0).ToArray();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,"original-card-live2d-1.png"));yield return new WaitForSecondsRealtime(1.4f);
        float[] after=model==null?null:model.getTransformedPoints(0).ToArray();
        checks["actualLive2DMotion"]=before!=null&&after!=null&&before.Zip(after,(a,b)=>Mathf.Abs(a-b)).Any(x=>x>.05f);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,"original-card-live2d-2.png"));yield return new WaitForSecondsRealtime(.3f);
        if(viewer!=null)viewer.DoHide();yield return new WaitForSecondsRealtime(.5f);Dialog.CloseAllSpace();
        checks["prototypeErrorsZero"]=errors==0;
        File.WriteAllText(Path.Combine(folder,"original-card-ui.json"),LitJson.JsonMapper.ToJson(new{passed=checks.Values.All(x=>x),checks,errors}));
        // Exercise the unchanged arena deck, simulator, replay and settlement
        // with this independently owned card in slot one.
        yield return CheckCompetitive();
    }
}
