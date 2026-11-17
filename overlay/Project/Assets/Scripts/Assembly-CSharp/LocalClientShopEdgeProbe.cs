using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class LocalClientProbe
{
    private bool ShopGoodsAvoidArrows(BscShopGoldDialog shop,out bool hits)
    {
        hits=true;var arrows=shop._objSwitchBtn.GetComponentsInChildren<BoxCollider>().Where(b=>b.enabled&&b.gameObject.activeInHierarchy).Select(LayoutColliderRect).ToArray();
        bool clear=true;int count=0;
        foreach(var cell in shop.GetComponent<LocalShopNativeUI>().VisibleCells.Where(c=>c.gameObject.activeInHierarchy)){
            foreach(var box in cell._cbCost.GetComponentsInChildren<BoxCollider>().Where(b=>b.enabled&&b.gameObject.activeInHierarchy)){
                count++;Rect target=LayoutColliderRect(box);clear&=arrows.All(a=>!a.Overlaps(target));
                Camera camera=NGUITools.FindCameraForLayer(box.gameObject.layer);var hit=new RaycastHit();
                hits&=UICamera.Raycast(camera.WorldToScreenPoint(box.transform.TransformPoint(box.center)),ref hit)&&hit.collider.GetComponentInParent<BscShopItem>()==cell;
            }
            foreach(var w in cell.GetComponentsInChildren<UIWidget>())if(w.isActiveAndEnabled&&w.finalAlpha>.02f)clear&=arrows.All(a=>!a.Overlaps(UiEntryRect(w)));
        }
        hits&=count>0;return clear;
    }
    private IEnumerator CheckShopEdges()
    {
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("-bnsm-shop-edge-output="))folder=Path.GetFullPath(arg.Substring("-bnsm-shop-edge-output=".Length));
        Directory.CreateDirectory(folder);float until=Time.realtimeSinceStartup+100;
        while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Game"&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.3f);
        yield return new WaitForSecondsRealtime(4);
        for(int i=0;i<8;i++){foreach(var n in UnityEngine.Object.FindObjectsOfType<NoticeDialog>())n.On1();foreach(var n in UnityEngine.Object.FindObjectsOfType<BscChatNoticeDialog>())n.DoHide();if(LocalGamePanel.Instance!=null)LocalGamePanel.Instance.CloseNotice();LocalDevPanel.Close();yield return new WaitForSecondsRealtime(.15f);}
        foreach(var input in UnityEngine.Object.FindObjectsOfType<UICamera>()){input.useMouse=false;input.useTouch=false;input.useKeyboard=false;input.useController=false;}
        bool baseline=LocalShopNativeUI.EdgeBaseline;var checks=new Dictionary<string,bool>();bool reproduced=false;
        int[,] samples=baseline?new int[,]{{1920,1080,85}}:new int[,]{{1920,1080,100},{1920,1080,85},{1920,1080,70},{1024,768,85},{2560,1080,70},{1280,720,100},{1600,720,100}};
        for(int sample=0;sample<samples.GetLength(0);sample++){
            Screen.SetResolution(samples[sample,0],samples[sample,1],false);LocalUiScale.Instance.Select(samples[sample,2]);yield return new WaitForSecondsRealtime(.5f);
            for(int kind=1;kind<=BSC_DEFINE.MARKET_SHOP_COUNT;kind++){
                Dialog.CloseAllSpace();BscShopGoldDialog.DoModal((BSC_DEFINE.MARKET_SHOP_TYPE)kind,null,null,false);yield return new WaitForSecondsRealtime(1.2f);
                var shop=UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();string key=sample+"-"+kind;
                checks["nativeOrdinaryShop"+key]=shop!=null&&!shop.IsBossShop;if(shop==null)continue;
                bool hits;bool clear=ShopGoodsAvoidArrows(shop,out hits);reproduced|=!clear;
                if(!baseline){
                    checks["productsClearOfShopArrows"+key]=clear;checks["productPurchaseTargetsHit"+key]=hits;
                    var screen=new Rect(0,0,Screen.width,Screen.height);
                    checks["shopArrowsWithinScreen"+key]=shop._objSwitchBtn.GetComponentsInChildren<BoxCollider>().Where(b=>b.enabled&&b.gameObject.activeInHierarchy).All(b=>SkinContains(screen,LayoutColliderRect(b))&&LayoutHit(b,b.center));
                    var ui=shop.GetComponent<LocalShopNativeUI>();
                    if(shop.NativePageCount>1){
                        checks["pageNextClick"+key]=SkinClick(ui.NextPageButton.transform);yield return new WaitForSecondsRealtime(.15f);
                        checks["pageNextChangesPage"+key]=shop.NativePage==1;checks["secondPageClear"+key]=ShopGoodsAvoidArrows(shop,out hits)&&hits;
                        checks["pagePreviousClick"+key]=SkinClick(ui.PreviousPageButton.transform);yield return new WaitForSecondsRealtime(.15f);checks["pagePreviousChangesPage"+key]=shop.NativePage==0;
                    }
                }
                if(sample==0||kind==2)DumpDesktopPage("shop-edge-"+key,shop);yield return new WaitForSecondsRealtime(.15f);
            }
        }
        if(!baseline){
            Dialog.CloseAllSpace();BscShopMarketDialog.DoModal();yield return new WaitForSecondsRealtime(2);
            var market=UnityEngine.Object.FindObjectOfType<BscShopMarketDialog>();
            var arena=market==null?null:market.GetComponentsInChildren<BscShopMarketItem>().FirstOrDefault(item=>item._item_index==4);
            checks["arenaMarketEntryEnabled"]=arena!=null&&BscShopMarketItem.GetIsOpenShop(4);
            if(arena!=null)arena.OnShop();
            yield return new WaitForSecondsRealtime(1.2f);
            var battleShop=UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();
            checks["arenaMarketEntryOpens"]=battleShop!=null&&battleShop.PurchaseShopId==4&&battleShop.GetComponent<LocalShopNativeUI>().VisibleCells.Any(item=>item.gameObject.activeInHierarchy);
            foreach(int shopId in new[]{1,2,3})
            {
                Dialog.CloseAllSpace();BscShopGoldDialog.DoModal((BSC_DEFINE.MARKET_SHOP_TYPE)shopId,null,null,false);
                yield return new WaitForSecondsRealtime(1.2f);
                var store=UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();
                var cell=store==null?null:store.GetComponent<LocalShopNativeUI>().VisibleCells.FirstOrDefault(item=>item.gameObject.activeInHierarchy);
                if(cell!=null)cell.SendMessage("OnBuyItem",SendMessageOptions.RequireReceiver);
                yield return new WaitForSecondsRealtime(.6f);
                var purchase=UnityEngine.Object.FindObjectOfType<BscShopBuyItemPopup>();
                checks["singlePackUsesNativePrice"+shopId]=purchase!=null&&purchase._tlbCost.gameObject.activeInHierarchy
                    &&purchase.GetComponent<LocalShopQuantity>()==null&&purchase._lbCost.gameObject.activeInHierarchy;
                if(purchase!=null)DumpDesktopPage("shop-price-"+shopId,purchase);
            }
            Dialog.CloseAllSpace();BscShopGoldDialog.DoModal(BSC_DEFINE.MARKET_SHOP_TYPE.GOLD,null,null,false);yield return new WaitForSecondsRealtime(1.2f);
            var shop=UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();
            checks["nativeShopNextClick"]=SkinClick(shop._objSwitchBtn.transform.Find("Btn_Right"));yield return new WaitForSecondsRealtime(3);
            checks["nativeShopNextChangesShop"]=shop.PurchaseShopId!=2;
            bool hits;checks["animatedShopSwitchStillClear"]=ShopGoodsAvoidArrows(shop,out hits)&&hits;
            checks["nativeShopPreviousClick"]=SkinClick(shop._objSwitchBtn.transform.Find("Btn_Left"));yield return new WaitForSecondsRealtime(3);checks["nativeShopPreviousReturns"]=shop.PurchaseShopId==2;
            Dialog.CloseAllSpace();BscShopGoldDialog.DoModalBoss();yield return new WaitForSecondsRealtime(2);
            shop=UnityEngine.Object.FindObjectOfType<BscShopGoldDialog>();var ui=shop.GetComponent<LocalShopNativeUI>();Rect v=LocalUiPageCanvas.Viewport(shop.transform);
            checks["bossShopGuttersUnchanged"]=shop.IsBossShop&&!shop._objSwitchBtn.activeSelf&&Mathf.Abs(ui.GoodsArea.xMin-v.xMin-60)<.01f&&Mathf.Abs(v.xMax-ui.GoodsArea.xMax-60)<.01f;
            DumpDesktopPage("boss-shop-regression",shop);yield return new WaitForSecondsRealtime(.2f);
        }
        bool passed=errors==0&&checks.Values.All(v=>v)&&(baseline?reproduced:true);
        File.WriteAllText(Path.Combine(folder,"shop-edges.json"),LitJson.JsonMapper.ToJson(new{passed,baseline,checks,errors,overlapReproduced=reproduced,mobileAspectGeometryOnly=true,androidDeviceVerified=false}));
        Application.Quit(passed?0:5);
    }
}
