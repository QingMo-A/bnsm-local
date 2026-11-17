using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public partial class LocalClientProbe
{
    private Rect WorldPixels(Transform space,Rect rect)
    {
        Camera camera=NGUITools.FindCameraForLayer(space.gameObject.layer);
        Vector3 a=camera.WorldToScreenPoint(space.TransformPoint(new Vector3(rect.xMin,rect.yMin,0))),b=camera.WorldToScreenPoint(space.TransformPoint(new Vector3(rect.xMax,rect.yMax,0)));
        return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
    }
    private IEnumerator CheckWorldWarUi()
    {
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("-bnsm-world-ui-output="))folder=Path.GetFullPath(arg.Substring("-bnsm-world-ui-output=".Length));
        Directory.CreateDirectory(folder);float until=Time.realtimeSinceStartup+100;
        while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Game"&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForSecondsRealtime(4);
        for(int i=0;i<8;i++){foreach(var n in UnityEngine.Object.FindObjectsOfType<NoticeDialog>())n.On1();foreach(var n in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>())n.DoHide();if(LocalGamePanel.Instance!=null)LocalGamePanel.Instance.CloseNotice();LocalDevPanel.Close();yield return new WaitForSecondsRealtime(.15f);}
        foreach(var input in UnityEngine.Object.FindObjectsOfType<UICamera>()){input.useMouse=false;input.useTouch=false;input.useKeyboard=false;input.useController=false;}
        var checks=new Dictionary<string,bool>();bool baseline=LocalWorldWarLayout.Baseline;
        int[,] samples=baseline?new int[,]{{1920,1080,85}}:new int[,]{{1920,1080,100},{1920,1080,85},{1920,1080,70},{1024,768,85},{2560,1080,70}};
        BscDeckDialog editor=null;
        for(int sample=0;sample<samples.GetLength(0);sample++){
            Screen.SetResolution(samples[sample,0],samples[sample,1],false);LocalUiScale.Instance.Select(samples[sample,2]);yield return new WaitForSecondsRealtime(.6f);
            Dialog.CloseAllSpace();MainController.GetGameSceneController().OnWorldWar();yield return new WaitForSecondsRealtime(3);
            var page=UnityEngine.Object.FindObjectOfType<WorldWarDialog>();checks["lobby"+sample]=page!=null;if(page==null)continue;
            if(!baseline){
                var layout=page.GetComponent<LocalWorldWarLayout>();checks["dedicatedMapLayout"+sample]=layout!=null;
                Rect screen=new Rect(0,0,Screen.width,Screen.height),map=WorldPixels(page.transform,layout.MapRect);
                checks["mapInsideViewport"+sample]=SkinContains(screen,map);
                bool chrome=true;foreach(string name in new[]{"Btn_Back","Btn_Rule","Btn_Rank","Btn_Board","Btn_Defence","Btn_Refresh"}){
                    Transform node=page.transform.Find("Root/Contents/UI/Btn/"+name);var box=node.GetComponent<BoxCollider>();if(box==null)box=node.GetComponentInChildren<BoxCollider>();
                    Rect hit=LayoutColliderRect(box);bool good=SkinContains(screen,hit)&&!map.Overlaps(hit)&&LayoutHit(box,box.center);chrome&=good;checks["chrome"+sample+name]=good;
                }
                checks["fixedChromeOutsideMapAndHit"+sample]=chrome;
                checks["overviewOutsideMap"+sample]=!map.Overlaps(LayoutColliderRect(layout.OverviewButton.GetComponent<BoxCollider>()));
                checks["weeklyTabHit"+sample]=SkinClick(page.cbWeeklyRanking.transform);yield return new WaitForSecondsRealtime(.2f);
                checks["weeklySelected"+sample]=page.cbWeeklyRanking.isChecked;
                checks["dailyTabHit"+sample]=SkinClick(page.cbDailyRanking.transform);yield return new WaitForSecondsRealtime(.2f);
                Rect daily=LayoutColliderRect(page.cbDailyRanking.GetComponent<BoxCollider>()),weekly=LayoutColliderRect(page.cbWeeklyRanking.GetComponent<BoxCollider>());
                checks["rankingTabsSeparated"+sample]=SkinContains(screen,daily)&&SkinContains(screen,weekly)&&!daily.Overlaps(weekly)&&!map.Overlaps(daily)&&!map.Overlaps(weekly);
                DumpDesktopPage("world-map-detail-"+sample,page);yield return new WaitForSecondsRealtime(.2f);
                float before=layout.Zoom;var wheelHit=new RaycastHit();bool picked=UICamera.Raycast(new Vector3(map.center.x,map.center.y,0),ref wheelHit);
                if(picked)DesktopMouseWheel.Dispatch(wheelHit.collider.gameObject,.1f);yield return new WaitForSecondsRealtime(.35f);checks["wheelZoom"+sample]=picked&&layout.Zoom>before;
                var drag=page.GetComponentInChildren<WorldWarDialogDragObject>();Vector3 pan=drag.transform.localPosition;int touch=UICamera.currentTouchID;UICamera.currentTouchID=-1;
                drag.gameObject.SendMessage("OnDrag",new Vector2(0,16),SendMessageOptions.RequireReceiver);drag.gameObject.SendMessage("OnDrag",new Vector2(0,16),SendMessageOptions.RequireReceiver);UICamera.currentTouchID=touch;
                yield return new WaitForSecondsRealtime(.15f);checks["nativeMapPan"+sample]=drag.transform.localPosition!=pan;
                checks["overviewHit"+sample]=SkinClick(layout.OverviewButton.transform);yield return new WaitForSecondsRealtime(.2f);checks["overviewFits"+sample]=Mathf.Abs(layout.Zoom-1)<.01f;
                var spots=page.GetComponentsInChildren<WorldWarSpot>();checks["nativeTerritoriesPreserved"+sample]=spots.Length>=20;
                bool hitAny=false;foreach(var spot in spots){var box=spot.GetComponent<BoxCollider>();if(LayoutHit(box,box.center)){hitAny=true;break;}}checks["nativeTerritoryHit"+sample]=hitAny;
            }
            DumpDesktopPage("world-map-"+sample,page);yield return new WaitForSecondsRealtime(.25f);
            checks["defenceEntryHit"+sample]=SkinClick(page.transform.Find("Root/Contents/UI/Btn/Btn_Defence"));yield return new WaitForSecondsRealtime(1);
            editor=UnityEngine.Object.FindObjectOfType<BscDeckDialog>();checks["defenceEditor"+sample]=editor!=null&&editor.DeckDialog is BscDeckDialogWorldWarDefence;if(editor==null)continue;
            if(!baseline){
                var layout=editor.GetComponent<LocalDeckLayout>();Rect screen=new Rect(0,0,Screen.width,Screen.height);
                Rect list=WorldPixels(editor.transform,layout.ItemsArea),selected=WorldPixels(editor.transform,layout.BattleArea);
                bool tabs=true,picked=true;var rects=new List<Rect>();
                for(int team=0;team<3;team++){
                    var box=editor.coCheckBoxes[team].GetComponent<BoxCollider>();Rect r=LayoutColliderRect(box);rects.Add(r);tabs&=SkinContains(screen,r)&&!list.Overlaps(r)&&!selected.Overlaps(r);
                    picked&=SkinClick(editor.coCheckBoxes[team].transform);yield return new WaitForSecondsRealtime(.12f);picked&=editor.GetDeckIndex()==team&&editor.GetCardCountInDeck(team)==5;
                }
                checks["teamTabsSeparate"+sample]=tabs&&!rects[0].Overlaps(rects[1])&&!rects[1].Overlaps(rects[2]);checks["nativeTeamSwitch"+sample]=picked;
                var done=editor.coDeckWorldWarDefence.coButtonArenaSettingDone;var doneBox=done.GetComponent<BoxCollider>();if(doneBox==null)doneBox=done.GetComponentInChildren<BoxCollider>();
                checks["saveInsideViewportAndHit"+sample]=SkinContains(screen,LayoutColliderRect(doneBox))&&LayoutHit(doneBox,doneBox.center);
                checks["candidateListSeparated"+sample]=SkinContains(screen,list)&&SkinContains(screen,selected)&&!list.Overlaps(selected);
                LocalUiScale.Instance.Select(samples[sample,2]==85?70:85);yield return new WaitForSecondsRealtime(.25f);LocalUiScale.Instance.Select(samples[sample,2]);yield return new WaitForSecondsRealtime(.25f);
                checks["liveDensityRetainsSelectedTeam"+sample]=editor.GetDeckIndex()==2&&editor.GetCardCountInDeck(2)==5;
            }
            DumpDesktopPage("world-defence-"+sample,editor);yield return new WaitForSecondsRealtime(.25f);
        }
        if(!baseline&&editor!=null){
            SkinClick(editor.coCheckBoxes[0].transform);yield return new WaitForSecondsRealtime(.15f);editor.OnDragAndDrop(0,1);yield return new WaitForSecondsRealtime(.2f);
            SkinClick(editor.coCheckBoxes[1].transform);yield return new WaitForSecondsRealtime(.15f);SkinClick(editor.coCheckBoxes[0].transform);yield return new WaitForSecondsRealtime(.15f);
            checks["draftSurvivesSwitch"]=editor.coDeckCards.Any(c=>c.CardSeq==2);
            checks["saveClick"]=SkinClick(editor.coDeckWorldWarDefence.coButtonArenaSettingDone.transform);yield return new WaitForSecondsRealtime(2);
            BscDeckDialog.DoModalWorldWarDefence(null,null);yield return new WaitForSecondsRealtime(1);editor=UnityEngine.Object.FindObjectOfType<BscDeckDialog>();
            var field=typeof(BscDeckDialog).GetField("mDeckCardSeqList",BindingFlags.Instance|BindingFlags.NonPublic);var saved=(List<ulong>)field.GetValue(editor);
            checks["savedFifteenSlotsReopen"]=saved.Count==15&&saved[0]==2&&saved[1]==1&&saved.Skip(5).SequenceEqual(Enumerable.Range(6,10).Select(n=>(ulong)n));
            DumpDesktopPage("world-defence-saved",editor);yield return new WaitForSecondsRealtime(.2f);
        }
        File.WriteAllText(Path.Combine(folder,"world-ui.json"),LitJson.JsonMapper.ToJson(new{passed=errors==0&&checks.Values.All(v=>v),baseline,checks,errors}));
        Application.Quit(errors==0&&checks.Values.All(v=>v)?0:4);
    }
}
