#if BNSM_SERVER
extern alias game;
using CardData=game::BscTable.CardData;
using SkillData=game::BscTable.SkillData;
#else
using BscTable;
#endif
using System;
using System.IO;
using ProtoBuf;

/// <summary>Shared immutable definition for the opt-in original-card prototype.</summary>
public static class LocalOriginalCardDefinition
{
    public const int CardId=1900000001;
    public const int SourceCardId=20088;
    public const int FirstSkillId=1900000101;
    public const string Name="听雨·沈砚";
    public const string AssetName="LocalOriginalShenYan";
    public const string AtlasName="LocalOriginalShenYanAtlas";
    public static bool IsCard(int id){return id==CardId;}
    public static bool IsSkill(int id){return id==FirstSkillId||id==FirstSkillId+1;}
    private static T Copy<T>(T original)
    {
        using(var stream=new MemoryStream()){Serializer.Serialize(stream,original);stream.Position=0;return Serializer.Deserialize<T>(stream);}
    }
    public static CardData CreateCard(CardData source)
    {
        if(source==null||source.Id!=SourceCardId||source.ActiveSkillId.Count!=2)throw new InvalidDataException("Original sword template unavailable");
        var card=Copy(source);
        card.Id=CardId;card.IdAlias="LOCAL_ORIGINAL_SHENYAN";card.NameAlias=Name;card.SortNameAlias=Name;
        card.DescAlias="原创试制剑士。听风辨雨，守住同伴身前的一线。";
        card.MemoAlias="试制：独立卡牌与动态卡面，战斗动作、技能机制沿用原剑士模板。";
        card.MaxRank=card.Rank;card.RankUpCardId=0;card.EvolveFirstAncestor=CardId;
        card.CardMini=card.CardSmall=card.CardBig=card.Character=card.CardBattle=AssetName;
        card.CardMiniAtlas=card.CardBattleAtlas=AtlasName;
        card.Illustrator="原创试制 · AI辅助绘制";
        // Keep the working original 3D rig/actions for this first production trial.
        card.ActiveSkillId[0]=FirstSkillId;card.ActiveSkillId[1]=FirstSkillId+1;
        card.ActiveSkillIdAlias.Clear();card.ActiveSkillIdAlias.Add("听雨剑势");card.ActiveSkillIdAlias.Add("流风断雨");
        card.SkillEnchantGroupId.Clear();card.SkillEnchantGroupId.AddRange(new int[7]);
        return card;
    }
    public static SkillData CreateSkill(SkillData original,int index)
    {
        if(original==null||index<0||index>1)throw new InvalidDataException("Original skill template unavailable");
        var skill=Copy(original);skill.Id=FirstSkillId+index;
        skill.NameAlias=index==0?"听雨剑势":"流风断雨";
        // Existing native effect/animation logic is deliberately reused.
        return skill;
    }
}
