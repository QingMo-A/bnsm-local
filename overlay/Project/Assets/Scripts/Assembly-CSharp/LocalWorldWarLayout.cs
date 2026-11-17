using System;
using UnityEngine;

// Desktop map presentation owns the native map, its clipping and fixed chrome
// together. Territory identities, authored positions and callbacks stay native.
public sealed class LocalWorldWarLayout : MonoBehaviour
{
    private WorldWarDialog page;
    private Transform map,viewport,ui;
    private UIPanel clip;
    private UIPanel[] panels;
    private WorldWarDialogDragObject drag;
    private BoxCollider blocker;
    private LocalShopWidgets widgets;
    private UIEventListener overview;
    private UILabel hint;
    private readonly Rect bounds=Rect.MinMaxRect(-1070,-1000,1010,1110);
    private float targetZoom=2.2f,zoom=2.2f;
    public Rect MapRect {get;private set;}
    public float Zoom {get{return zoom;}}
    public UIEventListener OverviewButton {get{return overview;}}
    internal static readonly bool Baseline=Array.IndexOf(Environment.GetCommandLineArgs(),"-bnsm-world-ui-baseline")>=0;
    public static void Attach(WorldWarDialog owner)
    {
        if(!LocalUiScale.Enabled||LocalMode.MobileLocal||owner==null||Baseline)return;
        var layout=owner.GetComponent<LocalWorldWarLayout>();
        if(layout==null){layout=owner.gameObject.AddComponent<LocalWorldWarLayout>();layout.page=owner;layout.Initialize();}
        layout.BindPanels();
    }
    private void OnEnable(){LocalUiLayoutRuntime.Register(this,LocalUiLayoutPhase.Anchor,Arrange);}
    private void OnDisable(){LocalUiLayoutRuntime.Unregister(this);}
    private void At(Transform node,float x,float y){LocalPageGeometry.At(transform,node,x,y);}
    private Transform Find(string path){return ui==null?null:ui.Find(path);}
    private void Initialize()
    {
        map=transform.Find("Root/Contents/Map");ui=transform.Find("Root/Contents/UI");
        LocalPageGeometry.Own(map);LocalPageGeometry.Own(ui);
        Transform paper=map.Find("Img/Texture_BG");
        if(paper!=null){paper.SetParent(transform.Find("Root"),false);LocalUiViewport.Cover(paper.GetComponent<UIWidget>());NGUITools.MarkParentAsChanged(paper.gameObject);}
        var go=new GameObject("DesktopWorldMapViewport");go.layer=gameObject.layer;viewport=go.transform;viewport.SetParent(map.parent,false);
        LocalPageGeometry.Own(viewport);clip=go.AddComponent<UIPanel>();clip.clipping=UIDrawCall.Clipping.AlphaClip;
        map.SetParent(viewport,false);drag=map.GetComponent<WorldWarDialogDragObject>();NGUITools.MarkParentAsChanged(map.gameObject);
        UIEventListener.Get(map.gameObject).onScroll=delegate(GameObject source,float delta){Scroll(delta);};
        var block=new GameObject("DesktopWorldInputBlocker");block.layer=gameObject.layer;block.transform.SetParent(transform.Find("Root"),false);block.transform.localPosition=new Vector3(0,0,100);
        blocker=block.AddComponent<BoxCollider>();
        var old=Find("BG");if(old!=null){var box=old.GetComponent<BoxCollider>();if(box!=null)box.enabled=false;}
        widgets=new LocalShopWidgets(ui,"DesktopWorldControls",-600);
        hint=widgets.Label(0,0,"拖动地图 · 滚轮缩放",17,Color.white,UIWidget.Pivot.Left);hint.effectStyle=UILabel.Effect.Outline;hint.effectColor=new Color(0,0,0,.75f);
        overview=widgets.Button(0,0,110,"全图",Overview);
        LocalUiPageCanvas.SuppressTownChrome(page);
    }
    private void BindPanels()
    {
        if(map==null)return;panels=map.GetComponentsInChildren<UIPanel>(true);
        foreach(var panel in panels)panel.clipping=UIDrawCall.Clipping.AlphaClip;
        foreach(var spot in map.GetComponentsInChildren<WorldWarSpot>(true)){
            spot.lbServer.lineWidth=205;spot.lbServer.maxLineCount=1;
        }
    }
    public void Overview(){targetZoom=zoom=1;map.localPosition=Vector3.zero;LocalUiLayoutRuntime.Invalidate();}
    public void Scroll(float delta){if(isActiveAndEnabled&&!float.IsNaN(delta)&&!float.IsInfinity(delta))targetZoom=Mathf.Clamp(targetZoom*Mathf.Pow(1.2f,DesktopMouseWheel.Notches(delta)),1,3.5f);}
    private void Clip(UIPanel panel)
    {
        Vector3 a=panel.transform.InverseTransformPoint(transform.TransformPoint(new Vector3(MapRect.xMin,MapRect.yMin,0)));
        Vector3 b=panel.transform.InverseTransformPoint(transform.TransformPoint(new Vector3(MapRect.xMax,MapRect.yMax,0)));
        panel.clipRange=new Vector4((a.x+b.x)/2,(a.y+b.y)/2,Mathf.Abs(b.x-a.x),Mathf.Abs(b.y-a.y));
    }
    private void Arrange()
    {
        if(map==null||ui==null||drag==null)return;Rect view=LocalUiPageCanvas.Viewport(transform);
        MapRect=Rect.MinMaxRect(view.xMin+134,view.yMin+106,view.xMax-30,view.yMax-105);
        At(blocker.transform,view.center.x,view.center.y);blocker.size=new Vector3(view.width,view.height,1);
        At(viewport,MapRect.center.x,MapRect.center.y);
        zoom=Mathf.Lerp(zoom,targetZoom,1-Mathf.Exp(-14*Time.unscaledDeltaTime));if(Mathf.Abs(zoom-targetZoom)<.001f)zoom=targetZoom;
        float scale=Mathf.Min(MapRect.width/bounds.width,MapRect.height/bounds.height)*zoom;map.localScale=new Vector3(scale,scale,1);
        float minX=MapRect.width/2-bounds.xMax*scale,maxX=-MapRect.width/2-bounds.xMin*scale;
        float minY=MapRect.height/2-bounds.yMax*scale,maxY=-MapRect.height/2-bounds.yMin*scale;
        if(minX>maxX)minX=maxX=-bounds.center.x*scale;if(minY>maxY)minY=maxY=-bounds.center.y*scale;
        drag.min_X=minX;drag.max_X=maxX;drag.min_Y=minY;drag.max_Y=maxY;
        Vector3 p=map.localPosition;p.x=Mathf.Clamp(p.x,minX,maxX);p.y=Mathf.Clamp(p.y,minY,maxY);map.localPosition=p;
        var gesture=map.GetComponent<BoxCollider>();gesture.center=new Vector3(-p.x/scale,-p.y/scale,gesture.center.z);gesture.size=new Vector3(MapRect.width/scale,MapRect.height/scale,gesture.size.z);
        Clip(clip);foreach(var panel in panels)if(panel!=null)Clip(panel);
        At(Find("Btn/Btn_Back"),view.xMin+52,view.yMax-50);
        At(Find("Label_Title"),view.xMin+192,view.yMax-50);
        At(Find("Btn/Btn_Rule"),view.xMin+68,view.yMax-182);
        At(Find("Btn/Btn_Rank"),view.xMin+68,view.yMax-302);
        At(Find("Btn/Btn_Board"),view.xMin+68,view.yMax-422);
        At(Find("Btn/Btn_Refresh"),view.xMin+68,view.yMin+165);
        page.cbDailyRanking.transform.localScale=Vector3.one*.72f;page.cbWeeklyRanking.transform.localScale=Vector3.one*.72f;
        At(page.cbDailyRanking.transform,view.center.x-110,view.yMin+44);
        At(page.cbWeeklyRanking.transform,view.center.x+110,view.yMin+44);
        Transform defence=Find("Btn/Btn_Defence");defence.localScale=Vector3.one*.78f;At(defence,view.xMax-85,view.yMin+60);
        At(overview.transform,view.xMax-85,view.yMax-50);At(widgets.Caption(overview).transform,view.xMax-85,view.yMax-50);
        At(hint.transform,view.xMin+142,view.yMin+45);
    }
}
