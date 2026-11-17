using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using live2d;

public static class LocalHelperCostumeBuild
{
    const string Root="Assets/Resources/helper/LocalNightSakura/";
    public static void Prepare()
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)throw new InvalidOperationException("Native helper icon rendering requires a graphics device; omit -nographics");
        var sheet=new Texture2D(2048,512,TextureFormat.RGBA32,false);
        var fill=new Color32[2048*512];for(int i=0;i<fill.Length;i++)fill[i]=new Color32(17,30,37,255);sheet.SetPixels32(fill);
        try {
            for(int i=0;i<3;i++) {
                string key=LocalHelperCostumeDefinition.Keys[i];
                var setting=AssetDatabase.LoadAssetAtPath<GameObject>(Root+key+".prefab").GetComponent<Live2DModelSetting>();
                var model=Live2DModelUnityCustom.loadModel(setting.modelFile.bytes);model.setRenderMode(0);
                for(int n=0;n<setting.materials.Count;n++)model.setTextureMaterial(n,setting.materials[n]);
                model.update();float w=model.getCanvasWidth(),h=model.getCanvasHeight();
                model.setMatrix(Matrix4x4.TRS(new Vector3(-.5f,h/(2*w),0),Quaternion.identity,new Vector3(1/w,-1/w,1/w)));
                var rt=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);var icon=new Texture2D(512,512,TextureFormat.RGBA32,false);var old=RenderTexture.active;
                try {
                    RenderTexture.active=rt;GL.Clear(true,true,new Color(.045f,.075f,.09f,1));
                    GL.PushMatrix();GL.LoadProjectionMatrix(Matrix4x4.Ortho(-.6f,.6f,-.55f,.65f,-100,100));GL.modelview=Matrix4x4.identity;model.draw();GL.PopMatrix();
                    icon.ReadPixels(new Rect(0,0,512,512),0,0);icon.Apply();
                    sheet.SetPixels32(i*512,0,512,512,icon.GetPixels32());
                    string preview=Path.GetFullPath("../evidence/helper-costumes/"+key+"-icon.png");Directory.CreateDirectory(Path.GetDirectoryName(preview));File.WriteAllBytes(preview,icon.EncodeToPNG());
                }finally{RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(icon);}
            }
            // One collection icon for the shared garment: three native model
            // portraits, without inventing three separate inventory items.
            var pixels=sheet.GetPixels32();
            for(int y=0;y<512;y++)for(int x=0;x<512;x++) {
                int column=Math.Min(2,x*3/512);int local=x-column*512/3;
                int sourceX=column*512+170+local;
                pixels[y*2048+1536+x]=pixels[y*2048+sourceX];
            }
            sheet.SetPixels32(pixels);sheet.Apply();File.WriteAllBytes(Root+"icons.png",sheet.EncodeToPNG());
        }finally{UnityEngine.Object.DestroyImmediate(sheet);}
        AssetDatabase.ImportAsset(Root+"icons.png",ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(Root+"icons.png");importer.mipmapEnabled=false;importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"icons.png");
        foreach(string name in new[]{"icons","icons-gray"}) {
            var shader=Shader.Find(name=="icons"?"Unlit/Transparent Colored":"DTO-DISCOLOR/Grayscale");if(shader==null)throw new InvalidDataException("Missing native icon shader");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+name+".mat");if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,Root+name+".mat");}
            material.shader=shader;material.mainTexture=texture;EditorUtility.SetDirty(material);
        }
        var go=new GameObject(LocalHelperCostumeDefinition.AtlasName);
        try {
            var atlas=go.AddComponent<UIAtlas>();atlas.spriteMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"icons.mat");atlas.spriteList=new List<UIAtlas.Sprite>();
            for(int i=0;i<3;i++)atlas.spriteList.Add(new UIAtlas.Sprite{name=LocalHelperCostumeDefinition.Icon(i),outer=new Rect(i*512,0,512,512),inner=new Rect(i*512,0,512,512)});
            atlas.spriteList.Add(new UIAtlas.Sprite{name="NightSakura_collection",outer=new Rect(1536,0,512,512),inner=new Rect(1536,0,512,512)});
            PrefabUtility.SaveAsPrefabAsset(go,Root+"icons-atlas.prefab");
        }finally{UnityEngine.Object.DestroyImmediate(go);}
        AssetDatabase.SaveAssets();Verify();Debug.Log("HELPER_COSTUME_ICONS_READY");
    }
    public static void Verify()
    {
        var decoded=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try {
            if(!decoded.LoadImage(File.ReadAllBytes(Root+"icons.png")))throw new InvalidDataException("Unreadable helper icon sheet");
            var colors=new HashSet<Color32>(decoded.GetPixels32());
            if(colors.Count<500)throw new InvalidDataException("Helper icons are blank; render with a native graphics device");
        }finally{UnityEngine.Object.DestroyImmediate(decoded);}
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"icons-atlas.prefab");
        if(prefab==null)throw new InvalidDataException("Prepare helper costume icons before building");
        var atlas=prefab.GetComponent<UIAtlas>();
        if(atlas.GetSprite("NightSakura_collection")==null)throw new InvalidDataException("Missing shared outfit icon");
        for(int i=0;i<3;i++) {
            var go=AssetDatabase.LoadAssetAtPath<GameObject>(Root+LocalHelperCostumeDefinition.Keys[i]+".prefab");
            if(go==null||go.GetComponent<Live2DModelSetting>().modelFile==null||atlas.GetSprite(LocalHelperCostumeDefinition.Icon(i))==null)throw new InvalidDataException("Missing helper costume asset "+i);
        }
    }
}
