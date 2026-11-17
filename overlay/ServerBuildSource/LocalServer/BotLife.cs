extern alias game;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBuf;

/// <summary>
/// 人机在服务端“活过一段时间”的结果。资产变化直接落在 bot-world.json 上，
/// 高光事件交给世界频道播报。这里不造假数据：抽到哪张卡就是池子里真有的那张。
/// </summary>
internal sealed class BotLifeEvent
{
    public ulong Time {get;set;}
    public uint Actor {get;set;}
    public string Name {get;set;}="";
    public uint Level {get;set;}
    public uint AvatarId {get;set;}
    /// <summary>draw / farm / idle</summary>
    public string Kind {get;set;}="draw";
    /// <summary>去掉 CARD_TALENT_ 前缀的资质名：C / B / A / S / SS / SSS</summary>
    public string Talent {get;set;}="";
    public uint CardId {get;set;}
    public string CardName {get;set;}="";
    /// <summary>可以直接发到世界频道的一句。</summary>
    public string Text {get;set;}="";
}

/// <summary>日活节奏与卡池口径。放在 LocalServer/data/bot-life.json，缺文件就用这里的默认值。</summary>
internal sealed class BotLifeSettings
{
    public bool Enabled {get;set;}=true;
    /// <summary>
    /// 两次日活之间隔多久（秒）。真实玩家不会每分钟都在动，但也不能太慢：
    /// 12 个居民按 45~150 秒一次轮下来，每个人大约 15~30 分钟动一次，像一群常在线的人。
    /// </summary>
    public int MinimumPauseSeconds {get;set;}=45;
    public int MaximumPauseSeconds {get;set;}=150;
    public int FarmGoldMin {get;set;}=5000;
    public int FarmGoldMax {get;set;}=14000;
    /// <summary>推图顺带拿到钻石的概率（活动/签到奖励），没有钻石就永远抽不到高资质。</summary>
    public int FarmCashChance {get;set;}=55;
    public int FarmCashMin {get;set;}=150;
    public int FarmCashMax {get;set;}=350;
    public int GoldDrawCost {get;set;}=28000;
    public int CashDrawCost {get;set;}=300;
    /// <summary>手上有钱时，先抽钻石池的概率。</summary>
    public int CashDrawChance {get;set;}=20;
    /// <summary>手上有钱时，抽金币池的概率。</summary>
    public int GoldDrawChance {get;set;}=45;
    /// <summary>
    /// 金币池的资质份额。原版金币池只有 C/B/A。
    /// </summary>
    public Dictionary<string,int> GoldTalent {get;set;}=new(){{"C",48},{"B",34},{"A",18}};
    /// <summary>
    /// 钻石池的资质份额。卡牌最高资质就是 SS，没有更高的档位
    /// （原先设计里那 2% 的 X 是哨兵值，不是可收集卡，这里并入 SS）。
    /// </summary>
    public Dictionary<string,int> CashTalent {get;set;}=new(){{"A",48},{"S",35},{"SS",17}};
    /// <summary>
    /// 哪几个资质档值得占一条世界频道。卡牌最高资质是 SS，所以默认只播 SS——
    /// S 在钻石池占 35%，也播就会满屏。想更热闹就往里加 "S"。
    /// </summary>
    public List<string> Broadcast {get;set;}=["SS"];
    /// <summary>卡册上限：抽到的卡只进卡册，不动卡组（卡组恒为 5 张）。</summary>
    public int MaxCardBook {get;set;}=40;
    public static BotLifeSettings Load(string root)
    {
        string path=Path.Combine(root,"LocalServer","data","bot-life.json");
        if(!File.Exists(path))return new BotLifeSettings();
        try
        {
            var loaded=JsonSerializer.Deserialize<BotLifeSettings>(File.ReadAllText(path),
                new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
            if(loaded==null)return new BotLifeSettings();
            if(loaded.MinimumPauseSeconds<10)loaded.MinimumPauseSeconds=10;
            if(loaded.MaximumPauseSeconds<loaded.MinimumPauseSeconds)loaded.MaximumPauseSeconds=loaded.MinimumPauseSeconds;
            return loaded;
        }
        catch(Exception) { return new BotLifeSettings(); }
    }
}

/// <summary>
/// 人机日活：让居民在服务端自己推图、攒钱、抽卡，并把结果写回存档。
/// 没有这一层，“抽奖通知”就只能靠编，一查就和他的卡组对不上。
/// </summary>
internal sealed partial class LocalBotWorld
{
    private const int MaxLifeEvents=48;
    private BotLifeSettings? lifeSettings;
    private GachaRules? drawRules;
    private CardNameIndex? cardNames;
    private BotLifeSettings Life => lifeSettings ??= BotLifeSettings.Load(rootDirectory);
    private GachaRules DrawRules => drawRules ??= GachaRules.Load(rootDirectory);
    private CardNameIndex CardNames => cardNames ??= new CardNameIndex(catalog,rootDirectory);

    /// <summary>
    /// 卡名解析。卡表里只有 NameAlias（形如 TEXT.Card.EVENT_BIWAL），
    /// 中文名在 TextLocalDataTable_zh_CN 里，而运行时表目录用哈希命名，
    /// 所以按 manifest 里的 id 定位。任何一步失败都返回空串，
    /// 调用方退化成不带卡名的台词——宁可少一个名字，也不能把键名发到世界频道。
    /// </summary>
    private sealed class CardNameIndex
    {
        private readonly Dictionary<int,string> names=new();
        public CardNameIndex(GameCatalog catalog,string root)
        {
            try
            {
                string dir=Path.Combine(root,"LocalServer","data","tables");
                string manifestPath=Path.Combine(dir,"manifest.json");
                if(!File.Exists(manifestPath))return;
                int wanted=game::CUtil.GenerateHashKey("TextLocalDataTable_zh_CN");
                using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));
                string? file=manifest.RootElement.EnumerateArray()
                    .Where(x=>x.GetProperty("id").GetInt32()==wanted)
                    .Select(x=>x.GetProperty("name").GetString())
                    .FirstOrDefault();
                if(file==null)return;
                string tablePath=Path.Combine(dir,file+".pb");
                if(!File.Exists(tablePath))return;
                using var stream=File.OpenRead(tablePath);
                var table=Serializer.Deserialize<BscTable.LocalTextDataTable>(stream);
                table.MakeMap();
                foreach(var card in catalog.Cards)
                {
                    if(card.Id<=0||string.IsNullOrWhiteSpace(card.NameAlias))continue;
                    string text=table.GetItem(card.NameAlias)?.Text??"";
                    if(!string.IsNullOrWhiteSpace(text))names[card.Id]=text;
                }
            }
            catch(Exception) { names.Clear(); }
        }
        public string Of(int cardId)=>names.GetValueOrDefault(cardId,"");
    }

    /// <summary>
    /// 到点就让一个人机动一次。返回非 null 表示产生了值得发到世界频道的事件。
    /// 由后台循环驱动；同一次调用内完成状态修改与落盘。
    /// </summary>
    internal BotLifeEvent? Advance(ulong now)
    {
        lock(gate)
        {
            if(!Life.Enabled || world.Bots.Count==0)return null;
            if(world.NextLifeAt==0) {world.NextLifeAt=now+(ulong)LifePause();Save();return null;}
            if(now<world.NextLifeAt)return null;
            BotLifeEvent? seen=null;
            var bot=PickResident();
            if(bot!=null)seen=Act(bot,now);
            if(seen!=null)
            {
                world.Life.Add(seen);
                if(world.Life.Count>MaxLifeEvents)world.Life.RemoveRange(0,world.Life.Count-MaxLifeEvents);
            }
            world.NextLifeAt=now+(ulong)LifePause();
            Save();
            return seen;
        }
    }

    /// <summary>最近的高光事件，最新的排在最后。</summary>
    internal IReadOnlyList<BotLifeEvent> RecentEvents()
    {
        lock(gate)return world.Life.ToArray();
    }

    private int LifePause()
    {
        var s=Life;
        int min=Math.Max(10,s.MinimumPauseSeconds),max=Math.Max(min,s.MaximumPauseSeconds);
        return RandomNumberGenerator.GetInt32(min,max+1);
    }

    /// <summary>挑最久没活动的那个人，避免同一个人连着刷屏。</summary>
    private LocalBot? PickResident()
    {
        if(world.Bots.Count==0)return null;
        return world.Bots
            .OrderBy(x=>x.LastLifeAt)
            .ThenBy(_=>RandomNumberGenerator.GetInt32(4096))
            .First();
    }

    private BotLifeEvent? Act(LocalBot bot,ulong now)
    {
        bot.LastLifeAt=now;
        var s=Life;
        bool canCash=bot.Profile.CashPoint>=(uint)s.CashDrawCost && s.CashDrawCost>0;
        bool canGold=bot.Profile.Gold>=(uint)s.GoldDrawCost && s.GoldDrawCost>0;
        int roll=RandomNumberGenerator.GetInt32(100);
        if(canCash && roll<s.CashDrawChance)return Draw(bot,now,true);
        if(canGold && roll<s.CashDrawChance+s.GoldDrawChance)return Draw(bot,now,false);
        Farm(bot);
        return null;
    }

    /// <summary>推图：涨金币，偶尔有钻石。这条是账本的另一半，没有它就抽不了第二次。</summary>
    private void Farm(LocalBot bot)
    {
        var s=Life;
        bot.Profile.Gold=SaturatedAdd(bot.Profile.Gold,(uint)Roll(s.FarmGoldMin,s.FarmGoldMax));
        if(RandomNumberGenerator.GetInt32(100)<s.FarmCashChance)
            bot.Profile.CashPoint=SaturatedAdd(bot.Profile.CashPoint,(uint)Roll(s.FarmCashMin,s.FarmCashMax));
    }

    private static int Roll(int min,int max)
    {
        if(max<=min)return min;
        return RandomNumberGenerator.GetInt32(min,max+1);
    }

    private static uint SaturatedAdd(uint current,uint delta)=>current>uint.MaxValue-delta?uint.MaxValue:current+delta;

    private static string TalentName(BscTable.CARD_TALENT talent)=>talent.ToString().Replace("CARD_TALENT_","");

    private BotLifeEvent? Draw(LocalBot bot,ulong now,bool cash)
    {
        var s=Life;
        var pool=catalog.DrawPool(cash,DrawRules.CardRank);
        if(pool.Length==0)return null;
        var card=PickByTalent(pool,cash?s.CashTalent:s.GoldTalent);
        if(card==null)return null;
        uint cost=(uint)(cash?s.CashDrawCost:s.GoldDrawCost);
        string talent=TalentName(card.Talent);
        bool duplicate=Owns(bot,card);
        bool full=bot.Profile.CardIds.Count>=s.MaxCardBook;
        bool hasPiece=catalog.Items.Any(x=>x.Id==card.PieceId&&x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE);
        if((duplicate||full)&&!hasPiece)return null;
        if(cash)bot.Profile.CashPoint-=cost;else bot.Profile.Gold-=cost;
        if(duplicate||full)
        {
            // 重复卡转碎片，和玩家抽卡的口径一致。
            if(catalog.Items.Any(x=>x.Id==card.PieceId && x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_PIECE))
            {
                int count=DrawRules.PieceCount(card.Talent);
                uint seed=(uint)card.PieceId;
                bot.Profile.Inventory[seed]=bot.Profile.Inventory.GetValueOrDefault(seed)+count;
            }
        }
        else if(bot.Profile.CardIds.Count<s.MaxCardBook && card.Id>0)
        {
            // 新卡只进卡册，不进卡组；满级是 1 级，正如真人刚抽到的样子。
            bot.Profile.CardIds.Add((uint)card.Id);
            bot.Profile.CardStates[(ulong)bot.Profile.CardIds.Count]=new LocalCardState();
            bot.Profile.CollectedCardIds.Add((uint)card.Id);
        }
        // 播报门槛显式配置，不靠权重算术推——金币池最高档是 A，钻石池最高档是 SS，
        // 两者权重太接近，用阈值会误判。
        if(!s.Broadcast.Contains(talent))return null;
        string resolved=CardNames.Of(card.Id);
        string text=full&&!duplicate?"卡册满了，这次抽到的"+(resolved.Length>0?resolved:talent+"卡")+"先转成碎片存着。":DrawLine(talent,resolved,duplicate);
        return new BotLifeEvent
        {
            Time=now,Actor=bot.Uid,Name=bot.Profile.Nickname,Level=bot.Profile.Level,
            AvatarId=bot.Profile.CardIds.Count>0?bot.Profile.CardIds[0]:(uint)card.Id,
            Kind="draw",Talent=talent,CardId=(uint)card.Id,CardName=resolved,Text=text
        };
    }

    /// <summary>按资质份额在池子里加权——均匀抽会让 SSS 泛滥，世界频道就没有分量了。</summary>
    private static BscTable.CardData? PickByTalent(BscTable.CardData[] pool,Dictionary<string,int> shares)
    {
        var buckets=pool
            .GroupBy(c=>TalentName(c.Talent))
            .Select(g=>(Key:g.Key,Cards:g.ToArray(),Weight:shares.TryGetValue(g.Key,out int w)&&w>0?w:0))
            .Where(b=>b.Weight>0)
            .ToArray();
        if(buckets.Length==0)return pool[RandomNumberGenerator.GetInt32(pool.Length)];
        int total=0;foreach(var b in buckets)total+=b.Weight;
        int pick=RandomNumberGenerator.GetInt32(total);
        foreach(var b in buckets)
        {
            if(pick<b.Weight)return b.Cards[RandomNumberGenerator.GetInt32(b.Cards.Length)];
            pick-=b.Weight;
        }
        var last=buckets[^1];
        return last.Cards[RandomNumberGenerator.GetInt32(last.Cards.Length)];
    }

    private bool Owns(LocalBot bot,BscTable.CardData card)
    {
        int identity=ProtocolRouter.HeroIdentity(card);
        foreach(uint id in bot.Profile.CardIds)
        {
            if(id==0)continue;
            var owned=catalog.FindCard(id);
            if(owned!=null && ProtocolRouter.HeroIdentity(owned)==identity)return true;
        }
        return false;
    }

    /// <summary>{0}=卡名 {1}=资质。资质不写死，不同池子的稀有档不一样。</summary>
    private static readonly string[] NamedLines=[
        "抽到 {0}（{1}），这波不亏",
        "{0} 是 {1}，先锁上再说",
        "磨了半天才出 {0}，{1} 也算出货了",
    ];
    /// <summary>拿不到卡名时的退路：照样是真人语气，但不会带出未翻译的键名。</summary>
    private static readonly string[] AnonLines=[
        "抽到一张 {0}，这波不亏",
        "磨了半天才出一张 {0}，也算出货了",
        "刚出一张 {0}，先锁了",
    ];
    private static string DrawLine(string talent,string cardName,bool duplicate)
    {
        bool named=!string.IsNullOrWhiteSpace(cardName)&&!cardName.StartsWith("TEXT.",StringComparison.Ordinal);
        var table=named?NamedLines:AnonLines;
        string line=table[RandomNumberGenerator.GetInt32(table.Length)];
        string text=named?string.Format(line,cardName,talent):string.Format(line,talent);
        if(duplicate)text+="（又一张，转碎片了）";
        return text.Length>100?text[..100]:text;
    }
}
