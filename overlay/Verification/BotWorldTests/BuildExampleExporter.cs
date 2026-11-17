extern alias game;
using ProtoBuf;
using System.Text.Json;

internal static class BuildExampleExporter
{
    internal static Func<string,string> Labels(string root)
    {
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LocalServer/data/tables/manifest.json")));
        string table=manifest.RootElement.EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN")).GetProperty("name").GetString()!;
        using var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables",table+".pb"));
        var texts=Serializer.Deserialize<BscTable.LocalTextDataTable>(input);texts.MakeMap();
        return alias=>texts.GetItem(alias)?.Text??alias;
    }
    internal static void Write(string root,GameCatalog catalog,IReadOnlyList<LocalBot> bots,string destination)
    {
        var Text=Labels(root);
        object Item(uint id)
        {
            var item=catalog.ItemsById[(int)id];
            return new{id,name=Text(item.NameAlias),item.Step,item.Level,item.Rank,effects=item.AttributeValueIdType.Select((type,i)=>new{type=type.ToString(),value=i<item.AttributeValueId.Count?item.AttributeValueId[i]:0,unit=i<item.AttributeUnit.Count?item.AttributeUnit[i].ToString():""})};
        }
        var examples=bots.Where(b=>b.Strength is "legend" or "expert").Select(b=>new
        {
            uid=b.Uid,name=b.Profile.Nickname,strength=LocalBotBuilder.TierName(b.Strength),style=LocalBotBuilder.StyleName(b.Style),
            cards=b.Profile.Deck.Select(seq=>
            {
                var state=b.Profile.CardStates[seq];var card=catalog.Cards.Single(c=>c.Id==b.Profile.CardIds[(int)seq-1]);
                return new{slot=seq,name=Text(card.NameAlias),card.Id,job=card.Class.ToString(),card.Rank,talent=card.Talent.ToString(),state.Level,state.Enchant,
                    gear=new[]{state.WeaponSequence}.Concat(state.AccessorySequences).Where(x=>x!=0).Select(x=>Item(b.Profile.EquipmentItems.Single(e=>e.Sequence==x).ItemId)),gems=state.GemIds.Select(Item)};
            })
        });
        File.WriteAllText(destination,JsonSerializer.Serialize(new{source="Original published CardData/ItemData/zh_CN text tables",selection="Native progression stage, equipment eligibility, attack type, slot role and style-weighted attributes",combatEffectivenessVerified=false,examples},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
    }
}
