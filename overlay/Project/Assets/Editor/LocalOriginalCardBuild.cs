using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class LocalOriginalCardBuild
{
    public static void Prepare()
    {
        const string root="Assets/Resources/LocalOriginalCard/";
        foreach(string name in new[]{"shenyan-full.png","shenyan-crop.png"}){
            var importer=AssetImporter.GetAtPath(root+name) as TextureImporter;
            if(importer==null)throw new InvalidDataException("Missing original illustration: "+name);
            importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=2048;importer.isReadable=false;importer.SaveAndReimport();
        }
        var shader=Shader.Find("Local/OriginalCardLive2D");if(shader==null)throw new InvalidDataException("Missing original-card shader");
        var full=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"shenyan-full.png");
        var crop=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"shenyan-crop.png");
        var material=AssetDatabase.LoadAssetAtPath<Material>(root+"display-material.mat");
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,root+"display-material.mat");}
        material.shader=shader;material.mainTexture=full;
        var portrait=AssetDatabase.LoadAssetAtPath<Material>(root+"portrait-material.mat");
        if(portrait==null){portrait=new Material(Shader.Find("Unlit/Transparent Colored"));AssetDatabase.CreateAsset(portrait,root+"portrait-material.mat");}
        portrait.mainTexture=crop;
        var go=new GameObject("LocalOriginalShenYan");
        try{
            var setting=go.AddComponent<Live2DModelSetting>();
            setting.layout=new Live2DModelSetting.Layout{width=5f/3f,height=2.5f,center=Vector2.zero};
            setting.modelFile=AssetDatabase.LoadAssetAtPath<TextAsset>(root+"shenyan.moc.bytes");
            if(setting.modelFile==null)throw new InvalidDataException("Missing generated legacy moc");
            setting.materials=new List<Material>{material};setting.motionGroupList=new List<Live2DModelSetting.MotionGroup>();
            setting.additionalBreath=new List<Live2DModelSetting.AdditionalBreath>();
            PrefabUtility.SaveAsPrefabAsset(go,root+"display.prefab");
        }finally{UnityEngine.Object.DestroyImmediate(go);}
        var atlasGo=new GameObject("LocalOriginalShenYanAtlas");
        try{
            var atlas=atlasGo.AddComponent<UIAtlas>();atlas.spriteMaterial=portrait;
            // Native NGUI sprite metadata crops the head from the illustrated
            // portrait without modifying or regenerating its raster source.
            var rect=new Rect(crop.width*.35f,crop.height*.03f,crop.width*.40f,crop.height*.40f);
            atlas.spriteList=new List<UIAtlas.Sprite>{new UIAtlas.Sprite{name=LocalOriginalCardDefinition.AssetName,outer=rect,inner=rect}};
            PrefabUtility.SaveAsPrefabAsset(atlasGo,root+"portrait-atlas.prefab");
        }finally{UnityEngine.Object.DestroyImmediate(atlasGo);}
        EditorUtility.SetDirty(material);EditorUtility.SetDirty(portrait);AssetDatabase.SaveAssets();
        Debug.Log("ORIGINAL_CARD_ASSETS_PREPARED: authored legacy Live2D model and native UI atlas");
    }
    public static void Windows(){Prepare();LocalCompactBuild.Windows();}
}
