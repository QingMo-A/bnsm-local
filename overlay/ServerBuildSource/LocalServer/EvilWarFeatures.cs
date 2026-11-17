using System.Security.Cryptography;
using System.Text;

internal sealed class LocalEvilWar
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,System.Text.Json.JsonElement>? ExtraFields {get;set;}
    public uint BestStage {get;set;}
    public uint Stage {get;set;}
    public uint Resets {get;set;}
    public List<uint> Slots {get;set;}=[1,1,1,1,1];
    public Dictionary<uint,uint> Runes {get;set;}=[];
    public uint Candidate {get;set;}
    public double EvilStone {get;set;}
    public double HolyStone {get;set;}
    public ulong AccountedAt {get;set;}
    public string ActiveSession {get;set;}="";
}

// Formula names and rounding follow the recovered 1.97 client EvilWar.cs.
// Server-only drop/offline policy is documented separately in docs/EvilWar.md.
internal sealed class EvilWarRules(GameCatalog catalog)
{
    public const uint MaxLevel=99999;
    public const double MaxNumber=9.999e80;
    public double N(BscTable.EVIL_WAR_DATA_TYPE alias)=>catalog.EvilWarNumbers.Single(x=>x.Alias==alias).Num;
    public double Curve(int first,double index)=>Math.Min(MaxNumber,Math.Truncate(N((BscTable.EVIL_WAR_DATA_TYPE)first)*Math.Pow(N((BscTable.EVIL_WAR_DATA_TYPE)(first+1)),index)+Math.Pow(index,N((BscTable.EVIL_WAR_DATA_TYPE)(first+2)))));
    public static double RoundCost(double value) {
        if(value<1000)return Math.Truncate(value);
        double unit=Math.Pow(10,Math.Floor(Math.Log10(value)/3)*3-1);
        return Math.Ceiling(value/unit)*unit;
    }
    public double SlotCost(uint level)=>Curve(11,level-1);
    public double RuneCost(uint level)=>Math.Min(MaxNumber,RoundCost(Curve(17,level-1)));
    public double GachaCost(int owned)=>Curve(14,owned);
    public double EvilDrop(uint wave,LocalEvilWar e) {
        double bonus=e.Runes.Sum(x=>catalog.EvilWarRunes.Single(r=>r.Id==x.Key).EffectType==BscTable.EVIL_WAR_RUNE_EFFECT_TYPE.EVIL_WAR_RUNE_EFFECT_EVIL_STONE_BONUS ? x.Value*catalog.EvilWarRunes.Single(r=>r.Id==x.Key).EffectValue:0);
        return Math.Min(MaxNumber,Math.Truncate(Curve(20,wave-1)*(1+bonus/100)));
    }
    public double HolyDrop(uint wave)=>Curve(23,wave-1);
    public double ResetReward(uint wave)=>Curve(29,wave-1);
    public double Power(LocalEvilWar e) => e.Slots.Select((lv,i)=>Math.Min(MaxNumber,Math.Truncate(Curve(8,lv-1)*(1+e.Runes.GetValueOrDefault((uint)i+1)*catalog.EvilWarRunes.Single(x=>x.Id==i+1).EffectValue/100)))).Sum();
    public double Offline(LocalEvilWar e,ulong now) {
        if(e.AccountedAt==0||now<=e.AccountedAt)return 0;
        double seconds=Math.Min(now-e.AccountedAt,N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_ABSENCE_REWARD_MAX_SECOND));
        uint wave=Math.Max(1,e.Stage); // Continue farming the saved floor; do not advance while offline.
        double hp=Curve(4,wave-1)*(wave%10==0?N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_HP_BOSS_BOUNS):1);
        return Math.Min(MaxNumber,Math.Floor(seconds/N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_ABSENCE_ATTACK_ROTATION_TIME)*Power(e)/hp*EvilDrop(wave,e)*N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_ABSENCE_REWARD_RATIO)));
    }
    public static PKLargeInt Pack(double number) {
        number=Math.Clamp(number,0,MaxNumber);
        uint exponent=number<1e9?0:(uint)Math.Max(0,Math.Floor(Math.Log10(number))-8);
        return new(){@base=(uint)Math.Floor(number/Math.Pow(10,exponent)),exponent=exponent};
    }
}

internal sealed partial class ProtocolRouter
{
    private Reply? HandleEvilWar(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(!name.StartsWith("GS_EVIL_WAR_",StringComparison.Ordinal))return null;
        string sessionHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.loginSessionKey)));
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionHash}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.EvilWarReceipts.TryGetValue(key,out var receipt))
            return receipt.Fingerprint==fingerprint?new(receipt.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(receipt.TypeName,true),Convert.FromBase64String(receipt.Payload)),receipt.Name):fail("infinite challenge sequence reused",ServerErrorCode.ERROR_INVALID_EVIL_WAR_REQUEST);
        if(p.Level<catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_EVIL_WAR))return fail("infinite challenge unlock level not reached",ServerErrorCode.ERROR_INVALID_EVIL_WAR_REQUEST);
        var e=p.EvilWar;var rules=new EvilWarRules(catalog);
        PKEvilWarRecordData Record() {
            var result=new PKEvilWarRecordData{bestStageId=e.BestStage,currentStageId=e.Stage,resetCount=e.Resets,candidateRuneId=e.Candidate,evilStone=EvilWarRules.Pack(e.EvilStone),holyStone=EvilWarRules.Pack(e.HolyStone),moonStone=p.Moonstone};
            result.slotLevelVec.AddRange(e.Slots);result.runeDataVec.AddRange(e.Runes.OrderBy(x=>x.Key).Select(x=>new PKEvilWarRuneData{runeId=x.Key,level=x.Value}));return result;
        }
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_EVIL_WAR_REQUEST);
        bool SameSlots(List<uint> slots)=>slots.Count==5&&slots.SequenceEqual(e.Slots);
        if(e.Slots.Count!=5||e.Slots.Any(x=>x is 0 or >EvilWarRules.MaxLevel)||!double.IsFinite(e.EvilStone)||!double.IsFinite(e.HolyStone))return Invalid("invalid saved infinite challenge state");
        if(name!="GS_EVIL_WAR_ENTER_REQUEST"&&e.ActiveSession!=sessionHash)return Invalid("enter infinite challenge first");
        Reply reply;
        switch(name) {
        case "GS_EVIL_WAR_ENTER_REQUEST":
            if(p.ActiveStage!=null)return Invalid("finish the adventure battle first");
            double absent=rules.Offline(e,Now);e.EvilStone=Math.Min(EvilWarRules.MaxNumber,e.EvilStone+absent);e.ActiveSession=sessionHash;
            reply=ok(new GsEvilWarEnterOk{record=Record(),earnedEvilStone=EvilWarRules.Pack(absent)});break;
        case "GS_EVIL_WAR_EXIT_REQUEST": {
            var r=(GsEvilWarExitRequest)request!;
            if(r.currentStageId!=e.Stage||!SameSlots(r.slotLevelVec))return Invalid("exit does not match saved progress");
            e.ActiveSession="";reply=ok(new GsEvilWarExitOk{record=Record()});break;
        }
        case "GS_EVIL_WAR_BATTLE_END_REQUEST": {
            var r=(GsEvilWarBattleEndRequest)request!;
            if(!SameSlots(r.slotLevelVec)||r.currentStageId==0||r.currentStageId>EvilWarRules.MaxLevel||
               !(r.currentStageId==e.Stage+1||(r.currentStageId==e.Stage&&e.Stage%10==9)))return Invalid("expected the next cleared wave or pre-boss farming wave");
            uint wave=r.currentStageId;bool first=wave>e.BestStage;
            double evil=rules.EvilDrop(wave,e),holy=wave%10==0?rules.HolyDrop(wave):0;
            uint moon=wave%10==0&&(first||Random.Shared.NextDouble()*100<Math.Clamp(rules.N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_MOON_STONE_DROP_PROB)*(wave/10),rules.N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_MOON_STONE_DROP_PROB_MIN),rules.N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_MOON_STONE_DROP_PROB_MAX)))?(uint)rules.N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_MOON_STONE_DROP_BASE):0;
            if((ulong)p.Moonstone+moon>int.MaxValue)return Invalid("moonstone wallet is full");
            e.EvilStone=Math.Min(EvilWarRules.MaxNumber,e.EvilStone+evil);e.HolyStone=Math.Min(EvilWarRules.MaxNumber,e.HolyStone+holy);p.Moonstone+=moon;e.Stage=wave;e.BestStage=Math.Max(e.BestStage,wave);
            reply=ok(new GsEvilWarBattleEndOk{record=Record(),earnedEvilStone=EvilWarRules.Pack(evil),earnedHolyStone=EvilWarRules.Pack(holy),earnedMoonStone=moon});break;
        }
        case "GS_EVIL_WAR_SLOT_ENCHANT_REQUEST": {
            var r=(GsEvilWarSlotEnchantRequest)request!;
            if(r.currentStageId!=e.Stage||r.slotLevelVec.Count!=5||r.slotLevelVec.Any(x=>x is 0 or >EvilWarRules.MaxLevel))return Invalid("invalid power levels");
            var changed=Enumerable.Range(0,5).Where(i=>r.slotLevelVec[i]!=e.Slots[i]).ToArray();
            if(changed.Length!=1)return Invalid("upgrade exactly one character at a time");int slot=changed[0];long count=(long)r.slotLevelVec[slot]-e.Slots[slot];
            if(count is <1 or >100)return Invalid("expected between 1 and 100 power upgrades");
            double cost=EvilWarRules.RoundCost(Enumerable.Range(0,(int)count).Sum(i=>rules.SlotCost(e.Slots[slot]+(uint)i)));
            if(e.EvilStone<cost)return fail("not enough purple crystals",ServerErrorCode.ERROR_NOT_ENOUGH_EVIL_STONE);
            e.EvilStone-=cost;e.Slots[slot]=r.slotLevelVec[slot];reply=ok(new GsEvilWarSlotEnchantOk{record=Record()});break;
        }
        case "GS_EVIL_WAR_RUNE_GACHA_REQUEST": {
            var available=catalog.EvilWarRunes.Where(x=>!e.Runes.ContainsKey((uint)x.Id)).ToArray();
            if(available.Length==0)return Invalid("all bagua runes are already owned");
            double cost=rules.GachaCost(e.Runes.Count);if(e.HolyStone<cost)return fail("not enough blue sapphires",ServerErrorCode.ERROR_NOT_ENOUGH_HOLY_STONE);
            e.HolyStone-=cost;e.Candidate=(uint)available[Random.Shared.Next(available.Length)].Id;reply=ok(new GsEvilWarRuneGachaOk{record=Record()});break;
        }
        case "GS_EVIL_WAR_RUNE_ENCHANT_REQUEST": {
            var r=(GsEvilWarRuneEnchantRequest)request!;
            if(r.currentStageId!=e.Stage||!catalog.EvilWarRunes.Any(x=>x.Id==r.runeId))return Invalid("invalid rune or wave");
            if(e.Runes.TryGetValue(r.runeId,out uint level)) {
                if(level>=EvilWarRules.MaxLevel)return Invalid("rune reached maximum level");double cost=rules.RuneCost(level);
                if(e.HolyStone<cost)return fail("not enough blue sapphires",ServerErrorCode.ERROR_NOT_ENOUGH_HOLY_STONE);e.HolyStone-=cost;e.Runes[r.runeId]=level+1;
            } else {
                if(e.Candidate!=r.runeId)return Invalid("only the rolled candidate may be accepted");e.Runes[r.runeId]=1;e.Candidate=0;
            }
            reply=ok(new GsEvilWarRuneEnchantOk{record=Record()});break;
        }
        case "GS_EVIL_WAR_RESET_REQUEST": {
            var r=(GsEvilWarResetRequest)request!;
            if(r.currentStageId!=e.Stage||e.Stage<rules.N(BscTable.EVIL_WAR_DATA_TYPE.EVIL_WAR_DATA_RESETABLE_WAVE)||e.Resets==uint.MaxValue)return Invalid("clear 100 waves before rebirth");
            e.HolyStone=Math.Min(EvilWarRules.MaxNumber,e.HolyStone+rules.ResetReward(e.Stage));e.EvilStone=0;e.Stage=0;e.Slots=[1,1,1,1,1];e.Resets++;
            reply=ok(new GsEvilWarResetOk{record=Record()});break;
        }
        default:return Invalid("unknown infinite challenge operation");
        }
        e.AccountedAt=Now;
        if(p.EvilWarReceipts.Count>=256)p.EvilWarReceipts.Remove(p.EvilWarReceipts.Keys.First());
        p.EvilWarReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(reply.Message))};store.Save();return reply;
    }
}
