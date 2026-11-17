using System;
using UnityEngine;

internal static class LocalOriginalCardAssets
{
    // Recovered RawCard shaders decode packed RGB/alpha regions. Our PNG has
    // normal RGBA pixels; preserve the native shader on all other textures.
    internal static Shader PortraitShader(Texture texture,Shader native)
    {
        if(texture==null||(texture.name!="shenyan-crop" && !LocalRegisteredCards.IsRegisteredPortrait(texture.name))||native==null||!native.name.StartsWith("DTO-Raw/RawCard",StringComparison.Ordinal))return native;
        return Shader.Find(native.name.IndexOf("Gray",StringComparison.OrdinalIgnoreCase)>=0?"DTO-DISCOLOR/Grayscale":"Unlit/Transparent Colored")??native;
    }
    internal static UnityEngine.Object Load(string path,Type type)
    {
        if(path==null||path.IndexOf(LocalOriginalCardDefinition.AssetName,StringComparison.OrdinalIgnoreCase)<0)return null;
        string normalized=path.Replace('\\','/').ToLowerInvariant();string asset="";
        if(normalized.Contains("/thumb/materials/"))asset="portrait-material";
        else if(normalized.Contains("/thumb/"))asset=typeof(Texture).IsAssignableFrom(type)?"shenyan-crop":"portrait-atlas";
        else if(normalized.Contains("/crop/"))asset="shenyan-crop";
        else if(normalized.Contains("/detail/")||normalized.Contains("/spcard/")||normalized.Contains("/mcsource/prefabs/"))asset="display";
        if(asset.Length==0)return null;
        var value=Resources.Load("LocalOriginalCard/"+asset);
        if(value==null)return null;
        if(type==typeof(UnityEngine.Object)||type.IsInstanceOfType(value))return value;
        var prefab=value as GameObject;
        return prefab!=null&&typeof(Component).IsAssignableFrom(type)?prefab.GetComponent(type):null;
    }
}
