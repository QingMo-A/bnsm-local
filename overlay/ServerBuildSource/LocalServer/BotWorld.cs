using System.Security.Cryptography;
using System.Text.Json;

internal sealed class LocalBot
{
    public uint Uid {get;set;}
    public int Seed {get;set;}
    public string Strength {get;set;}="regular";
    public string AiStrength {get;set;}="regular";
    public string Style {get;set;}="balanced";
    /// <summary>聊天人格：这个人愿意接的话题集合。旧存档没有这个字段，加载时按 uid 补齐。</summary>
    public List<string> Topics {get;set;}=[];
    public int Revision {get;set;}=1;
    public ulong CreatedAt {get;set;}
    /// <summary>上一次日活时刻，用来让轮转不总是挑到同一个人。</summary>
    public ulong LastLifeAt {get;set;}
    public LocalBotTactics Tactics {get;set;}=new();
    public LocalPlayer Profile {get;set;}=new();
}

/// <summary>Policy contract for the future PvP executor; it does not fabricate matches or rewards.</summary>
internal sealed class LocalBotTactics
{
    public int ReactionMilliseconds {get;set;}=1000;
    public double DecisionAccuracy {get;set;}=.7;
    public string TargetPriority {get;set;}="front";
    public bool ReserveInterrupt {get;set;}
    public bool ChainControl {get;set;}
    public double HealAtHpRatio {get;set;}=.4;
}

internal sealed class LocalBotWorldSettings
{
    public int DesiredPopulation {get;set;}=12;
    public string Distribution {get;set;}="mixed";
    public bool AutomaticPopulation {get;set;}=true;
    public bool AllowFutureBotChallenges {get;set;}
}

/// <summary>Persistent residents, separate from human saves and saved only when the roster changes.</summary>
internal sealed partial class LocalBotWorld
{
    private sealed class World
    {
        public int Schema {get;set;}=1;
        public uint NextUid {get;set;}=910001;
        public LocalBotWorldSettings Settings {get;set;}=new();
        public List<LocalBot> Bots {get;set;}=[];
        public List<BotManagementRequest> ManagementRequests {get;set;}=[];
        public List<BotManagementResult> ManagementResults {get;set;}=[];
        /// <summary>下一次日活时刻；0 表示还没排过。</summary>
        public ulong NextLifeAt {get;set;}
        /// <summary>最近的日活事件，供世界频道消费。</summary>
        public List<BotLifeEvent> Life {get;set;}=[];
    }
    private readonly object gate=new();
    private readonly string path;
    private readonly string rootDirectory;
    private readonly GameCatalog catalog;
    private readonly World world;
    private readonly LocalBotBuilder builder;
    /// <summary>ambient-chat.json 里配的初始班子（uid / 昵称 / 聊天人格）。</summary>
    private readonly List<(uint Uid,string Name,List<string> Topics)> cast;
    internal const int MaximumPopulation=100;
    internal static readonly string[] Strengths=["casual","regular","expert","legend"];
    internal static readonly string[] Styles=["balanced","burst","control","sustain","counter"];
    private static readonly string[] FirstNames=["听雨","北辰","云中","青岚","渡江","照夜","归鸿","寒川","长风","临渊","望舒","星河","沧浪","逐月","飞雪","秋水"];
    private static readonly string[] LastNames=["客","剑","行","影","游","舟","歌","尘","问道","无痕","如故","知秋"];
    /// <summary>
    /// 随机补位人机的人格包。八个包合起来覆盖 WorldChatDialogue 产出的全部话题，
    /// 保证任何一个可问的话题都有人接得住。
    /// </summary>
    private static readonly string[][] Personas=[
        ["social","stamina","shop","stage"],
        ["draw","rarity","social","gold"],
        ["stage","equipment","gem","skill"],
        ["skill","gold","materials","growth"],
        ["star","growth","transcend","draw"],
        ["draw","shop","social","materials"],
        ["stage","equipment","gold","stamina"],
        ["gem","materials","skill","social"],
    ];
    public LocalBotWorld(string root,string directory,GameCatalog catalog,IReadOnlyList<LocalPlayer> humans)
    {
        this.catalog=catalog;builder=new(catalog);path=Path.Combine(directory,"bot-world.json");
        rootDirectory=root;
        cast=LoadCast(root);
        world=File.Exists(path)?JsonSerializer.Deserialize<World>(File.ReadAllText(path))??throw new InvalidDataException("Empty bot world"):new();
        if(world.Schema!=1 || world.Bots.Count>MaximumPopulation || world.Bots.Select(x=>x.Uid).Distinct().Count()!=world.Bots.Count ||
            world.Bots.Any(x=>x.Uid<900000 || humans.Any(h=>h.Uid==x.Uid) || x.Profile.Uid!=x.Uid))throw new InvalidDataException("Invalid saved bot identities");
        bool changed=false;
        // 旧存档没有 Topics：先补人格再做校验，否则校验会看到半初始化的花名册。
        foreach(var bot in world.Bots)if(bot.Topics.Count==0) {bot.Topics=TopicsFor(bot.Uid);changed=true;}
        foreach(var bot in world.Bots)builder.Validate(bot);
        if(world.Bots.Count==0)
        {
            // Keep old contact UIDs so friend links and chat history still refer to the same resident.
            foreach(var actor in cast)
            {
                if(humans.Any(h=>h.Uid==actor.Uid))continue;
                world.Bots.Add(Create(actor.Uid,actor.Name,world.Bots.Count,topics:actor.Topics));changed=true;
            }
        }
        if(world.Settings.AutomaticPopulation && world.Bots.Count<world.Settings.DesiredPopulation)
        {
            AddCore(world.Settings.DesiredPopulation-world.Bots.Count,humans);changed=true;
        }
        changed|=ApplyManagementRequests(humans);
        if(changed)Save();
    }
    /// <summary>配置读不到时也要能起服，内置同一套六人组兜底。</summary>
    private static List<(uint Uid,string Name,List<string> Topics)> LoadCast(string root)
    {
        try
        {
            var settings=JsonSerializer.Deserialize<AmbientWorldChat.Settings>(File.ReadAllText(Path.Combine(root,"LocalServer/data/ambient-chat.json")));
            var list=settings?.Actors.Where(a=>a.Uid>=900000 && !string.IsNullOrWhiteSpace(a.Name))
                .Select(a=>(a.Uid,a.Name,a.Topics??new List<string>())).ToList();
            if(list is {Count:>=2})return list;
        }
        catch(Exception) { }
        return [
            (900001u,"晚风",new List<string>{"social","stamina","shop"}),
            (900002u,"鱼丸",new List<string>{"draw","social","rarity"}),
            (900003u,"阿渡",new List<string>{"stage","equipment","gem"}),
            (900004u,"青团",new List<string>{"skill","gold","materials"}),
            (900005u,"山雨",new List<string>{"star","growth","transcend"}),
            (900006u,"橘子汽水",new List<string>{"draw","social","shop"}),
        ];
    }
    /// <summary>配置里点名的人用配置，其余按 uid 稳定地分一个包。</summary>
    private List<string> TopicsFor(uint uid)
    {
        foreach(var (castUid,_,topics) in cast)if(castUid==uid && topics.Count>0)return new List<string>(topics);
        return PersonaFor(uid);
    }
    private static List<string> PersonaFor(uint uid)=>[..Personas[(int)(uid%Personas.Length)]];

    public IReadOnlyList<LocalBot> All(){lock(gate)return world.Bots.ToArray();}
    public LocalBot? Find(uint uid){lock(gate)return world.Bots.FirstOrDefault(x=>x.Uid==uid);}
    public LocalBotWorldSettings Settings {get {lock(gate)return new(){DesiredPopulation=world.Settings.DesiredPopulation,Distribution=world.Settings.Distribution,AutomaticPopulation=world.Settings.AutomaticPopulation,AllowFutureBotChallenges=world.Settings.AllowFutureBotChallenges};}}
    public IReadOnlyList<AmbientWorldChat.Actor> Actors()=>All().Select(b=>new AmbientWorldChat.Actor{Uid=b.Uid,Name=b.Profile.Nickname,Level=b.Profile.Level,CardId=b.Profile.CardIds[0],
        Topics=b.Topics.Count>0?b.Topics:PersonaFor(b.Uid)}).ToArray();
    private LocalBot Create(uint uid,string name,int ordinal,string? distribution=null,List<string>? topics=null)
    {
        string tier=(distribution??world.Settings.Distribution) switch{"casual"=>"casual","regular"=>"regular","expert"=>"expert","legend"=>"legend",_=>Strengths[new[]{0,0,1,1,2,0,1,2,1,0,3,2}[ordinal%12]]};
        int seed=RandomNumberGenerator.GetInt32(int.MaxValue);
        var bot=builder.Build(uid,name,tier,Styles[ordinal%Styles.Length],seed,ProtocolRouter.Now);
        bot.Topics=topics is {Count:>0}?new List<string>(topics):PersonaFor(uid);
        return bot;
    }
    private void AddCore(int count,IReadOnlyList<LocalPlayer> humans,string? distribution=null)
    {
        for(int i=0;i<count;i++)
        {
            while(humans.Any(h=>h.Uid==world.NextUid)||world.Bots.Any(b=>b.Uid==world.NextUid))world.NextUid=checked(world.NextUid+1);
            uint id=world.NextUid;world.NextUid=checked(world.NextUid+1);
            string name=FirstNames[RandomNumberGenerator.GetInt32(FirstNames.Length)]+LastNames[RandomNumberGenerator.GetInt32(LastNames.Length)];
            if(world.Bots.Any(b=>b.Profile.Nickname==name))name+="·"+id.ToString()[^3..];
            world.Bots.Add(Create(id,name,world.Bots.Count,distribution));
        }
    }
    public void Add(int count,string distribution,IReadOnlyList<LocalPlayer> humans)
    {
        if(count is <1 or >MaximumPopulation || distribution!="mixed"&&!Strengths.Contains(distribution))throw new ArgumentException("人机数量或强度无效");
        lock(gate)
        {
            if(world.Bots.Count+count>MaximumPopulation)throw new ArgumentException("人机总数最多 100 个");
            int previousCount=world.Bots.Count,previousDesired=world.Settings.DesiredPopulation;uint previousUid=world.NextUid;
            try {AddCore(count,humans,distribution);world.Settings.DesiredPopulation=world.Bots.Count;Save();}
            catch {world.Bots.RemoveRange(previousCount,world.Bots.Count-previousCount);world.NextUid=previousUid;world.Settings.DesiredPopulation=previousDesired;throw;}
        }
    }
    public void Configure(uint uid,string strength,string style,string? aiStrength=null)
    {
        if(!Strengths.Contains(strength)||!Styles.Contains(style)||aiStrength!=null&&!Strengths.Contains(aiStrength))throw new ArgumentException("人机强度或流派无效");
        lock(gate)
        {
            int index=world.Bots.FindIndex(x=>x.Uid==uid);
            if(index<0)throw new ArgumentException("找不到人机");
            var old=world.Bots[index];
            var replacement=builder.Build(uid,old.Profile.Nickname,strength,style,old.Seed,old.CreatedAt,aiStrength??old.AiStrength);
            replacement.Topics=old.Topics;
            replacement.Revision=old.Revision+1;
            world.Bots[index]=replacement;
            try {Save();}catch {world.Bots[index]=old;throw;}
        }
    }
    public IReadOnlyList<LocalBot> Opponents(uint level,string? strength=null)=>All()
        .Where(x=>strength==null||x.Strength==strength)
        .OrderBy(x=>Math.Abs((int)x.Profile.Level-(int)level)).ThenBy(x=>x.Uid).Take(20).ToArray();
    public object Summary()=>new{ok=true,count=All().Count,settings=Settings,pvpReady=false,factionPvpReady=true,
        message="势力战已接入人机配装和原版自动战斗；其他PvP模式、主动挑战与自定义操作策略尚未接入。",
        bots=All().Select(b=>new{uid=b.Uid,name=b.Profile.Nickname,level=b.Profile.Level,strength=b.Strength,aiStrength=b.AiStrength,style=b.Style,b.Revision,tactics=b.Tactics,
            cards=b.Profile.Deck.Select(s=>new{sequence=s,id=b.Profile.CardIds[(int)s-1],level=b.Profile.CardStates[s].Level,
                equipment=new[]{b.Profile.CardStates[s].WeaponSequence}.Concat(b.Profile.CardStates[s].AccessorySequences).Where(x=>x!=0).Select(x=>b.Profile.EquipmentItems.Single(e=>e.Sequence==x).ItemId),gems=b.Profile.CardStates[s].GemIds})})};
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(world));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal byte[] CaptureSave(){lock(gate)return File.ReadAllBytes(path);}
}

/// <summary>Build actual catalog loadouts; strength never injects arbitrary combat multipliers.</summary>
internal sealed class LocalBotBuilder(GameCatalog catalog)
{
    private static readonly BscTable.ITEM_SUB_TYPE[] EquipmentSlots=[BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON,BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_NECKLACE,BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_EARRING];
    private static readonly int[] SkillIndex=[0,4,1,2,3,5,6];
    private static readonly BscTable.NUMBER_TYPE[] SkillRanks=[BscTable.NUMBER_TYPE.ACTIVE_SKILL_1_OPEN_GRADE,BscTable.NUMBER_TYPE.ULTIMATE_SKILL_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_1_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_2_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_3_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_4_OPEN_GRADE,BscTable.NUMBER_TYPE.PASSIVE_SKILL_5_OPEN_GRADE];
    internal LocalBot Build(uint uid,string name,string strength,string style,int seed,ulong created,string? aiStrength=null,uint factionDeck=0,LocalBotBuildTuning? tuning=null)
    {
        int tier=Array.IndexOf(LocalBotWorld.Strengths,strength);if(tier<0||!LocalBotWorld.Styles.Contains(style))throw new ArgumentException("Unknown bot template");
        aiStrength??=strength;
        int aiTier=Array.IndexOf(LocalBotWorld.Strengths,aiStrength);if(aiTier<0)throw new ArgumentException("Unknown bot tactics");
        var random=new Random(seed);
        uint level=(uint)(tier switch{0=>random.Next(18,41),1=>random.Next(45,71),2=>random.Next(75,96),_=>99});
        level=Math.Min(tuning?.Level??level,ProgressionRules.PlayerMax(catalog));
        var p=new LocalPlayer{Uid=uid,Account="world-bot:"+uid,Nickname=name,Level=level,Exp=ProgressionRules.PlayerFloor(catalog,level),Gold=0,Intro="人机 · "+TierName(strength)+" · "+StyleName(style),DebugFlags=0};
        int[] roles=style switch{"burst"=>[1,4,2,7,5],"control"=>[3,2,1,5,6],"sustain"=>[3,1,2,6,5],"counter"=>[1,2,3,7,6],_=>[3,1,2,6,7]};
        var selected=new HashSet<int>();
        foreach(int role in roles)
        {
            int rankLimit=Math.Min((int)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_RANK],catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE));
            var available=catalog.UsableHeroes.Where(c=>LocalCardRegistry.FindExtensionForCard(c.Id)==null&&!selected.Contains(c.EvolveFirstAncestor)&&c.Rank<=rankLimit&&c.Rank<=c.MaxRank&&c.Talent<=BscTable.CARD_TALENT.CARD_TALENT_SSS&&(factionDeck==0||ProtocolRouter.FactionCardAllowed(c,factionDeck))).ToArray();
            var pool=available.Where(c=>(int)c.Class==role).ToArray();if(pool.Length==0)pool=available;
            if(pool.Length==0)throw new InvalidDataException("No usable hero for bot faction template");
            int rankTarget=tuning?.Rank??(tier==3?pool.Max(c=>c.Rank):new[]{3,5,8,12}[tier]);
            var ordered=pool.OrderBy(c=>Math.Abs(c.Rank-rankTarget)).ThenBy(c=>Math.Abs((int)c.Talent-new[]{2,3,5,6}[tier])).ToArray();
            if(ordered.Length==0)throw new InvalidDataException("No usable hero for bot class "+role);
            var finalists=ordered.Where(c=>Math.Abs(c.Rank-rankTarget)==Math.Abs(ordered[0].Rank-rankTarget)).ToArray();
            if(tier==3) {var talent=finalists.Max(c=>c.Talent);finalists=finalists.Where(c=>c.Talent==talent).ToArray();}
            else finalists=finalists.Take(16).ToArray();
            var card=finalists[random.Next(finalists.Length)];selected.Add(card.EvolveFirstAncestor);p.CardIds.Add((uint)card.Id);
            ulong sequence=(ulong)p.CardIds.Count;p.Deck.Add(sequence);
            uint cardLevel=(uint)Math.Min((int)level,ProgressionRules.CardMax(catalog,p,card));
            var state=new LocalCardState{Level=cardLevel,Exp=ProgressionRules.CardFloor(catalog,cardLevel),ActiveSkills=[1,1],PassiveSkills=[1,1,1,1,1]};p.CardStates[sequence]=state;
            var enchantMaterials=catalog.CardEnchants.FirstOrDefault(x=>x.EnchantType==card.EnchantType)?.MaterialDataList;
            int enchantCap=Math.Min(catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_ENCHANT),enchantMaterials?.TakeWhile(x=>x.MinLevel<=cardLevel).Count()??0);
            state.Enchant=(uint)Math.Floor(enchantCap*(tuning?.EnchantFactor??new[]{.15,.4,.8,1}[tier]));
            for(int slot=0;slot<7;slot++)
            {
                int index=slot<2?slot:slot-2;var ids=slot<2?card.ActiveSkillId:card.PassiveSkillId;
                if(index>=ids.Count||!catalog.SkillIds.Contains(ids[index])||card.Rank<catalog.Numbers[SkillRanks[slot]])continue;
                int cap=Math.Min((int)cardLevel,Math.Min((int)catalog.Numbers[BscTable.NUMBER_TYPE.MAX_CARD_SKILL_LEVEL],catalog.CardProcesses.GetSkillItem(SkillIndex[slot])?.ProcessDataList.Count??1));
                uint value=(uint)Math.Max(1,Math.Floor(cap*(tuning?.SkillFactor??new[]{.3,.6,.9,1}[tier])));
                (slot<2?state.ActiveSkills:state.PassiveSkills)[index]=value;
            }
            for(int slot=0;slot<3;slot++)
            {
                var candidates=catalog.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==EquipmentSlots[slot]&&Fits(x,card,cardLevel)).ToArray();
                var item=Choose(candidates,card,style,sequence==1,tier,random);
                if(item==null)continue;
                ulong equipment=p.NextEquipmentSequence++;
                p.EquipmentItems.Add(new(){ItemId=(uint)item.Id,Sequence=equipment,RegisteredAt=created,Locked=true});
                if(slot==0)state.WeaponSequence=equipment;else state.AccessorySequences[slot-1]=equipment;
            }
            var gems=catalog.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_GEM&&x.Level<=cardLevel).ToArray();
            for(int slot=0;slot<3;slot++)
            {
                var gem=Choose(gems,card,style,sequence==1,tier,random);if(gem==null)continue;
                state.GemIds[slot]=(uint)gem.Id;p.Inventory[(uint)gem.Id]=p.Inventory.GetValueOrDefault((uint)gem.Id)+1;
            }
        }
        p.Decks[(uint)BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_ARENA]=p.Deck.ToList();
        p.Decks[(uint)BscTable.USER_DECK_INDEX_TYPE.USER_DECK_INDEX_INFINITE_ARENA]=p.Deck.ToList();
        var bot=new LocalBot{Uid=uid,Seed=seed,Strength=strength,AiStrength=aiStrength,Style=style,CreatedAt=created,Profile=p,
            Tactics=new(){ReactionMilliseconds=new[]{1500,1000,650,400}[aiTier],DecisionAccuracy=new[]{.4,.65,.85,.95}[aiTier],TargetPriority=style=="burst"?"lowest-effective-hp":style=="control"?"healer-or-controller":"front",ReserveInterrupt=aiTier>=2,ChainControl=aiTier>=2&&style=="control",HealAtHpRatio=style=="sustain"?.65:.4}};
        Validate(bot);return bot;
    }
    internal static bool Fits(BscTable.ItemData item,BscTable.CardData card,uint level)
    {
        if(item.Level>level||item.Class!=BscTable.CARD_CLASS.CLASS_ALL&&item.Class!=card.Class)return false;
        if(item.CardId!=0)return card.EvolveFirstAncestor==item.CardId;
        if(item.CardCharacterType!=BscTable.CARD_CHARACTER_TYPE.CARD_CHARACTER_TYPE_NONE)return item.CardCharacterType==card.CardCharacterType;
        if(item.CardFaction is not (BscTable.CARD_FACTION.FACTION_ALL or BscTable.CARD_FACTION.FACTION_NONE))return card.Faction is BscTable.CARD_FACTION.FACTION_ALL or BscTable.CARD_FACTION.FACTION_NONE||card.Faction==item.CardFaction;
        return item.CardGroupType is BscTable.CARD_GROUP.GROUP_ALL or BscTable.CARD_GROUP.GROUP_NONE||card.Group is BscTable.CARD_GROUP.GROUP_ALL or BscTable.CARD_GROUP.GROUP_NONE||card.Group==item.CardGroupType;
    }
    private static BscTable.ItemData? Choose(BscTable.ItemData[] items,BscTable.CardData card,string style,bool tank,int tier,Random random)
    {
        if(items.Length==0)return null;
        // Step is the native progression stage; compare within a tier before style scoring.
        int[] steps=items.Select(x=>x.Step).Distinct().Order().ToArray();
        int target=steps[(int)Math.Round((steps.Length-1)*new[]{.15,.45,.8,1}[tier])];
        var pool=items.Where(x=>x.Step==target).ToArray();
        var maxima=pool.SelectMany(x=>x.AttributeValueIdType.Select((effect,i)=>(effect,unit:i<x.AttributeUnit.Count?(int)x.AttributeUnit[i]:0,value:i<x.AttributeValueId.Count?Math.Abs((double)x.AttributeValueId[i]):0)))
            .GroupBy(x=>(x.effect,x.unit)).ToDictionary(g=>g.Key,g=>Math.Max(1,g.Max(x=>x.value)));
        double Score(BscTable.ItemData item)
        {
            double value=item.Rank*.05;
            for(int i=0;i<Math.Min(item.AttributeValueIdType.Count,item.AttributeValueId.Count);i++)
            {
                var effect=item.AttributeValueIdType[i];int unit=i<item.AttributeUnit.Count?(int)item.AttributeUnit[i]:0;
                // Skill IDs are identifiers, never numeric stat magnitudes.
                value+=effect==BscTable.EQEFFECT_TYPE.EQEFFECT_SKILL?1.5:Weight(effect,card,style,tank)*Math.Abs((double)item.AttributeValueId[i])/maxima[(effect,unit)];
            }
            return value;
        }
        var ranked=pool.OrderByDescending(Score).ThenByDescending(x=>x.Rank).ThenBy(x=>x.Id).ToArray();
        return ranked[random.Next(Math.Min(tier==3?2:4,ranked.Length))];
    }
    private static double Weight(BscTable.EQEFFECT_TYPE effect,BscTable.CardData card,string style,bool tank)
    {
        string name=effect.ToString();bool force=card.AttackType==BscTable.ATTACK_TYPE.ATTACK_FORCE;
        if(name=="EQEFFECT_P_ATTACK")return force?.2:style=="burst"?4:2;
        if(name=="EQEFFECT_F_ATTACK")return force?(style=="burst"?4:2):.2;
        if(name=="EQEFFECT_ADD_HEAL")return card.Class==BscTable.CARD_CLASS.CLASS_SUMMONER?5:.1;
        if(name=="EQEFFECT_HP")return tank||style is "sustain" or "counter"?3:1;
        if(name.Contains("DEFENCE")||name.Contains("REDUCE_")||name.Contains("CRITICAL_DEF"))return tank||style=="sustain"?2.5:1;
        if(name.Contains("PENETRATION")||name.Contains("CRITICAL"))return style=="burst"?3:1.5;
        if(name.Contains("PARRY")||name.Contains("REFLECT")||name.Contains("AVOID"))return style=="counter"?4:1;
        if(name.Contains("IMMUNE")||name.Contains("HIT_"))return style=="control"?3:1;
        return name=="EQEFFECT_HIT"?2:1;
    }
    internal void Validate(LocalBot bot)
    {
        var p=bot.Profile;
        if(p.Uid!=bot.Uid||p.Level<1||p.Level>ProgressionRules.PlayerMax(catalog)||p.Deck.Count!=5||p.CardIds.Count<5||p.DebugFlags!=0||!LocalBotWorld.Strengths.Contains(bot.Strength)||!LocalBotWorld.Strengths.Contains(bot.AiStrength)||!LocalBotWorld.Styles.Contains(bot.Style))throw new InvalidDataException("Invalid bot roster shape");
        var used=new HashSet<ulong>();
        foreach(var seq in p.Deck)
        {
            if(seq<1||seq>(ulong)p.CardIds.Count||!p.CardStates.TryGetValue(seq,out var state))throw new InvalidDataException("Missing bot card state");
            var card=catalog.UsableHeroes.SingleOrDefault(c=>c.Id==p.CardIds[(int)seq-1])??throw new InvalidDataException("Unavailable bot hero");
            if(state.Level<1||state.Level>ProgressionRules.CardMax(catalog,p,card)||state.Exp<ProgressionRules.CardFloor(catalog,state.Level)||state.ActiveSkills.Count!=2||state.PassiveSkills.Count!=5)throw new InvalidDataException("Bot card level exceeds native cap");
            var equipment=new[]{state.WeaponSequence}.Concat(state.AccessorySequences).ToArray();
            for(int i=0;i<equipment.Length;i++)if(equipment[i]!=0)
            {
                if(!used.Add(equipment[i]))throw new InvalidDataException("Bot equipment is shared between slots");
                var owned=p.EquipmentItems.Single(x=>x.Sequence==equipment[i]);var item=catalog.ItemsById[(int)owned.ItemId];
                if(item.ItemSubType!=EquipmentSlots[i]||!Fits(item,card,state.Level))throw new InvalidDataException("Illegal bot loadout");
            }
            if(state.GemIds.Count!=3||state.GemIds.Any(id=>id!=0&&(!catalog.ItemsById.TryGetValue((int)id,out var g)||g.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_GEM||g.Level>state.Level)))throw new InvalidDataException("Invalid bot gems");
        }
        // 日活抽卡会往卡册里加卡，它们不进卡组（卡组恒为 5），所以不校验装备槽，
        // 但等级/技能形状必须合法，否则客户端展示会崩。
        for(int i=0;i<p.CardIds.Count;i++)
        {
            ulong seq=(ulong)i+1;
            if(p.Deck.Contains(seq))continue;
            if(p.CardIds[i]==0||!p.CardStates.TryGetValue(seq,out var extra))throw new InvalidDataException("Missing bot card state");
            var card=catalog.UsableHeroes.SingleOrDefault(c=>c.Id==p.CardIds[i])??throw new InvalidDataException("Unavailable bot hero");
            if(extra.Level<1||extra.Level>ProgressionRules.CardMax(catalog,p,card)||extra.Exp<ProgressionRules.CardFloor(catalog,extra.Level)||extra.ActiveSkills.Count!=2||extra.PassiveSkills.Count!=5)throw new InvalidDataException("Bot card level exceeds native cap");
            if(extra.GemIds.Count!=3||extra.GemIds.Any(id=>id!=0&&(!catalog.ItemsById.TryGetValue((int)id,out var g)||g.ItemType!=BscTable.ITEM_TYPE.ITEM_TYPE_GEM||g.Level>extra.Level)))throw new InvalidDataException("Invalid bot gems");
        }
    }
    internal static string TierName(string value)=>value switch{"casual"=>"普通","regular"=>"精英","expert"=>"高手","legend"=>"宗师",_=>value};
    internal static string StyleName(string value)=>value switch{"balanced"=>"均衡","burst"=>"爆发","control"=>"控制","sustain"=>"续航","counter"=>"反击",_=>value};
}
