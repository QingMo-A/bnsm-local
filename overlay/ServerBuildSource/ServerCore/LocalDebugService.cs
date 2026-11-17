using System.Text.Json;

/// <summary>Shared desktop/mobile debug actions. Edits use the same save lock as game packets.</summary>
internal sealed class LocalDebugService
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private readonly LocalRuntime runtime;
    public LocalDebugService(LocalRuntime runtime) { this.runtime = runtime; }
    public object Handle(string method, string path, uint? queryUid, string? jsonBody)
    {
        switch(method + " " + path)
        {
            case "GET /debug/bots": return runtime.Router.Bots.Summary();
            case "GET /debug/bots/opponents":
            {
                lock(runtime.Store.Gate)
                {
                    var player=Resolve(runtime,queryUid,out var error);if(player==null)return Failure(error);
                    return new{ok=true,pvpReady=false,opponents=runtime.Router.Bots.Opponents(player.Level).Select(b=>new{uid=b.Uid,name=b.Profile.Nickname,level=b.Profile.Level,strength=b.Strength,aiStrength=b.AiStrength,style=b.Style,revision=b.Revision,tactics=b.Tactics})};
                }
            }
            case "POST /debug/bots/generate":
            {
                var body=Read<BotBody>(jsonBody);if(body==null)return Failure("人机生成请求无效");
                try {runtime.Router.Bots.Add(body.count??0,body.strength??"mixed",runtime.Store.All());return runtime.Router.Bots.Summary();}
                catch(ArgumentException error){return Failure(error.Message);}
            }
            case "POST /debug/bots/configure":
            {
                var body=Read<BotBody>(jsonBody);if(body?.botUid==null)return Failure("缺少人机 UID");
                try {runtime.Router.Bots.Configure(body.botUid.Value,body.strength??"",body.style??"",body.aiStrength);return runtime.Router.Bots.Summary();}
                catch(ArgumentException error){return Failure(error.Message);}
            }
            case "GET /debug/players":
            {
            lock(runtime.Store.Gate) return Json(new {ok=true,players=runtime.Store.All().Select(p=>new {uid=p.Uid,nickname=p.Nickname,account=p.Account,level=p.Level,gold=p.Gold,cash=p.CashPoint,cards=p.CardIds.Count(id=>id!=0)}).ToArray()});
            }
            case "GET /debug/state":
            {
            uint? uid = queryUid;
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,uid,out var error);if(p==null)return Failure(error);
                var cards=new List<object>();
                for(int i=0;i<p.CardIds.Count;i++) {
                    var data=runtime.Catalog.Cards.FirstOrDefault(c=>c.Id==p.CardIds[i]);if(data==null)continue;
                    p.CardStates.TryGetValue((ulong)i+1,out var state);
                    int max=ProgressionRules.CardMax(runtime.Catalog,p,data);
                    cards.Add(new {seq=i+1,id=data.Id,level=state?.Level??1,exp=state?.Exp??0,maxLevel=max,maxExp=ProgressionRules.CardCeiling(runtime.Catalog,max)});
                }
                return Json(new {ok=true,uid=p.Uid,nickname=p.Nickname,flags=LocalDebugFlags.Normalize(p.DebugFlags),level=p.Level,maxLevel=ProgressionRules.PlayerMax(runtime.Catalog),exp=p.Exp,
                    gold=p.Gold,cash=p.CashPoint,vip=runtime.Catalog.VipFor(p.VipExp).VipLv,maxVip=runtime.Catalog.Vip.Max(x=>x.VipLv),vipExp=p.VipExp,
                    skillPoint=p.SkillPoint??0,skillPointMax=runtime.Catalog.SkillPointMax(p),stamina=p.Stamina,staminaMax=runtime.Catalog.StaminaMax(p.Level),challenges=runtime.Router.DebugChallengeCounts(p),cards});
            }
            }
            case "POST /debug/options":
            {
            var body=Read<OptionBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                if(body?.option is not >=0 || body.option>=LocalDebugFlags.OptionCount || body.enabled==null)return Failure("调试开关无效");
                if(p.ActiveStage!=null)return Failure("请先结束当前战斗，再切换养成规则");
                p.DebugFlags=LocalDebugFlags.Change(p.DebugFlags,body.option.Value,body.enabled.Value);runtime.Store.Save();
                return Json(new{ok=true,flags=p.DebugFlags,message="调试规则已保存，取消勾选恢复正常消耗"});
            }
            }
            case "POST /debug/challenges/restore":
            {
            var body=Read<ChallengeBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                error=runtime.Router.DebugRestoreChallenges(p,body?.mode??"");if(error!=null)return Failure(error);
                return Json(new{ok=true,challenges=runtime.Router.DebugChallengeCounts(p),message="次数已恢复，冷却已解除；通关及领奖记录保留，江湖可使用地图上的重置按钮重新开始"});
            }
            }
            case "POST /debug/level":
            {
            var body=Read<LevelBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                if(body?.level is not uint wanted)return Failure("level 缺失或无效");
                if(p.ActiveStage!=null)return Failure("请先结束当前战斗，再修改等级");
                if(p.TrainingSlots.Values.Any(x=>x.CardSequence!=0))return Failure("请先结束或领取修炼，再修改存档等级");
                wanted=Math.Clamp(wanted,1,ProgressionRules.PlayerMax(runtime.Catalog));
                uint old=p.Level;p.Level=wanted;
                // EXP is cumulative, not progress within the current level. Keep valid progress on a no-op.
                uint floor=ProgressionRules.PlayerFloor(runtime.Catalog,wanted);
                uint next=ProgressionRules.PlayerFloor(runtime.Catalog,wanted+1);
                p.Exp=old==wanted && p.Exp>=floor && p.Exp<next?p.Exp:floor;
                p.Stamina=Math.Min(p.Stamina,runtime.Catalog.StaminaMax(p.Level));
                p.SkillPoint=Math.Min(p.SkillPoint??0,runtime.Catalog.SkillPointMax(p));
                runtime.Store.Save();
                return Json(new {ok=true,level=p.Level,previousLevel=old,exp=p.Exp,message=$"存档等级 {old} → {p.Level}（累计经验 {p.Exp}）"});
            }
            }
            case "POST /debug/card-exp":
            {
            var body=Read<CardExpBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                if(body?.seq is not uint seq || seq<1 || seq>p.CardIds.Count || p.CardIds[(int)seq-1]==0)return Failure("卡牌序号无效");
                if(body.exp==null && body.addExp==null)return Failure("exp 或 addExp 缺失");
                if(p.ActiveStage!=null)return Failure("请先结束当前战斗，再修改卡牌");
                if(p.TrainingSlots.Values.Any(x=>x.CardSequence==seq))return Failure("请先结束或领取该卡牌的修炼");
                var data=runtime.Catalog.Cards.FirstOrDefault(c=>c.Id==p.CardIds[(int)seq-1]);if(data==null)return Failure("卡牌数据缺失");
                int max=ProgressionRules.CardMax(runtime.Catalog,p,data);uint ceiling=ProgressionRules.CardCeiling(runtime.Catalog,max);
                if(!p.CardStates.TryGetValue(seq,out var state))p.CardStates[seq]=state=new();
                uint before=state.Exp,old=state.Level;
                // A player-level downgrade must not silently lower an already trained card on an EXP+ click.
                ulong target=body.exp??((ulong)state.Exp+(body.addExp??0));
                if(target>=state.Exp && state.Exp>ceiling)return Failure("该卡已超过当前存档等级的培养上限，请先提升存档等级");
                state.Exp=(uint)Math.Min(target,ceiling);state.Level=ProgressionRules.CardLevel(runtime.Catalog,state.Exp,max);
                runtime.Store.Save();
                return Json(new {ok=true,seq,level=state.Level,previousLevel=old,exp=state.Exp,previousExp=before,maxExp=ceiling,message=$"卡牌 Lv{old} → Lv{state.Level}"});
            }
            }
            case "POST /debug/vip":
            {
            var body=Read<VipBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                if(body==null || (body.level==null && body.addLevels is not >0))return Failure("VIP 等级参数缺失或无效");
                var c=runtime.Catalog;int old=c.VipFor(p.VipExp).VipLv;
                long wanted=body.level??((long)old+body.addLevels!.Value);
                int target=(int)Math.Clamp(wanted,0,c.Vip.Max(x=>x.VipLv));
                var vip=c.Vip.Where(x=>x.VipLv<=target).OrderByDescending(x=>x.VipLv).First();
                // Debug privileges do not fake recharge orders or reset purchased gifts.
                p.VipExp=target==old?p.VipExp:(uint)vip.LvUpNeedVipExp;
                runtime.Store.Save();
                return Json(new {ok=true,vip=vip.VipLv,vipExp=p.VipExp,message=$"V特权 {old} → {vip.VipLv}"});
            }
            }
            case "POST /debug/currency":
            {
            var body=Read<CurrencyBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                if(body?.amount is not >0 || body.currency is not ("gold" or "cash"))return Failure("货币或数量无效");
                uint before=body.currency=="gold"?p.Gold:p.CashPoint;
                ulong target=(ulong)before+body.amount.Value;
                // Native currency labels use Int32; keep debug balances readable and reject atomically.
                if(target>int.MaxValue)return Failure("货币达到客户端可显示上限，本次未增加");
                if(body.currency=="gold")p.Gold=(uint)target;else p.CashPoint=(uint)target;
                runtime.Store.Save();
                return Json(new {ok=true,gold=p.Gold,cash=p.CashPoint,message=(body.currency=="gold"?"金币":"钻石")+" +"+body.amount.Value.ToString("N0")});
            }
            }
            case "POST /debug/skill-point":
            {
            var body=Read<SimpleBody>(jsonBody);
            lock(runtime.Store.Gate) {
                var p=Resolve(runtime,body?.uid,out var error);if(p==null)return Failure(error);
                if(body==null)return Failure("请求无效");
                uint before=p.SkillPoint??0,max=runtime.Catalog.SkillPointMax(p);
                p.SkillPoint=max;p.SkillPointStamp=ProtocolRouter.Now;runtime.Store.Save();
                return Json(new {ok=true,skillPoint=max,skillPointMax=max,previous=before,message=$"武功点数 {before} → {max}"});
            }
            }
            default: return Failure("未知调试操作");
        }
    }
    private static object Failure(string message)=>Json(new {ok=false,message});
    private sealed class ChallengeBody {public uint? uid {get;set;}public string? mode {get;set;}}
    private sealed class SimpleBody {public uint? uid {get;set;}}
    private sealed class OptionBody {public uint? uid {get;set;}public int? option {get;set;}public bool? enabled {get;set;}}
    private sealed class LevelBody {public uint? uid {get;set;}public uint? level {get;set;}}
    private sealed class CardExpBody {public uint? uid {get;set;}public uint? seq {get;set;}public uint? exp {get;set;}public uint? addExp {get;set;}}
    private sealed class VipBody {public uint? uid {get;set;}public int? level {get;set;}public uint? addLevels {get;set;}}
    private sealed class CurrencyBody {public uint? uid {get;set;}public string? currency {get;set;}public uint? amount {get;set;}}
    private static object Json(object value) => value;
    private sealed class BotBody {public int? count {get;set;} public uint? botUid {get;set;} public string? strength {get;set;} public string? aiStrength {get;set;} public string? style {get;set;}}
    private static T? Read<T>(string? jsonBody) where T:class
    {
        try { return JsonSerializer.Deserialize<T>(jsonBody ?? "", Options); }
        catch(JsonException) { return null; }
    }
    private static LocalPlayer? Resolve(LocalRuntime runtime,uint? uid,out string error)
    {
        if(uid is uint id) {var p=runtime.Store.Find(id);error=p==null?"找不到 uid="+id:"";return p;}
        var all=runtime.Store.All();error=all.Count==0?"还没有本地存档":all.Count>1?"存在多个账号，请指定 uid":"";
        return all.Count==1?all[0]:null;
    }
}
