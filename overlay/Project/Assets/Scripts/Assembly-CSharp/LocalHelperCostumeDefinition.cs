#if BNSM_SERVER
extern alias game;
#endif
using System;
using System.IO;
using ProtoBuf;

/// <summary>Shared additive local outfit definitions; original catalog bytes remain untouched.</summary>
public static class LocalHelperCostumeDefinition
{
    public const int FirstItemId=1900030001;
    public const int TemplateItemId=220003004;
    public const int MaxEnchant=10;
    public const int EventId=900011;
    public const string AtlasName="LocalNightSakuraIcons";
    public const string ResourceRoot="helper/LocalNightSakura/";
    public static readonly int[] Helpers={1,3,2};
    public static readonly string[] Keys={"molly","miko","sophia"};
    public static readonly string[] Names={"茉莉","米可","索菲娅"};
    public static bool IsItem(int id){return id>=FirstItemId&&id<=FirstItemId+MaxEnchant;}
    public static int TemplateId(int id){if(!IsItem(id))throw new ArgumentOutOfRangeException("id");return TemplateItemId+(id-FirstItemId)*100;}
    private static int Remap(int id){int step=(id-TemplateItemId)/100;return id>=TemplateItemId&&id<=TemplateItemId+MaxEnchant*100&&(id-TemplateItemId)%100==0?FirstItemId+step:id;}
    public static string Icon(int index){return "NightSakura_"+Keys[index];}
    public static string ModelFile(int index){return "LocalNightSakura/"+Keys[index];}
    public static BscTable.ItemData CreateItem(int id,BscTable.ItemData source)
    {
        if(!IsItem(id)||source==null||source.Id!=TemplateId(id)||source.EnchantValue!=id-FirstItemId)throw new InvalidDataException("Missing helper outfit enchant template");
        BscTable.ItemData item;
        using(var s=new MemoryStream()){Serializer.Serialize(s,source);s.Position=0;item=Serializer.Deserialize<BscTable.ItemData>(s);}
        int level=id-FirstItemId;item.Id=id;item.IdAlias="LOCAL_HELPER_NIGHT_SAKURA_"+level;
        item.NameAlias="青墨·夜樱"+(level==0?string.Empty:" +"+level);
        item.DescAlias="三位助手共用，各显对应款式。\n活动「夜樱初绽」获取，最高强化 +10。";
        item.Icon="NightSakura_collection";item.Atlas=AtlasName;item.Thumbnail=string.Empty;item.Prefabs=string.Empty;
        item.WearableHelperType.Clear();item.WearableHelperType.Add(BscTable.HELPER_TYPE.MAX_HELER_TYPE);
        item.Sell=0;item.Value=0;item.Next1Id=Remap(item.Next1Id);item.Next2Id=Remap(item.Next2Id);item.AwakningNextId=Remap(item.AwakningNextId);
        for(int n=0;n<item.NextId.Count;n++)item.NextId[n]=Remap(item.NextId[n]);
        for(int n=0;n<item.Material1Id.Count;n++)item.Material1Id[n]=Remap(item.Material1Id[n]);
        for(int n=0;n<item.Material1IdAlias.Count;n++)if(item.Material1Id[n]==FirstItemId)item.Material1IdAlias[n]="LOCAL_HELPER_NIGHT_SAKURA_0";
        item.Next1IdAlias=item.Next1Id==0?string.Empty:"LOCAL_HELPER_NIGHT_SAKURA_"+(level+1);
        item.EvolveFirstAncestor=FirstItemId;item.ClothResetItemId=Remap(item.ClothResetItemId);
        item.ClothResetItemIdAlias=item.ClothResetItemId==0?string.Empty:"LOCAL_HELPER_NIGHT_SAKURA_0";
        item.Source.Clear();item.ShortCutAuto=false;return item;
    }
    public static void AddModels(BscTable.HelperData helper)
    {
        for(int i=0;i<3;i++)if((int)helper.HelperType==Helpers[i]) {
            for(int level=0;level<=MaxEnchant;level++) {
                int id=FirstItemId+level;var existing=helper.FindModel(id);
                if(existing!=null){if(existing.ModelFile!=ModelFile(i))throw new InvalidDataException("Helper outfit model ID collision");continue;}
                helper.ModelList.Add(new BscTable.HelperModel{ModelId=id,ModelIdAlias="LOCAL_HELPER_NIGHT_SAKURA_"+level,ModelFile=ModelFile(i)});
            }
        }
    }
}
