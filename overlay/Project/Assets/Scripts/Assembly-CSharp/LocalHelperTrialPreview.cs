using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>Explicit, isolated art-preview launch only; normal wardrobe IDs and saves stay native.</summary>
public sealed class LocalHelperTrialPreview : MonoBehaviour
{
    private static readonly bool requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-bnsm-helper-outfit-preview")>=0&&Array.IndexOf(Environment.GetCommandLineArgs(),"-bnsm-probe")>=0;
    public static bool Enabled {get{return requested&&LocalMode.Enabled;}}
    public static bool UseTrial=true;
    private Font previewFont;
    private bool switching;
    private static readonly FieldInfo helperType=typeof(BscHelperHouseDialog).GetField("_currentHelperType",BindingFlags.Instance|BindingFlags.NonPublic);

    public static Live2DModelSetting LoadSetting(string nativeFile)
    {
        var formal=LocalHelperCostumeTables.LoadModel(nativeFile);
        if(formal!=null)return formal;
        if(Enabled&&UseTrial) {
            string name=null;
            if(string.Equals(nativeFile,"1004A/1004A_05",StringComparison.OrdinalIgnoreCase))name="miko";
            if(string.Equals(nativeFile,"1004B/1004B_05",StringComparison.OrdinalIgnoreCase))name="molly";
            if(string.Equals(nativeFile,"1004C/1004C_05",StringComparison.OrdinalIgnoreCase))name="sophia";
            if(name!=null) {
                var prefab=Resources.Load<GameObject>(LocalHelperCostumeDefinition.ResourceRoot+name);
                if(prefab==null)throw new InvalidOperationException("Missing helper trial asset: "+name);
                return prefab.GetComponent<Live2DModelSetting>();
            }
        }
        return Utils.LoadComponent<Live2DModelSetting>(Global.helperPath+nativeFile);
    }

    public static uint Current(BscHelperHouseDialog house){return (uint)(BscTable.HELPER_TYPE)helperType.GetValue(house);}
    public IEnumerator Select(uint desired)
    {
        switching=true;
        try {
            var house=UnityEngine.Object.FindObjectOfType<BscHelperHouseDialog>();
            if(house==null)house=BscHelperHouseDialog.DoModal();
            for(int i=0;i<3&&Current(house)!=desired;i++) {
                int current=Index(Current(house)),target=Index(desired);
                house.SendMessage(target>current?"OnBtnClickedRight":"OnBtnClickedLeft",SendMessageOptions.RequireReceiver);
                yield return new WaitForSecondsRealtime(.7f);
            }
        }finally{switching=false;}
    }
    static int Index(uint type){return type==3?0:type==1?1:2;}
    private void OnGUI()
    {
        if(!Enabled)return;
        if(previewFont==null)previewFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","SimHei"},17);
        var oldFont=GUI.skin.font;GUI.skin.font=previewFont;
        float scale=Mathf.Clamp(Screen.height/900f,.85f,1.5f);
        var oldMatrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        float width=Screen.width/scale;
        GUILayout.BeginArea(new Rect(width-425,75,405,145),GUI.skin.box);
        GUILayout.Label("青墨·夜樱 · 独立试装预览");
        GUI.enabled=!switching;
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("茉莉",GUILayout.Height(31)))StartCoroutine(Select(1));
        if(GUILayout.Button("米可",GUILayout.Height(31)))StartCoroutine(Select(3));
        if(GUILayout.Button("索菲娅",GUILayout.Height(31)))StartCoroutine(Select(2));
        GUILayout.EndHorizontal();
        if(GUILayout.Button(UseTrial?"查看原服装":"查看夜樱试装",GUILayout.Height(31))) {
            UseTrial=!UseTrial;
            var house=UnityEngine.Object.FindObjectOfType<BscHelperHouseDialog>();
            if(house!=null)house.SendMessage("ChangeHelperInfo",SendMessageOptions.RequireReceiver);
            if(HelperController.Instance!=null)HelperController.Instance.SendMessage("ChangeToMyHelperType",SendMessageOptions.DontRequireReceiver);
        }
        GUI.enabled=true;GUILayout.Label("使用测试存档；可正常点击模型、切换助手。");
        GUILayout.EndArea();GUI.matrix=oldMatrix;GUI.skin.font=oldFont;
    }
    private void OnDestroy(){if(previewFont!=null)UnityEngine.Object.Destroy(previewFont);}
}
