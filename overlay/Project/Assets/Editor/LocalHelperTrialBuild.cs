using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using live2d;

// Native model renders, not painted mockups. All outputs stay outside Assets.
public static class LocalHelperTrialBuild
{
    const string AssetsRoot="Assets/Resources/helper/LocalNightSakura/";
    static string SourceModelHash(string letter)
    {
        return letter=="A"?"c045081dda75ce2b40e1f91d9a9060ab42fecc7329f58003c6bda04edb58ef3b":letter=="B"?"58bf86a2dd259e6aa4e0f88b5b665bbbf3b7fc6559117af39634b19f11e828b3":"f384743f3f1d77c21f7fefbfda2423c8121ec2909fc692aa0769b584189b9e3e";
    }
    static string Hash(byte[] bytes)
    {
        using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    }
    public static void Prepare()
    {
        // Generated raster paint is constrained to authored clothes UV regions.
        // Every other pixel, and the complete original separate alpha, stay native.
        foreach(string letter in new[]{"A","B","C"}) {
            string key=letter=="A"?"miko":letter=="B"?"molly":"sophia";
            var original=new Texture2D(2,2,TextureFormat.RGBA32,false);
            var paint=new Texture2D(2,2,TextureFormat.RGBA32,false);
            original.LoadImage(File.ReadAllBytes("Assets/Texture2D/1004"+letter+"_a.png"));
            paint.LoadImage(File.ReadAllBytes(Path.GetFullPath("../SourceArt/HelperNightSakura/"+key+"-generated.png")));
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/helper/1004"+letter.ToLowerInvariant()+"/1004"+letter+"_05.prefab");
            var mask=ClothesMask(source.GetComponent<Live2DModelSetting>(),letter);
            var pixels=original.GetPixels32();int changed=0;
            for(int y=0;y<original.height;y++)for(int x=0;x<original.width;x++) {
                int index=(original.height-1-y)*original.width+x;
                Color c=pixels[index];
                if(!mask[y*1024+x])continue;
                Color p=paint.GetPixelBilinear((x+.5f)/original.width,1-(y+.5f)/original.height);
                p.a=c.a;pixels[index]=p;changed++;
            }
            original.SetPixels32(pixels);original.Apply();
            string texturePath=AssetsRoot+key+"-atlas.png";
            File.WriteAllBytes(texturePath,original.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(paint);
            AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.mipmapEnabled=false;importer.maxTextureSize=1024;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var nativeMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/1004"+letter+"_a.mat");
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetsRoot+key+".mat");
            if(material==null){material=new Material(nativeMaterial);AssetDatabase.CreateAsset(material,AssetsRoot+key+".mat");}
            else material.CopyPropertiesFromMaterial(nativeMaterial);
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var go=UnityEngine.Object.Instantiate(source);
            try {
                go.name=key;go.transform.localPosition=Vector3.zero;
                var setting=go.GetComponent<Live2DModelSetting>();setting.materials=new System.Collections.Generic.List<Material>{material};
                PrefabUtility.SaveAsPrefabAsset(go,AssetsRoot+key+".prefab");
            }finally{UnityEngine.Object.DestroyImmediate(go);}
            EditorUtility.SetDirty(material);Debug.Log("HELPER_TRIAL_BAKED "+key+" clothesPixels="+changed);
        }
        AssetDatabase.SaveAssets();
    }

    static bool[] ClothesMask(Live2DModelSetting setting,string letter)
    {
        int[] selected=letter=="A"?new[]{11,12,13,14,15,16,17}:letter=="B"?new[]{7,11,12,13,14,15}:new[]{9,10,11,12,13,16,17};
        var model=Live2DModelUnityCustom.loadModel(setting.modelFile.bytes);model.update();
        if(Hash(setting.modelFile.bytes)!=SourceModelHash(letter))throw new InvalidDataException("Clothes UV mask belongs to a different native model: "+letter);
        var mask=new bool[1024*1024];
        foreach(int index in selected) {
            var data=model.getDrawData(index);
            var method=Array.Find(data.GetType().GetMethods(),m=>m.ReturnType==typeof(float[])&&m.GetParameters().Length==0);
            var uv=(float[])method.Invoke(data,null);var triangles=model.getIndexArray(index);
            for(int i=0;i<triangles.Length;i+=3) {
                var a=new Vector2(uv[triangles[i]*2],uv[triangles[i]*2+1])*1024;
                var b=new Vector2(uv[triangles[i+1]*2],uv[triangles[i+1]*2+1])*1024;
                var c=new Vector2(uv[triangles[i+2]*2],uv[triangles[i+2]*2+1])*1024;
                for(int y=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,b.y,c.y)));y<=Mathf.Min(1023,Mathf.CeilToInt(Mathf.Max(a.y,b.y,c.y)));y++)
                for(int x=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)));x<=Mathf.Min(1023,Mathf.CeilToInt(Mathf.Max(a.x,b.x,c.x)));x++) {
                    var p=new Vector2(x+.5f,y+.5f);float s=Cross(b-a,p-a),t=Cross(c-b,p-b),u=Cross(a-c,p-c);
                    if((s>=0&&t>=0&&u>=0)||(s<=0&&t<=0&&u<=0))mask[y*1024+x]=true;
                }
            }
        }
        // Two native texels of extrusion protect moving edges under bilinear sampling.
        var grown=(bool[])mask.Clone();
        for(int y=2;y<1022;y++)for(int x=2;x<1022;x++)if(mask[y*1024+x])for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)grown[(y+dy)*1024+x+dx]=true;
        return grown;
    }
    static float Cross(Vector2 a,Vector2 b){return a.x*b.y-a.y*b.x;}

    [MenuItem("Local/Helper trial/Validate and export art prototype")]
    public static void Package()
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../evidence/helper-trial"));Directory.CreateDirectory(root);
        var checks=new System.Collections.Generic.List<object>();
        foreach(string letter in new[]{"A","B","C"}) {
            string key=letter=="A"?"miko":letter=="B"?"molly":"sophia";
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/helper/1004"+letter.ToLowerInvariant()+"/1004"+letter+"_05.prefab").GetComponent<Live2DModelSetting>();
            var trial=AssetDatabase.LoadAssetAtPath<GameObject>(AssetsRoot+key+".prefab").GetComponent<Live2DModelSetting>();
            bool native=source.modelFile==trial.modelFile&&source.physicsFile==trial.physicsFile&&source.poseFile==trial.poseFile&&source.motionGroupList.Count==trial.motionGroupList.Count;
            int motions=0;
            for(int g=0;g<source.motionGroupList.Count;g++) {
                var a=source.motionGroupList[g];var b=trial.motionGroupList[g];
                native&=a.motionType==b.motionType&&a.motions.Length==b.motions.Length;
                for(int j=0;j<a.motions.Length;j++){native&=a.motions[j].motionFile==b.motions[j].motionFile&&Live2DLoader.LoadAssetsMotion(b.motions[j].motionFile)!=null;motions++;}
            }
            native&=trial.materials.Count==1&&trial.materials[0].shader==source.materials[0].shader&&trial.materials[0].GetTexture("_SubTex")==source.materials[0].GetTexture("_SubTex");
            var original=new Texture2D(2,2);var painted=new Texture2D(2,2);
            original.LoadImage(File.ReadAllBytes("Assets/Texture2D/1004"+letter+"_a.png"));painted.LoadImage(File.ReadAllBytes(AssetsRoot+key+"-atlas.png"));
            var aPixels=original.GetPixels32();var bPixels=painted.GetPixels32();var mask=ClothesMask(source,letter);
            int outsideChanges=0,changed=0,alphaChanges=0;
            for(int y=0;y<1024;y++)for(int x=0;x<1024;x++) {
                int i=(1023-y)*1024+x;bool difference=!aPixels[i].Equals(bPixels[i]);
                if(difference){changed++;if(!mask[y*1024+x])outsideChanges++;}if(aPixels[i].a!=bPixels[i].a)alphaChanges++;
            }
            UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(painted);
            bool passed=native&&outsideChanges==0&&alphaChanges==0&&changed>10000;
            var row=new {helper=key,passed,nativeModelPhysicsMotionsShaderAndAlphaPreserved=native,motionFilesDecoded=motions,changedClothesPixels=changed,changedPixelsOutsideClothesMask=outsideChanges,changedAlphaPixels=alphaChanges,sourceModelSha256=Hash(source.modelFile.bytes),atlasSha256=Hash(File.ReadAllBytes(AssetsRoot+key+"-atlas.png"))};
            checks.Add(row);if(!passed)throw new InvalidDataException("Helper trial verification failed: "+key);
        }
        File.WriteAllText(Path.Combine(root,"native-asset-validation.json"),LitJson.JsonMapper.ToJson(new {passed=true,scope="editor-native-art-prototype",utc=DateTime.UtcNow.ToString("o"),checks,gameWardrobeAndSaveIntegration=false,formalPackagesReplaced=false,deviceValidated=false}));
        AssetDatabase.ExportPackage(new[]{AssetsRoot.TrimEnd('/'),"Assets/Editor/LocalHelperTrialBuild.cs"},Path.Combine(root,"HelperNightSakura.unitypackage"),ExportPackageOptions.Recurse);
        Debug.Log("HELPER_TRIAL_PACKAGE_OK "+root);
    }

    public static void Trials()
    {
        Prepare();
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../evidence/helper-trial/renders"));Directory.CreateDirectory(root);
        foreach(string key in new[]{"miko","molly","sophia"}) {
            var setting=AssetDatabase.LoadAssetAtPath<GameObject>(AssetsRoot+key+".prefab").GetComponent<Live2DModelSetting>();
            Render(setting,Path.Combine(root,key+".png"),0);
            Render(setting,Path.Combine(root,key+"-left.png"),-25);
            Render(setting,Path.Combine(root,key+"-right.png"),25);
        }
        Debug.Log("HELPER_TRIAL_RENDER_OK "+root);
    }
    public static void Originals()
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../evidence/helper-trial/originals"));
        Directory.CreateDirectory(root);
        foreach(string helper in new[]{"A","B","C"})foreach(string outfit in new[]{"00","03","05"}) {
            string id="1004"+helper+"_"+outfit;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/helper/1004"+helper.ToLowerInvariant()+"/"+id+".prefab");
            Render(prefab.GetComponent<Live2DModelSetting>(),Path.Combine(root,id+".png"),0);
        }
        Debug.Log("HELPER_ORIGINAL_RENDER_OK "+root);
    }

    public static void Showcase()
    {
        Trials();
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../evidence/helper-trial/renders"));
        string[] keys={"molly","miko","sophia"};
        var collection=new Texture2D(768*3,1024,TextureFormat.RGBA32,false);
        try {
            for(int k=0;k<keys.Length;k++) {
                string key=keys[k];var t=new Texture2D(2,2);t.LoadImage(File.ReadAllBytes(Path.Combine(root,key+".png")));
                collection.SetPixels32(k*768,0,768,1024,t.GetPixels32());UnityEngine.Object.DestroyImmediate(t);
                string frames=Path.Combine(root,key+"-frames");Directory.CreateDirectory(frames);
                var setting=AssetDatabase.LoadAssetAtPath<GameObject>(AssetsRoot+key+".prefab").GetComponent<Live2DModelSetting>();
                for(int i=0;i<48;i++){float phase=i/48f*Mathf.PI*2;Render(setting,Path.Combine(frames,i.ToString("D3")+".png"),Mathf.Sin(phase)*25,phase);}
            }
            collection.Apply();File.WriteAllBytes(Path.Combine(root,"collection.png"),collection.EncodeToPNG());
        }finally{UnityEngine.Object.DestroyImmediate(collection);}
        Debug.Log("HELPER_TRIAL_SHOWCASE_OK "+root);
    }

    public static void Render(Live2DModelSetting setting,string path,float angle,float phase=-1)
    {
        var cameraGo=new GameObject("HelperTrialRenderCamera");
        var camera=cameraGo.AddComponent<Camera>();
        var rt=new RenderTexture(768,1024,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(768,1024,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;
        try {
            cameraGo.transform.position=new Vector3(0,0,-10);camera.orthographic=true;
            camera.orthographicSize=.72f;camera.aspect=.75f;camera.cullingMask=1<<30;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f,1);
            camera.targetTexture=rt;
            var model=Live2DModelUnityCustom.loadModel(setting.modelFile.bytes);
            model.setRenderMode(0);
            for(int i=0;i<setting.materials.Count;i++)model.setTextureMaterial(i,setting.materials[i]);
            model.setParamFloat("PARAM_ANGLE_X",angle);
            if(phase>=0){
                model.setParamFloat("PARAM_BODY_ANGLE_X",Mathf.Sin(phase)*6);
                model.setParamFloat("PARAM_ANGLE_Y",Mathf.Cos(phase)*5);
                model.setParamFloat("PARAM_BREATH",.5f+.4f*Mathf.Sin(phase*2));
                float eye=1-Mathf.Pow(Mathf.Max(0,Mathf.Cos(phase-1)),32);
                model.setParamFloat("PARAM_EYE_L_OPEN",eye);model.setParamFloat("PARAM_EYE_R_OPEN",eye);
                model.setParamFloat("PARAM_MOUTH_OPEN_Y",Mathf.Max(0,Mathf.Sin(phase*2))*.35f);
            }
            model.update();
            if(model.getErrorFlags()!=0)throw new InvalidDataException("Native model error: "+path);
            float w=model.getCanvasWidth(),h=model.getCanvasHeight();
            model.setMatrix(Matrix4x4.TRS(new Vector3(-.5f,h/(2*w),0),Quaternion.identity,new Vector3(1/w,-1/w,1/w)));
            RenderTexture.active=rt;GL.Clear(true,true,camera.backgroundColor);
            GL.PushMatrix();GL.LoadProjectionMatrix(Matrix4x4.Ortho(-.54f,.54f,-.72f,.72f,-100,100));GL.modelview=Matrix4x4.identity;
            model.draw();GL.PopMatrix();
            image.ReadPixels(new Rect(0,0,768,1024),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());
        }finally {
            RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);
            camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(cameraGo);
        }
    }
}
