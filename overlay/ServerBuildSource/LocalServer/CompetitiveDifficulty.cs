// Match offers contain immutable template inputs, not references to resident saves.
// Preview, start, castle detail and reconnect therefore rebuild the same legal deck.
internal sealed record LocalBotBuildTuning(uint Level,int Rank,double EnchantFactor,double SkillFactor);
internal sealed class LocalCompetitiveOffer
{
    public uint Uid {get;set;}
    public int Revision {get;set;}
    public int Seed {get;set;}
    public string Name {get;set;}="";
    public string Style {get;set;}="balanced";
    public ulong CreatedAt {get;set;}
    public int Grade {get;set;}
    public uint Badge {get;set;}
    public uint Level {get;set;}
    public uint Rank {get;set;}
    public int LadderVersion {get;set;}
    public int GrowthStage {get;set;}
}
internal sealed class LocalCompetitiveProgress
{
    public uint Battles {get;set;}
    public uint Wins {get;set;}
    public uint LossStreak {get;set;}
    public void Record(bool won){Battles++;if(won){Wins++;LossStreak=0;}else LossStreak++;}
}
internal sealed partial class ProtocolRouter
{
    private static readonly string[] CompetitiveGradeNames=["入门","普通","进阶","高手","宗师"];
    private (uint Battles,uint Wins,uint Rank,uint Losses) CompetitiveProgression(LocalPlayer p,uint mode)
    {
        if(mode is 18 or 44){var r=mode==18?p.Competitive.CastleDifficulty:p.Competitive.WorldDifficulty;return(r.Battles,r.Wins,10000,r.LossStreak);}
        if(mode==43)return(p.Faction.Battles,p.Faction.Wins,10000,p.Faction.LossStreak);
        var record=p.Competitive.Records.GetValueOrDefault(mode)??new();return(record.Battles,record.Wins,record.Rank,record.LossStreak);
    }
    private int CompetitiveBaseGrade(LocalPlayer p,uint mode)
    {
        var r=CompetitiveProgression(p,mode);
        int grade=r.Rank<=1000||r.Wins>=50?3:r.Rank<=5000||r.Wins>=15?2:1;
        // The first five completed matches stay in the introductory bracket.
        if(r.Battles<5)grade=1;
        grade=Math.Min(grade,p.Level<50?1:p.Level<90?2:3);
        return Math.Max(1,grade-(r.Losses>=3?1:0));
    }
    private LocalCompetitiveOffer MakeCompetitiveOffer(LocalPlayer p,LocalBot source,int grade,uint badge)
    {
        grade=Math.Clamp(grade,0,4);
        int offset=new[]{12,6,2,0,0}[grade];
        return new(){Uid=source.Uid,Revision=source.Revision,Seed=source.Seed,Name=source.Profile.Nickname+" · "+CompetitiveGradeNames[grade]+"人机",Style=source.Style,CreatedAt=source.CreatedAt,Grade=grade,Badge=badge,Level=(uint)Math.Max(1,(int)p.Level-offset)};
    }
    private List<LocalCompetitiveOffer> CompetitiveOffers(LocalPlayer p,uint mode)
    {
        if(p.Competitive.Offers.TryGetValue(mode,out var existing)&&existing.Count>0&&(mode!=18||existing.All(x=>x.Level>=35))){AssignCompetitiveRanks(p,mode,existing);return existing;}
        int grade=CompetitiveBaseGrade(p,mode);
        var sources=CompetitiveSources(p,mode);
        var result=sources.Select((source,i)=>MakeCompetitiveOffer(p,source,grade+(i%3==0?1:i%3==2?-1:0),(uint)(i%3))).ToList();
        // With a small configured population, prioritize easy/ordinary offers
        // rather than marking a hard deck as easy or duplicating a UID.
        if(result.Count<3)for(int i=0;i<result.Count;i++)result[i]=MakeCompetitiveOffer(p,sources[i],grade-1+i,(uint)(2-i));
        if(mode==18)foreach(var offer in result)offer.Level=Math.Max(35u,offer.Level);
        AssignCompetitiveRanks(p,mode,result);
        ApplyCompetitiveLadder(p,mode,result);
        p.Competitive.Offers[mode]=result;return result;
    }
    private LocalBot[] CompetitiveSources(LocalPlayer p,uint mode)
    {
        var residents=Bots.All().OrderBy(bot=>bot.Uid).ToArray();
        if(mode is 18 or 44||residents.Length==0)return residents;
        uint count=(uint)residents.Length;
        uint salt=unchecked(p.Uid*2654435761u+mode*2246822519u);
        uint completed=CompetitiveProgression(p,mode).Battles;
        int start=(int)((salt%count+3UL*completed)%count);
        return residents.Skip(start).Concat(residents.Take(start)).Take(9).ToArray();
    }
    private void AssignCompetitiveRanks(LocalPlayer p,uint mode,List<LocalCompetitiveOffer> offers)
    {
        if(mode is not (10 or 12 or 29 or 31 or 42))return;
        var record=PvpRecord(p,mode);var used=new HashSet<uint>(offers.Where(x=>x.Rank>0).Select(x=>x.Rank)){record.Rank};
        foreach(var offer in offers.Where(x=>x.Rank==0)){
            uint seed=unchecked((uint)offer.Seed^offer.Uid*2654435761u^record.Battles*2246822519u^mode*3266489917u^record.Rank);seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;
            // Easier neighbours are close; stronger opponents offer larger jumps.
            uint low=offer.Badge==0?28u:offer.Badge==1?65u:94u,high=offer.Badge==0?43u:offer.Badge==1?80u:100u;
            uint basis=low*100+(seed%((high-low)*100));uint candidate=(uint)Math.Max(1,(ulong)record.Rank*basis/10000);
            candidate=Math.Min(candidate,Math.Max(1,record.Rank-1));
            while(used.Contains(candidate)&&candidate>1)candidate--;
            if(used.Contains(candidate)){candidate=1;while(used.Contains(candidate))candidate++;}
            offer.Rank=candidate;used.Add(candidate);
        }
    }
    private LocalPlayer CompetitiveProfile(LocalCompetitiveOffer offer,uint factionDeck=0)
    {
        string variant=$"v16/{offer.Grade}/{offer.Level}/{offer.Seed}/{offer.Style}/{offer.Name}/{factionDeck}/{offer.LadderVersion}/{offer.GrowthStage}";
        var key=(offer.Uid,offer.Revision,variant);if(competitiveBots.TryGetValue(key,out var saved))return saved;
        int grade=Math.Clamp(offer.Grade,0,4);string strength=grade==0?"casual":grade<=2?"regular":grade==3?"expert":"legend";
        int maxRank=Math.Min((int)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_RANK],catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE));
        var tuning=new LocalBotBuildTuning(offer.Level,new[]{3,5,6,8,maxRank}[grade],new[]{.1,.3,.5,.7,1}[grade],new[]{.3,.5,.7,.85,1}[grade]);
        var builder=new LocalBotBuilder(catalog);
        var bot=builder.Build(offer.Uid,offer.Name,strength,offer.Style,offer.Seed,offer.CreatedAt,strength,factionDeck,tuning);
        int enchantLimit=new[]{0,0,1,3,int.MaxValue}[grade];
        foreach(var pair in bot.Profile.CardStates){
            var state=pair.Value;var card=catalog.RequireCard(bot.Profile.CardIds[(int)pair.Key-1]);
            if(FeatureOpen(bot.Profile,BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_SKILL_ENCHANT)&&state.Enchant>=catalog.Numbers[BscTable.NUMBER_TYPE.SKILL_ENCHANT_LIMIT_CARD_ENCHANT_GRADE])for(int slot=0;slot<Math.Min(7,card.SkillEnchantGroupId.Count);slot++)
                if(catalog.SkillEnchants.TryGetValue(card.SkillEnchantGroupId[slot],out var group))state.SkillEnchants[slot]=(uint)Math.Min(enchantLimit,Math.Min(catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_SKILL_ENCHANT),group.SkillEnchantInfoList.Max(x=>x.EnchantLevel)));
        }
        ApplyCompetitiveGrowth(bot.Profile,offer);
        builder.Validate(bot);if(competitiveBots.Count>=512)competitiveBots.Clear();return competitiveBots[key]=bot.Profile;
    }
    private List<LocalPlayer> CompetitiveOpponents(LocalPlayer p,uint mode)=>CompetitiveOffers(p,mode).Select(x=>CompetitiveProfile(x)).ToList();
    private LocalPlayer? CastleOpponent(LocalPlayer p)
    {
        var c=p.Competitive;if(c.CastleOpponent?.Uid==c.CastleTarget)return CompetitiveProfile(c.CastleOpponent);
        // Old saves have a target UID but no template inputs. Adopt one once.
        var offer=CompetitiveOffers(p,18).FirstOrDefault(x=>x.Uid==c.CastleTarget);
        if(offer==null)return null;c.CastleOpponent=offer;return CompetitiveProfile(offer);
    }
    private LocalPlayer? AutomaticCompetitiveOpponent(LocalPlayer p,uint mode,uint iteration)
    {
        var offers=CompetitiveOffers(p,mode);if(offers.Count==0)return null;
        var progress=CompetitiveProgression(p,mode);
        uint[] cycle=progress.Battles<5?[2u,1u,2u]:[2u,2u,1u,2u,1u,0u];
        uint badge=cycle[iteration%(uint)cycle.Length];var pool=offers.Where(x=>x.Badge==badge).ToArray();
        if(pool.Length==0)pool=offers.ToArray();return CompetitiveProfile(pool[(int)(iteration/(uint)cycle.Length%(uint)pool.Length)]);
    }
}
