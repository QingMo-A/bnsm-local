using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BscTable;
using DTO;
using UnityEngine;

public partial class LocalClientProbe
{
    private IEnumerator CheckHelperCostume()
    {
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("-bnsm-helper-output="))folder=Path.GetFullPath(arg.Substring("-bnsm-helper-output=".Length));
        Directory.CreateDirectory(folder);var checks=new Dictionary<string,bool>();
        float until=Time.realtimeSinceStartup+110;
        while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Game"&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForSecondsRealtime(4);
        for(int n=0;n<12;n++){
            foreach(var notice in UnityEngine.Object.FindObjectsOfType<NoticeDialog>())notice.On1();
            foreach(var notice in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>())notice.DoHide();
            if(LocalGamePanel.Instance!=null)LocalGamePanel.Instance.CloseNotice();LocalDevPanel.Close();yield return new WaitForSecondsRealtime(.2f);
        }
        Dialog.CloseAllSpace();var p=MonoSingleton<MainClient>.Instance.Player;
        var eventButton=UnityEngine.Object.FindObjectOfType<BscEventComponent>();eventButton.SendMessage("OnClick",SendMessageOptions.RequireReceiver);yield return new WaitForSecondsRealtime(3);
        var events=UnityEngine.Object.FindObjectOfType<BscEventMissionPopup>();events.SendMessage("OnClickedMenu",900011,SendMessageOptions.RequireReceiver);yield return new WaitForSecondsRealtime(1);
        checks["nativeActivityTitle"]=events.coLableTitle.text=="夜樱初绽";
        var rowInfo=typeof(BscEventMssionBodyList_MissionBig).GetField("mInfo",BindingFlags.Instance|BindingFlags.NonPublic);
        BscEventMssionBodyList_MissionBig baseGift=null;
        foreach(var row in UnityEngine.Object.FindObjectsOfType<BscEventMssionBodyList_MissionBig>())if(((PromotionInfo)rowInfo.GetValue(row)).DataId==1)baseGift=row;
        checks["nativeClaimButton"]=baseGift!=null&&baseGift.goButtonReceive.activeInHierarchy;
        if(baseGift!=null)baseGift.SendMessage("OnReceive",SendMessageOptions.RequireReceiver);yield return new WaitForSecondsRealtime(3);
        checks["baseGarmentClaimed"]=p.GetItemCount(1900030001)==1;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"activity-claim.png"));yield return new WaitForSecondsRealtime(.3f);
        Dialog.CloseAllSpace();
        for(uint goal=2;goal<=3;goal++) {
            bool done=false,success=false;
            MonoSingleton<ClientUpdate>.Instance.DoReceivePromotionReward(900011,PROMOTION_TYPE.PROMOTION_TYPE_CONTENTS_MISSION,goal,r=>{done=true;success=true;},(e,u,v)=>{done=true;});
            until=Time.realtimeSinceStartup+12;while(!done&&Time.realtimeSinceStartup<until)yield return null;
            checks["nativeRewardJob"+goal]=success;
        }
        checks["copiesAndFabric"]=p.GetItemCount(1900030001)==36&&p.GetItemCount(280000011)==8&&p.GetItemCount(280000012)==16&&p.GetItemCount(280000013)==50;
        var atlas=LocalHelperCostumeTables.LoadAsset("Texture/ItemIcons/LocalNightSakuraIcons",typeof(UIAtlas)) as UIAtlas;
        checks["sharedIconAtlas"]=atlas!=null&&atlas.GetSprite("NightSakura_collection")!=null;
        for(int n=0;n<3;n++) {
            uint type=(uint)LocalHelperCostumeDefinition.Helpers[n];bool done=false,success=false;
            MonoSingleton<ClientUpdate>.Instance.DoUpdateHelperCostume(type,1900030001,r=>{done=true;success=true;},(e,u,v)=>{done=true;});
            until=Time.realtimeSinceStartup+12;while(!done&&Time.realtimeSinceStartup<until)yield return null;checks["wear"+type]=success;
        }
        var house=BscHelperHouseDialog.DoModal();yield return new WaitForSecondsRealtime(1);
        var selector=gameObject.AddComponent<LocalHelperTrialPreview>();var modelCases=new List<object>();
        foreach(uint type in new uint[]{1,3,2,3,1,2,1}) {
            yield return selector.Select(type);yield return new WaitForSecondsRealtime(.6f);
            string key=type==1?"molly":type==3?"miko":"sophia";var live=house._currentlive2DModel;
            var mapping=Singleton<DataTable>.Instance.GetHelperData((HELPER_TYPE)type).FindModel(1900030001);
            var direct=Resources.Load<GameObject>(LocalHelperCostumeDefinition.ResourceRoot+key);
            var resolved=mapping==null?null:LocalHelperTrialPreview.LoadSetting(mapping.ModelFile);
            modelCases.Add(new{type,key,path=mapping==null?"missing":mapping.ModelFile,direct=direct==null?"missing":direct.name,resolved=resolved==null?"missing":resolved.name,actual=live==null||live.modelSetting==null?"missing":live.modelSetting.name});
            File.WriteAllText(Path.Combine(folder,"model-cases.json"),LitJson.JsonMapper.ToJson(modelCases));
            checks[key+"RealItemModel"]=live!=null&&live.modelSetting.name==key&&live.getLive2DModel().getErrorFlags()==0;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,key+"-formal.png"));yield return new WaitForSecondsRealtime(.2f);
        }
        if(!checks["mollyRealItemModel"]||!checks["mikoRealItemModel"]||!checks["sophiaRealItemModel"]){File.WriteAllText(Path.Combine(folder,"helper-costume-test.json"),LitJson.JsonMapper.ToJson(new{passed=false,checks,errors,productionSaveUsed=false,previewOverrideUsed=LocalHelperTrialPreview.Enabled}));Application.Quit();yield break;}
        yield return selector.Select(1);uint startingGold=p.gameInfo.coinInfo.gold;
        for(uint level=0;level<10;level++) {
            ItemCraftDialog.DoHelperCostumeCraftModal(1900030001+level,null);yield return new WaitForSecondsRealtime(.7f);
            var craft=UnityEngine.Object.FindObjectOfType<ItemCraftDialog>();var container=craft.rightContainer as ItemCraftContainer;
            checks["nativeCraftPage"+level]=craft.CraftResultItemId==1900030002+level&&container!=null;
            if(level==0||level==9){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"craft-"+level+".png"));yield return new WaitForSecondsRealtime(.2f);}
            if(container!=null)container.SendMessage("OnOK",SendMessageOptions.RequireReceiver);
            until=Time.realtimeSinceStartup+8;while(p.GetItemCount(1900030002+level)==0&&Time.realtimeSinceStartup<until)yield return null;
            checks["nativeCraftResult"+(level+1)]=p.GetItemCount(1900030002+level)==1&&p.helperinfo.TrueForAll(x=>x.costumeId==1900030002+level);
            yield return new WaitForSecondsRealtime(2.4f);craft.OnClose();yield return new WaitForSecondsRealtime(.5f);
        }
        checks["goldAndCap"]=startingGold-p.gameInfo.coinInfo.gold==255000&&Singleton<DataTable>.Instance.GetItemData(1900030011,false).Next1Id==0;
        Dialog.CloseAllSpace();house=BscHelperHouseDialog.DoModal();yield return new WaitForSecondsRealtime(1);
        checks["reopenKeepsPlusTen"]=p.helperinfo.TrueForAll(x=>x.costumeId==1900030011)&&house._currentlive2DModel.modelSetting.name=="molly";
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"plus-ten.png"));yield return new WaitForSecondsRealtime(.3f);
        bool passed=errors==0;foreach(bool pass in checks.Values)passed&=pass;
        File.WriteAllText(Path.Combine(folder,"helper-costume-test.json"),LitJson.JsonMapper.ToJson(new{passed,checks,errors,productionSaveUsed=false,previewOverrideUsed=LocalHelperTrialPreview.Enabled}));Application.Quit();
    }
}
