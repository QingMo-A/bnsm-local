using System.Text;

/// <summary>Conservative offline dialogue intents, not a general-purpose language model.</summary>
internal static class WorldChatDialogue
{
    internal sealed record Decision(string Group,string Topic,int Chance=100,bool Quiet=false,bool ClearContext=false);
    internal static string Normalize(string text) {
        var b=new StringBuilder();foreach(char c in text.ToLowerInvariant())if(!char.IsPunctuation(c) && !char.IsWhiteSpace(c) && !char.IsSymbol(c))b.Append(c);
        return b.ToString();
    }
    private static bool Has(string text,params string[] words)=>words.Any(w=>text.Contains(w,StringComparison.Ordinal));
    internal static Decision Analyze(string text,string previous="",string lastGroup="") {
        // Explicit corrections replace the old clause rather than matching its negated keyword.
        foreach(string marker in new[]{"我问的是","我说的是","而是","，是",",是"}) {
            int correction=text.LastIndexOf(marker,StringComparison.Ordinal);
            if(correction>=0) {text=text[(correction+marker.Length)..];break;}
        }
        string t=Normalize(text);
        Decision Reply(string group,string topic,int chance=100)=>new(group,topic,chance);
        if(Has(t,"别刷屏","别说话","别回了","别回复","安静点","闭嘴","少说点","暂停聊天"))return new("","",Quiet:true);
        if(Has(t,"不聊这个","别提这个","不是问","没问","换个话题","不想聊","别说抽卡","别聊","不聊","别提","不想听"))return new("","",ClearContext:true);
        if(Has(t,"你是机器人","你是人机","你是ai","是不是ai","真人还是","是脚本吗"))return Reply("identity","social");
        bool question=Has(t,"怎么","如何","什么","为啥","是啥","会不会","是不是","怎样","哪种","想问","问下","请教","区别","为何","能否","为什么","咋","哪里","在哪","多少","能不能","有没有","请问","谁知道","有人知道","求教","求助","救命","帮忙") || text.Contains('?') || text.Contains('？');
        if(previous=="stage" && t is "前排" or "扛不住啊" or "坦克倒了")return Reply("stage.defence","stage");
        if(previous=="stage" && t is "输出" or "打太慢" or "时间到了")return Reply("stage.damage","stage");
        if(previous=="skill" && t is "金币不够" or "没金币了" or "缺金币")return Reply("skill.gold","skill");
        if(previous=="skill" && t is "点数不够" or "没点数" or "点数没了")return Reply("skill.points","skill");
        if(previous=="draw" && t is "重复了" or "又是他" or "又重复了")return Reply("draw.duplicate","draw");
        if(previous=="stage" && !Has(t,"怎么","区别","为什么") && t.Length<=16 && (t.Contains("关") || t.Contains("层")))return Reply("stage.detail","stage");
        if(previous=="social" && Has(t,"推图","闯关","打关"))return Reply("social.stage","social");
        if(previous=="social" && Has(t,"养卡","刷材料","清背包"))return Reply("social.growth","social");
        if(Has(t,"扛不住","站不住","被秒","前排太脆"))return Reply("stage.defence","stage");
        if(Has(t,"刮痧","伤害不够","输出不够","打不死","打到超时","时间不够"))return Reply("stage.damage","stage");
        if(previous=="stage" && Has(t,"还是不行","还是没过","还是打不过"))return Reply("stage.detail","stage");
        if(Has(t,"99","满级","上限") && Has(t,"等级","升级","级了"))return Reply("growth.cap","growth");
        if(Has(t,"打不过","卡关","过不去","打不动") || (t.Contains("卡在") && Has(t,"关","塔","层","boss")))return Reply("stage.ask","stage");
        if(Has(t,"终于过了","终于打过","终于通关","过关了"))return Reply("stage.win","stage",65);
        if(Has(t,"宝石") && Has(t,"合成","合不了","怎么弄"))return Reply("gem","gem");
        if(Has(t,"材料") && Has(t,"合成","低级","下级","不够"))return Reply("materials","materials");
        if(Has(t,"刻印","精炼","装备觉醒","装备分解"))return Reply("equipment","equipment",question?100:45);
        if(Has(t,"升级","升星") && t.Contains("升级") && t.Contains("升星") && question)return Reply("growth","growth");
        if(previous=="skill" && lastGroup=="skill.unlock" && !question && Has(t,"星","超越","没解锁"))return Reply("skill.unlock","skill");
        if(Has(t,"武功","技能","技能点")) {
            if(Has(t,"报错","爆红","错误"))return Reply("skill.error","skill");
            if(Has(t,"解锁","二技能","第二个","第二技能","不能点","点不了"))return Reply("skill.unlock","skill");
            if(Has(t,"不够","没有","用完","恢复","几点","多久") && !Has(t,"金币"))return Reply("skill.points","skill");
            if(Has(t,"金币","太贵","花钱"))return Reply("skill.gold","skill");
            return Reply("skill","skill",question?100:45);
        }
        if(Has(t,"升星","碎片召唤","碎片合成","召唤卡牌"))return Reply("star","star");
        if(previous=="star" && Has(t,"碎片","怎么合","在哪","然后呢"))return Reply("star","star");
        if(Has(t,"超越"))return Reply("transcend","transcend",question?100:50);
        if(Has(t,"强化","突破","觉醒"))return Reply("growth","growth",question?100:45);
        if(Has(t,"重复卡","重复角色","已经有的卡") || (Has(t,"抽","十连") && Has(t,"重复","又是","已经有","同一张")))return Reply("draw.duplicate","draw");
        if(Has(t,"ss","资质") && Has(t,"星","绿色","紫色","蓝色"))return Reply("rarity","draw");
        bool drawTopic=previous=="draw" || Has(t,"抽卡","十连","单抽","出货","抽到");
        if(drawTopic && question && Has(t,"概率","保底"))return Reply("draw.rules","draw");
        if(drawTopic && Has(t,"全是材料","全出材料","没出","不出","非酋","歪了"))return Reply("draw.badluck","draw");
        if(drawTopic && Has(t,"终于抽到","出了ss","出ss","出货了"))return Reply("draw.good","draw");
        if(Has(t,"抽到了","我抽到") && !question)return Reply("draw.report","draw",80);
        if(Has(t,"抽卡","十连","单抽","出货","抽到"))return Reply(question?"draw.ask":"draw.result","draw",question?100:65);
        if(Has(t,"金币","缺钱","没钱"))return Reply("gold","gold",question||Has(t,"不够","没了","花光","缺")?100:40);
        if(Has(t,"体力","包子"))return Reply("stamina","stamina",question||Has(t,"不够","没了","用完")?100:40);
        if(Has(t,"商店","买材料","买道具"))return Reply("shop","shop",question?100:40);
        if(t is "在吗" or "有人" or "在不" or "嗨" or "hi")return Reply("greeting","social");
        if(Has(t,"你好","哈喽","嗨大家","晚上好","早上好","有人吗","有人在吗","有人没","都在干嘛","在不在","hello"))return Reply("greeting","social");
        if(Has(t,"谢谢","谢了","多谢") && previous.Length>0)return Reply("thanks","social",50);
        if(previous.Length>0 && Has(t,"然后呢","具体呢","怎么操作","在哪弄"))return Reply(previous switch {"stage"=>"stage.detail","draw"=>"draw.ask","star"=>"star","skill"=>"skill",_=>previous},previous);
        if(previous=="shop" && lastGroup=="shop" && !question && t.Length is >0 and <25)return Reply("shop.find","shop");
        return new("","");
    }
}
