using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

internal sealed partial class ProtocolRouter
{
    private Reply? HandleChat(string name,object? request,LocalPlayer p,SessionKeyInfo session,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name=="GS_FETCH_PUBLIC_CHAT_MESSAGE_REQUEST") {
            var fetch=(GsFetchPublicChatMessageRequest)request!;
            var result=new GsFetchPublicChatMessageOk();result.msgList.AddRange(worldChat.Fetch(fetch.lastMsgSeq,fetch.beforeMsgSeq));return ok(result);
        }
        if(name=="GS_SEND_PUBLIC_CHAT_MESSAGE_REQUEST") {
            var send=(GsSendPublicChatMessageRequest)request!;
            if(send.replayMessage || send.replayDataSeq!=0)return fail("battle replay sharing is unavailable locally",ServerErrorCode.ERROR_INVALID_ID);
            string message=(send.msg??"").Trim();
            if(message.Length==0 || message.Length>100 || message.Any(char.IsControl) || message.IndexOfAny(['[',']'])>=0)
                return fail("chat accepts 1..100 plain text characters",ServerErrorCode.ERROR_INVALID_ID);
            string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{session.sequenceNo}/{p.Uid}")));
            try {
                var added=worldChat.Send(key,message,NameCard(p),CardInfo(p.CardIds[(int)p.LeaderSequence-1],p.LeaderSequence,p));
                var result=new GsSendPublicChatMessageOk {silentTime=added.timeStamp+3};result.msgList.AddRange(worldChat.Fetch(send.lastMsgSeq,0));return ok(result);
            } catch(InvalidDataException ex) {return fail(ex.Message,ServerErrorCode.ERROR_INVALID_ID);}
        }

        return null;
    }

    /// <summary>
    /// 让居民自己动一次，并把高光结果直接发到世界频道。
    /// 客户端没有系统频道（ChatType 只有 PUBLIC/PRIVATE/GUILD/HELP），
    /// 伪造"系统"用户也没有合法头像卡，所以播报按本人身份发——效果是看到群里有人晒卡。
    /// </summary>
    internal BotLifeEvent? BotLifeTick()
    {
        var life=Bots.Advance(Now);
        if(life!=null)worldChat.Broadcast(life.Actor,life.Name,life.Level,life.AvatarId,life.Text,life.Time);
        return life;
    }

    /// <summary>世界频道自己的心跳。和玩家在不在线、开没开聊天面板无关。</summary>
    internal void AmbientTick()=>worldChat.Pump();
    internal byte[] CaptureAmbientSave()=>worldChat.CaptureSave();

    private PKUserPollingData BotPolling(ConnectionState connection)
    {
        var data=new PKUserPollingData();
        foreach(var life in Bots.RecentEvents().Where(x=>x.Kind=="draw"&&x.CardId!=0&&x.Time+300>=Now).OrderBy(x=>x.Time).ThenBy(x=>x.Actor)){
            ulong key=life.Time*1000000+life.Actor;
            if(key<=connection.LastBotNotice)continue;
            data.userMessageList.Add(new(){messageType=MegaPhoneMSGType.MEGA_PHONE_MSG_TYPE_GET_CARD,timeStamp=life.Time,nickName=life.Name,idValue=life.CardId,contents=MegaPhoneMSGContents.MEGA_PHONE_MSG_CONTENTS_GACHA});
            connection.LastBotNotice=key;if(data.userMessageList.Count==2)break;
        }
        return data;
    }
}

internal sealed class AmbientWorldChat
{
    internal sealed class Settings {
        public bool Enabled {get;set;}=true;
        /// <summary>剧本对话（真有一个话题在聊）的节奏。</summary>
        public int MinimumPauseSeconds {get;set;}=240;
        public int MaximumPauseSeconds {get;set;}=600;
        public int MinimumReplySeconds {get;set;}=6;
        public int MaximumReplySeconds {get;set;}=13;
        /// <summary>
        /// 世界频道的底噪节奏：单人一句、彼此不相干，招人/求带/打卡/抱怨/问价都在这一层。
        /// 真实频道的大部分内容是这个，剧本对话只是偶尔出现的一小段。两条流各自计时，可以交错说话。
        /// </summary>
        public int ChatterMinimumSeconds {get;set;}=30;
        public int ChatterMaximumSeconds {get;set;}=75;
        public List<Actor> Actors {get;set;}=[];
        public List<List<Line>> Conversations {get;set;}=[];
        public List<ChatterLine> Chatter {get;set;}=[];
        public Dictionary<string,List<string>> Replies {get;set;}=[];
    }
    internal sealed class Actor {
        public uint Uid {get;set;}
        public string Name {get;set;}="";
        public uint Level {get;set;}
        public uint CardId {get;set;}
        public List<string> Topics {get;set;}=[];
    }
    internal sealed class Line {
        public int Actor {get;set;}
        public string Text {get;set;}="";
    }
    /// <summary>
    /// 世界频道里的一条底噪。单句、不接上茬，所以可以高频地撒；
    /// Kind 只是给人看的分类（recruit/party/idle/rant/shop/aside），运行时不参与判定。
    /// </summary>
    internal sealed class ChatterLine {
        public int Actor {get;set;}
        public string Text {get;set;}="";
        public string Kind {get;set;}="";
    }
    internal sealed class Entry {
        public uint Sequence {get;set;}
        public ulong Time {get;set;}
        public uint Uid {get;set;}
        public string Name {get;set;}="";
        public uint Level {get;set;}
        public uint AvatarId {get;set;}
        public uint AvatarLevel {get;set;}=1;
        public uint AvatarSkin {get;set;}
        public string Text {get;set;}="";
        public string RequestKey {get;set;}="";
        public string BotText {get;set;}="";
    }
    internal sealed class Conversation {
        public string Topic {get;set;}="";
        public string LastGroup {get;set;}="";
        public int LastActor {get;set;}=-1;
        public ulong LastUserAt {get;set;}
        public ulong LastReplyAt {get;set;}
        public List<string> RecentInputs {get;set;}=[];
    }
    internal sealed class PlannedLine {
        public int Actor {get;set;}
        public string Text {get;set;}="";
        public ulong DueAt {get;set;}
        public ulong ExpiresAt {get;set;}
        public uint TargetUid {get;set;}
        public uint SourceSequence {get;set;}
        public string Topic {get;set;}="";
        public string Group {get;set;}="";
    }
    private sealed class History {
        public uint LastSequence {get;set;}
        public List<Entry> Messages {get;set;}=[];
        public ulong NextAmbientAt {get;set;}
        /// <summary>底噪（广告/求带/水贴）的下一条时间。与 NextAmbientAt 各自独立计时。</summary>
        public ulong NextChatterAt {get;set;}
        /// <summary>
        /// 上一次真正推进的时间。用来把"谁在调 Tick"和"能不能推进"解绑：
        /// 客户端轮询不能加速，服务端后台循环也不会因为 5 秒一跳就把频道刷爆。
        /// </summary>
        public ulong LastTickAt {get;set;}
        public ulong QuietUntil {get;set;}
        public ulong LastReplyAt {get;set;}
        public List<string> RecentThreads {get;set;}=[];
        public List<PlannedLine> Pending {get;set;}=[];
        public Dictionary<uint,Conversation> Contexts {get;set;}=[];
    }
    private readonly Func<IReadOnlyList<Actor>>? residentActors;
    internal IReadOnlyList<Actor> LocalActors => settings.Enabled ? residentActors?.Invoke()??settings.Actors : [];
    private readonly Settings settings;
    private readonly string path;
    private readonly History history;
    private readonly Func<ulong> timeSource;
    private readonly Random random;
    private readonly bool chatterEnabled;
    private readonly object gate=new();
    private static readonly JsonSerializerOptions Options=new() {WriteIndented=true};
    /// <summary>
    /// chatter 默认开。关掉只用于测试：应答逻辑的时序断言需要一条安静的频道，
    /// 底噪的真实感另外单独测，两者不该互相干扰。
    /// </summary>
    internal AmbientWorldChat(string root,string directory,GameCatalog catalog,Func<ulong>? clock=null,Random? rng=null,Func<IReadOnlyList<Actor>>? actors=null,bool chatter=true)
    {
        residentActors=actors;
        chatterEnabled=chatter;
        timeSource=clock ?? (()=>ProtocolRouter.Now);random=rng ?? Random.Shared;
        settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"LocalServer/data/ambient-chat.json")))!;
        // Local conversations complement the original chatter without editing
        // the shared content archive. These are in-game recruitment/trading ads.
        string[] localLines={"势力战先看今天开的属性本，没开放的明天再安排。","天下争霸结算是服务器时间晚上十点，奖励去礼物箱领。","比武币攒着买需要的材料，别只盯着战力数字。","配装求稳就先补命中，控制打空了堆输出也难受。","前排先站得住，再考虑把控制链接起来。","卡册重复的卡转碎片，先把主力养好。","门派招人，一起打日常，不要求天天在线。","【门派招募】休闲互助，攻略和配装都能一起聊。","【交换心得】收控制队配装思路，广告位用完了。","有没有人研究闪避队，想借套阵容切磋一下。","抽之前说好就一次，抽完又想攒下一次了。","刚做完饭，挂着聊两句再打日常。","今晚先把体力用了，明天还要早起。","打不过先看看对面技能，不一定是星级的问题。","收工了收工了，今晚不卷排名。","昨天连着歪，今天先存着钻石。"};
        for(int i=0;i<localLines.Length;i++)if(!settings.Chatter.Any(x=>x.Text==localLines[i]))settings.Chatter.Add(new(){Actor=i%Math.Max(1,settings.Actors.Count),Text=localLines[i]});
        settings.MinimumPauseSeconds=Math.Clamp(settings.MinimumPauseSeconds,45,600);
        settings.MaximumPauseSeconds=Math.Clamp(settings.MaximumPauseSeconds,settings.MinimumPauseSeconds,1200);
        settings.MinimumReplySeconds=Math.Clamp(settings.MinimumReplySeconds,4,20);
        settings.MaximumReplySeconds=Math.Clamp(settings.MaximumReplySeconds,settings.MinimumReplySeconds,30);
        settings.ChatterMinimumSeconds=Math.Clamp(settings.ChatterMinimumSeconds,8,600);
        settings.ChatterMaximumSeconds=Math.Clamp(settings.ChatterMaximumSeconds,settings.ChatterMinimumSeconds,1200);
        if(settings.Actors.Count<2 || settings.Conversations.Count<2 || settings.Actors.Select(a=>a.Uid).Distinct().Count()!=settings.Actors.Count ||
            settings.Actors.Any(a=>a.Uid<900000 || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length>12 || a.Level==0 || !catalog.UsableHeroes.Any(c=>c.Id==a.CardId)) ||
            settings.Conversations.Any(t=>t.Count is <1 or >4 || t.Any(l=>l.Actor<0 || l.Actor>=settings.Actors.Count || !ValidText(l.Text))) ||
            settings.Replies.Any(t=>t.Value.Count==0 || t.Value.Any(v=>!ValidText(v))))
            throw new InvalidDataException("Invalid local ambient chat cast or conversations");
        // 底噪是可选内容：手改配置写错一句不该把整个服务端弄起不来，剔掉坏行即可。
        settings.Chatter.RemoveAll(l=>l.Actor<0 || l.Actor>=settings.Actors.Count || !ValidText(l.Text));
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"world-chat.json");
        history=File.Exists(path) ? JsonSerializer.Deserialize<History>(File.ReadAllText(path))! : new();
        if(history.Messages.Any(m=>m.Sequence>history.LastSequence))throw new InvalidDataException("Invalid saved chat sequence");
        ulong now=timeSource();
        history.Pending.RemoveAll(p=>p.Actor<0 || p.Actor>=(residentActors?.Invoke()??settings.Actors).Count || p.ExpiresAt<now || !ValidText(p.Text));
        if(history.NextAmbientAt==0)history.NextAmbientAt=now+(ulong)Pause();
        if(history.NextChatterAt==0)history.NextChatterAt=now+(ulong)ChatterPause();
        if(settings.Enabled && history.Messages.Count==0) {
            // Seed a complete short exchange, never leave a scripted question without its answer.
            var shortThreads=settings.Conversations.Where(t=>t.Count<=2).ToArray();
            var thread=shortThreads.Length>0?shortThreads[random.Next(shortThreads.Length)]:settings.Conversations[random.Next(settings.Conversations.Count)];
            history.RecentThreads.Add(ThreadKey(thread));
            foreach(var line in thread)Append(line.Actor,line.Text,now>30?now-30:now);
        }
        Save();
    }
    private static bool ValidText(string? text)=>!string.IsNullOrWhiteSpace(text) && text.Length<=100 && !text.Any(char.IsControl) && text.IndexOfAny(['[',']'])<0;
    private int Pause()=>random.Next(settings.MinimumPauseSeconds,settings.MaximumPauseSeconds+1);
    private int ChatterPause()=>random.Next(settings.ChatterMinimumSeconds,settings.ChatterMaximumSeconds+1);
    private static string ThreadKey(List<Line> lines)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("/",lines.Select(l=>l.Text)))));
    private void Save() {
        string data=JsonSerializer.Serialize(history,Options);
        for(int attempt=0;;attempt++)try {File.WriteAllText(path+".tmp",data);File.Move(path+".tmp",path,true);break;}
        catch(IOException ex)when(attempt<8 && (ex.HResult&0xffff) is 32 or 33) {Thread.Sleep(25*(attempt+1));}
    }
    internal byte[] CaptureSave(){lock(gate)return File.ReadAllBytes(path);}
    private Entry Add(Entry entry) {
        entry.Sequence=checked(++history.LastSequence);history.Messages.Add(entry);
        if(history.Messages.Count>200)history.Messages.RemoveRange(0,history.Messages.Count-200);
        return entry;
    }
    private void Append(int actorIndex,string text,ulong when,string botText="") {
        var cast=residentActors?.Invoke()??settings.Actors;
        var actor=cast[actorIndex%cast.Count];
        Add(new() {Time=when,Uid=actor.Uid,Name=actor.Name,Level=actor.Level,AvatarId=actor.CardId,Text=text,BotText=botText});
    }
    private bool IsActor(uint uid)=>(residentActors?.Invoke()??settings.Actors).Any(a=>a.Uid==uid);
    private bool RecentText(string text,ulong now,int window=7200)=>history.Messages.Where(m=>IsActor(m.Uid) && m.Time+(ulong)window>=now).TakeLast(160)
        .Any(m=>WorldChatDialogue.Normalize(m.BotText.Length>0?m.BotText:m.Text)==WorldChatDialogue.Normalize(text));

    /// <summary>
    /// 服务端自己的心跳。世界频道不该只在玩家打开面板时才热闹：
    /// 后台按同一套时间闸门推进，关掉聊天框也在发生，回头打开看到的就是这一屏真实的历史。
    /// </summary>
    internal void Pump() {lock(gate)Tick();}

    private void Tick() {
        ulong now=timeSource();
        // 时间闸门：不管谁在调、调得多密（客户端轮询 / 5 秒后台循环），这里每秒最多真正推进一次。
        // 轮询因此仍然不能加速，后台循环也不会把频道刷爆。
        if(now<=history.LastTickAt+2)return;
        history.LastTickAt=now;
        bool removed=history.Pending.RemoveAll(p=>p.ExpiresAt<now)>0;
        if(!settings.Enabled || now<history.QuietUntil){if(removed)Save();return;}
        // 玩家的回应优先，其次才是剧本，最后才是底噪。
        if(StepReply(now))return;
        if(StepScript(now))return;
        if(StepChatter(now))return;
        if(removed)Save();
    }

    /// <summary>已排队的回应。返回 true 表示这一拍已经用掉了。</summary>
    private bool StepReply(ulong now) {
        var next=history.Pending.OrderByDescending(p=>p.TargetUid!=0).ThenBy(p=>p.DueAt).FirstOrDefault();
        if(next==null)return false;
        if(now<next.DueAt)return false;
        if(next.TargetUid!=0 && history.LastReplyAt+10>now){next.DueAt=history.LastReplyAt+10;Save();return true;}
        history.Pending.Remove(next);
        // Suppress obsolete questions, repeated lines and unfinished offline scripts.
        bool current=next.TargetUid==0 || history.Messages.LastOrDefault(m=>m.Uid==next.TargetUid)?.Sequence==next.SourceSequence;
        if(current && !RecentText(next.Text,now)) {
            string text=next.Text;
            if(next.TargetUid!=0 && history.Messages.Where(m=>!IsActor(m.Uid) && m.Time+120>=now).Select(m=>m.Uid).Distinct().Take(2).Count()>1) {
                string nick=history.Messages.Last(m=>m.Uid==next.TargetUid).Name;
                if(nick.Length+text.Length+1<=100)text=nick+"，"+text;
            }
            Append(next.Actor,text,now,next.Text);
            if(next.TargetUid!=0 && history.Contexts.TryGetValue(next.TargetUid,out var context)) {
                context.LastReplyAt=now;context.LastActor=next.Actor;context.Topic=next.Topic;context.LastGroup=next.Group;history.LastReplyAt=now;
            }
        }
        history.NextAmbientAt=Math.Max(history.NextAmbientAt,now+(ulong)Pause());Save();return true;
    }

    /// <summary>剧本对话：一小段真的在聊同一个话题的对话。</summary>
    private bool StepScript(ulong now) {
        if(now<history.NextAmbientAt)return false;
        var available=settings.Conversations.Where(t=>!history.RecentThreads.Contains(ThreadKey(t)) && t.All(l=>!RecentText(l.Text,now))).ToList();
        if(available.Count==0){history.NextAmbientAt=now+(ulong)Pause();Save();return true;}
        var thread=available[random.Next(available.Count)];history.RecentThreads.Add(ThreadKey(thread));
        while(history.RecentThreads.Count>Math.Min(18,settings.Conversations.Count-1))history.RecentThreads.RemoveAt(0);
        Append(thread[0].Actor,thread[0].Text,now);
        ulong due=now;
        foreach(var line in thread.Skip(1)) {
            due+=(ulong)random.Next(9,20);
            history.Pending.Add(new(){Actor=line.Actor,Text=line.Text,DueAt=due,ExpiresAt=now+75});
        }
        history.NextAmbientAt=now+(ulong)Pause();Save();
        return true;
    }

    /// <summary>
    /// 底噪：招人、求带、打卡、抱怨、问价，一句一条，彼此不相干。
    /// 真实的世界频道大部分时间是这个样子——好几个人同时在说不同的事，
    /// 而不是排队等上一个人说完。所以这条流和剧本流各自计时，允许交错。
    /// 同一句在 40 分钟内不重复；池子按人声分配，先挑这个人最近没说过的。
    /// </summary>
    private bool StepChatter(ulong now) {
        if(!chatterEnabled || settings.Chatter.Count==0)return false;
        if(now<history.NextChatterAt)return false;
        var pool=settings.Chatter;
        var fresh=pool.Where(l=>!RecentText(l.Text,now,7200)).ToList();
        if(fresh.Count==0)fresh=pool;
        // 别让同一个人连说两句——真实频道里几乎不会这样。
        uint lastUid=history.Messages.Count>0?history.Messages[^1].Uid:0;
        var cast=residentActors?.Invoke()??settings.Actors;
        var varied=fresh.Where(l=>cast[l.Actor%cast.Count].Uid!=lastUid).ToList();
        var pick=varied.Count>0?varied:fresh;
        var line=pick[random.Next(pick.Count)];
        Append(line.Actor,line.Text,now);
        history.NextChatterAt=now+(ulong)ChatterPause();Save();
        return true;
    }

    internal List<PKChatMessage> Fetch(uint last,uint before) {
        lock(gate) {
            Tick();
            var messages=history.Messages.Where(m=>before!=0 ? m.Sequence<before : m.Sequence>last).OrderByDescending(m=>m.Sequence).Take(10);
            return messages.Select(ToPacket).ToList();
        }
    }
    private int ChooseActor(string topic,Conversation context,int addressed,ulong now) {
        var actors=residentActors?.Invoke()??settings.Actors;
        if(addressed>=0)return addressed;
        if(context.LastActor>=0 && context.LastActor<actors.Count && context.LastReplyAt+180>=now)return context.LastActor;
        var candidates=Enumerable.Range(0,actors.Count).Where(i=>!history.Messages.Any(m=>m.Uid==actors[i].Uid && m.Time+20>now)).ToArray();
        if(candidates.Length==0)return -1;
        var preferred=candidates.Where(i=>actors[i].Topics.Contains(topic)).ToArray();
        return preferred.Length>0?preferred[random.Next(preferred.Length)]:candidates[random.Next(candidates.Length)];
    }
    private void PlanReply(Entry user,ulong now) {
        // New player speech takes precedence over ambient scripts and their own pending question.
        history.Pending.RemoveAll(p=>p.TargetUid==0 || p.TargetUid==user.Uid);
        history.NextAmbientAt=Math.Max(history.NextAmbientAt,now+60);
        if(!history.Contexts.TryGetValue(user.Uid,out var context) || context.LastUserAt+600<now)context=new();
        history.Contexts[user.Uid]=context;
        if(history.Contexts.Count>32)foreach(var id in history.Contexts.OrderBy(x=>x.Value.LastUserAt).Take(history.Contexts.Count-32).Select(x=>x.Key).ToArray())history.Contexts.Remove(id);
        string normalized=WorldChatDialogue.Normalize(user.Text);
        bool repeated=context.RecentInputs.Contains(normalized);context.LastUserAt=now;
        context.RecentInputs.Add(normalized);while(context.RecentInputs.Count>8)context.RecentInputs.RemoveAt(0);
        var decision=WorldChatDialogue.Analyze(user.Text,context.Topic,context.LastGroup);
        if(decision.Quiet){history.QuietUntil=now+300;history.Pending.Clear();context.Topic="";return;}
        if(decision.ClearContext){context.Topic="";return;}
        if(repeated || now<history.QuietUntil)return;
        int addressed=Array.FindIndex((residentActors?.Invoke()??settings.Actors).ToArray(),a=>user.Text.StartsWith("@"+a.Name,StringComparison.Ordinal) || user.Text.StartsWith(a.Name+"，",StringComparison.Ordinal) || user.Text.StartsWith(a.Name+",",StringComparison.Ordinal));
        string group=decision.Group;
        if(group.Length==0 && addressed>=0)group="unknown";
        if(group.Length==0){context.Topic="";return;}
        bool followup=decision.Topic.Length>0 && decision.Topic==context.Topic;
        if(history.LastReplyAt+10>now || context.LastReplyAt+(followup?10UL:30UL)>now)return;
        if(random.Next(100)>=decision.Chance && addressed<0)return;
        if(!settings.Replies.TryGetValue(group,out var choices))return;
        var available=choices.Where(t=>!RecentText(t,now)).ToArray();if(available.Length==0)return;
        int actor=ChooseActor(decision.Topic,context,addressed,now);if(actor<0)return;
        string text=available[random.Next(available.Length)];
        context.Topic=decision.Topic;context.LastGroup=group;
        if(history.Pending.Count(p=>p.TargetUid!=0)>=3)return;
        history.Pending.Add(new(){Actor=actor,Text=text,TargetUid=user.Uid,SourceSequence=user.Sequence,Topic=decision.Topic,Group=group,
            DueAt=now+(ulong)random.Next(settings.MinimumReplySeconds,settings.MaximumReplySeconds+1),ExpiresAt=now+45});
    }
    internal PKChatMessage Send(string key,string text,PKUserNameCardInfo name,PKUserCardInfo leader) {
        lock(gate) {
            var prior=history.Messages.FirstOrDefault(m=>m.RequestKey==key);
            if(prior!=null) {if(prior.Text!=text)throw new InvalidDataException("chat request sequence reused with another message");return ToPacket(prior);}
            ulong now=timeSource();
            if(history.Messages.Any(m=>m.Uid==name.uid && m.Time+3>now))throw new InvalidDataException("wait a moment before sending another message");
            var entry=Add(new() {Time=now,Uid=name.uid,Name=name.commonInfo.nick,Level=name.commonInfo.level,AvatarId=leader.cardID,AvatarLevel=leader.cardLevel,AvatarSkin=leader.cardSkinId,Text=text,RequestKey=key});
            // 玩家刚说完话，底噪让一让，别让一句招人广告立刻把他的发言顶出视野。
            history.NextChatterAt=Math.Max(history.NextChatterAt,now+12);
            if(settings.Enabled)PlanReply(entry,now);
            Save();return ToPacket(entry);
        }
    }
    /// <summary>
    /// 把一条居民自己说的话直接放进世界频道，不经过剧本与去重队列。
    /// 用于人机抽到高资质卡后的晒卡：内容来自真实抽卡结果，事后能和卡册对上。
    /// </summary>
    internal void Broadcast(uint uid,string name,uint level,uint avatarId,string text,ulong when) {
        lock(gate) {
            if(!settings.Enabled || !ValidText(text))return;
            Add(new() {Time=when,Uid=uid,Name=name,Level=level,AvatarId=avatarId,AvatarLevel=1,Text=text,BotText=text});
            Save();
        }
    }
    private static PKChatMessage ToPacket(Entry e)=>new() {type=ChatType.CHAT_TYPE_PUBLIC,senderUid=e.Uid,message=e.Text,timeStamp=e.Time,chatSeq=e.Sequence,
        senderCommonInfo=new PKUserCommonInfo {nick=e.Name,level=e.Level},leaderCardInfo=new PKUserLeaderCardInfo {cardID=e.AvatarId,cardLevel=e.AvatarLevel,cardSkinId=e.AvatarSkin}};
}
