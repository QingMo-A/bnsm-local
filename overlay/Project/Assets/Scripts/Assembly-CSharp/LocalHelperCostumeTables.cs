using System;
using System.Collections.Generic;
using BscTable;
using UnityEngine;

internal static class LocalHelperCostumeTables
{
    private static readonly Dictionary<int,ItemData> items=new Dictionary<int,ItemData>();
    internal static void Clear(){items.Clear();}
    internal static Live2DModelSetting LoadModel(string modelFile)
    {
        if(!LocalMode.Enabled||modelFile==null||!modelFile.StartsWith("LocalNightSakura/",StringComparison.Ordinal))return null;
        string key=modelFile.Substring("LocalNightSakura/".Length);
        if(Array.IndexOf(LocalHelperCostumeDefinition.Keys,key)<0)throw new InvalidOperationException("Unknown local helper model "+modelFile);
        // Additive models have no original downloaded-bundle address. Bypass
        // that retired lookup and use the exact, validated Resources prefab.
        var prefab=Resources.Load<GameObject>(LocalHelperCostumeDefinition.ResourceRoot+key);
        var setting=prefab==null?null:prefab.GetComponent<Live2DModelSetting>();
        if(setting==null)throw new InvalidOperationException("Missing local helper model "+modelFile);
        return setting;
    }
    internal static ItemData Get(int id,Func<int,ItemData> original)
    {
        ItemData item;
        if(!items.TryGetValue(id,out item)){item=LocalHelperCostumeDefinition.CreateItem(id,original(LocalHelperCostumeDefinition.TemplateId(id)));items.Add(id,item);}
        return item;
    }
    internal static UnityEngine.Object LoadAsset(string path,Type type)
    {
        if(path==null||path.IndexOf(LocalHelperCostumeDefinition.AtlasName,StringComparison.OrdinalIgnoreCase)<0)return null;
        string root=LocalHelperCostumeDefinition.ResourceRoot;
        if(typeof(Material).IsAssignableFrom(type)||path.IndexOf("/materials/",StringComparison.OrdinalIgnoreCase)>=0)return Resources.Load<Material>(root+(path.EndsWith("_Gray",StringComparison.OrdinalIgnoreCase)?"icons-gray":"icons"));
        if(typeof(Texture).IsAssignableFrom(type))return Resources.Load<Texture2D>(root+"icons");
        var go=Resources.Load<GameObject>(root+"icons-atlas");
        if(go==null)return null;
        return typeof(Component).IsAssignableFrom(type)?go.GetComponent(type):(UnityEngine.Object)go;
    }
}
