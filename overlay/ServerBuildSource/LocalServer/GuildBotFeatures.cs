// Membership and NPC guild-work ledgers share the atomic players.json save.
// Bot-world identities/loadouts remain in bot-world.json and are never rewritten here.
internal sealed class LocalGuildBotActivity
{
    public LocalGuildUser User {get;set;}=new();
    public ulong AttendanceDay {get;set;}
    public ulong WorkDay {get;set;}
    public ulong ContributionDay {get;set;}
    public uint LastGuild {get;set;}
    public ulong AttendanceAt {get;set;}
    public ulong ContributionAt {get;set;}
    // One original contribution voucher is earned by the NPC's guild errand
    // each active day. This guild-only supply cannot enter a human inventory.
    public Dictionary<uint,int> Supplies {get;set;}=[];
    public List<LocalGuildBotAttendanceReward> Rewards {get;set;}=[];
}
internal sealed class LocalGuildBotAttendanceReward
{
    public uint GuildId {get;set;}
    public uint DataId {get;set;}
    public ulong Day {get;set;}
}
internal sealed partial class ProtocolRouter
{
    private LocalPlayer? GuildActor(uint uid)=>store.Find(uid)??Bots.Find(uid)?.Profile;
    private LocalGuildBotActivity? BotGuildActivity(uint uid)=>store.All().Select(p=>p.GuildBotActivities.GetValueOrDefault(uid)).FirstOrDefault(x=>x!=null);
    private LocalGuildBotActivity EnsureBotGuildActivity(LocalGuild g,uint uid)
    {
        var existing=BotGuildActivity(uid);if(existing!=null)return existing;
        var founder=store.All().Single(p=>p.FoundedGuilds.Contains(g));
        var added=new LocalGuildBotActivity();founder.GuildBotActivities[uid]=added;return added;
    }
    private LocalGuildUser GuildAccount(LocalPlayer actor)=>Bots.Find(actor.Uid)==null?actor.Guild:BotGuildActivity(actor.Uid)?.User??actor.Guild;
    private void ScheduleBotGuildActivity(LocalGuild g,LocalPlayer bot,ulong now)
    {
        var activity=EnsureBotGuildActivity(g,bot.Uid);
        activity.LastGuild=g.Id;activity.User.GuildId=g.Id;activity.User.Day=TowerDay(now);
        activity.AttendanceAt=now+15+bot.Uid%45;
        activity.ContributionAt=now+90+bot.Uid%150;
    }
    internal void GuildBotTick(ulong? at=null)
    {
        lock(store.Gate){
            using var batch=store.BatchSaves();ulong now=at??Now;
            RefreshGuilds(now);bool changed=false;
            ulong attendanceDay=GuildAttendanceDay(now),workDay=TowerDay(now);
            var ticket=catalog.Items.Where(x=>x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_TICKET_CONTRIBUTION).OrderBy(x=>x.Id).FirstOrDefault();
            foreach(var guild in AllGuilds.Where(g=>!g.Disbanded))foreach(var pair in guild.Members.ToArray()){
                var bot=Bots.Find(pair.Key);if(bot==null)continue;
                var member=pair.Value;var activity=EnsureBotGuildActivity(guild,bot.Uid);
                if(!member.IsBot){member.IsBot=true;changed=true;}
                if(activity.LastGuild!=guild.Id){ScheduleBotGuildActivity(guild,bot.Profile,now);changed=true;}
                if(activity.AttendanceDay<attendanceDay&&now>=activity.AttendanceAt&&now>=member.Joined){
                    member.AttendanceDay=attendanceDay;activity.User.AttendanceDay=attendanceDay;activity.AttendanceDay=attendanceDay;
                    activity.AttendanceAt=(attendanceDay+1)*86400+14*3600+120+15+bot.Uid%45;changed=true;
                }
                if(ticket!=null&&activity.WorkDay<workDay&&now>=activity.ContributionAt&&now>=member.Joined){
                    // No offline-day catch-up or new-member windfall: at most
                    // one current-day guild errand supply per resident.
                    uint item=(uint)ticket.Id;if(activity.Supplies.GetValueOrDefault(item)==0)activity.Supplies[item]=1;
                    activity.WorkDay=workDay;changed=true;
                }
                if(ticket!=null&&activity.ContributionDay<workDay&&now>=activity.ContributionAt&&activity.Supplies.GetValueOrDefault((uint)ticket.Id)>0){
                    var table=Guilds.Contributions.OrderByDescending(x=>x.Prob).ThenBy(x=>x.Id).FirstOrDefault();
                    var slot=table?.ContributionSlotInfoList.Where(x=>x.PcLvMin<=bot.Profile.Level&&bot.Profile.Level<=x.PcLvMax&&x.RewardGuildPoint>=0).OrderBy(x=>x.SlotId).ThenByDescending(x=>x.SlotProb).ThenBy(x=>x.SlotSeq).FirstOrDefault();
                    if(slot!=null){uint points=(uint)slot.RewardGuildPoint;
                        if((ulong)guild.Points+points<=uint.MaxValue&&(ulong)member.Contribution+points<=uint.MaxValue&&(ulong)member.WeeklyContribution+points<=uint.MaxValue&&GuildDonations(guild)<=ulong.MaxValue-points&&guild.DonationCount<uint.MaxValue){
                            activity.Supplies[(uint)ticket.Id]--;activity.ContributionDay=workDay;
                            activity.ContributionAt=(workDay+1)*86400-4*3600+90+bot.Uid%150;
                            guild.TotalDonationPoints=GuildDonations(guild)+points;guild.Points+=points;guild.DonationCount++;member.Contribution+=points;member.WeeklyContribution+=points;
                            GuildLog(guild,bot.Profile,GuildHistoryLogType.GUILD_HISTORY_GUILD_CONTRIBUTION,points);changed=true;
                        }
                    }
                }
                foreach(var pending in activity.Rewards.Where(x=>x.GuildId==guild.Id).ToArray()){
                    var reward=Guilds.Attendance.SingleOrDefault(x=>x.DataId==pending.DataId);
                    if(reward!=null&&reward.RewardGuildPoint>=0&&(ulong)guild.Points+(uint)reward.RewardGuildPoint<=uint.MaxValue){
                        guild.Points+=(uint)reward.RewardGuildPoint;activity.Rewards.Remove(pending);
                        GuildLog(guild,bot.Profile,GuildHistoryLogType.GUILD_HISTORY_ATTENDACE_REWARD,pending.DataId);changed=true;
                    }
                }
            }
            if(changed)store.Save();
        }
    }
}
