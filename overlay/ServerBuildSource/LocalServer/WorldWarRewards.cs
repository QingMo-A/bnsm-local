internal sealed partial class ProtocolRouter
{
    private bool SettleWorldWarMail(IEnumerable<LocalPlayer> players,ulong now)
    {
        ulong day=(now+WorldDayOffset)/86400,week=WorldWeek(day);
        var scores=new List<(LocalPlayer Player,uint Territory,bool Weekly,ulong Period,uint Score)>();
        foreach(var player in players)
        {
            var periods=WorldPeriods(player.Competitive);
            foreach(var (id,score) in player.Competitive.WorldScores)
            {
                if(periods.Day>0&&periods.Day<day&&score.Daily>0)scores.Add((player,id,false,periods.Day,score.Daily));
                if(periods.Week>0&&periods.Week<week&&score.Weekly>0)scores.Add((player,id,true,periods.Week,score.Weekly));
            }
        }
        bool changed=false;
        foreach(var board in scores.GroupBy(x=>(x.Territory,x.Weekly,x.Period)).OrderBy(x=>x.Key.Weekly).ThenBy(x=>x.Key.Period).ThenBy(x=>x.Key.Territory))
        {
            var (id,weekly,period)=board.Key;
            Dictionary<uint,ulong> Paid(LocalPlayer p)=>weekly?p.Mailbox.WorldWeeklyPaid:p.Mailbox.WorldDailyPaid;
            if(!board.Any(x=>Paid(x.Player).GetValueOrDefault(id)<period))continue;
            if(!Competitive.Territories.Any(x=>x.Id==id&&x.MapOpen))continue;
            uint groupId=WorldRewardGroup(id,weekly);
            var group=Competitive.WorldRewards.FirstOrDefault(x=>x.GroupId==groupId&&(int)x.GroupType==(weekly?1:0));
            if(group==null)continue;
            ulong closingDay=weekly?(period+1)*7-3:period+1;
            ulong cutoff=closingDay*86400-WorldDayOffset;
            uint kind=(uint)(weekly?RewardGiveType.REWARD_GIVE_TYPE_WORLD_WAR_WEEKLY_RANK:RewardGiveType.REWARD_GIVE_TYPE_WORLD_WAR_DAILY_RANK);
            int rank=0;
            foreach(var entry in board.OrderByDescending(x=>x.Score).ThenBy(x=>x.Player.Uid))
            {
                rank++;
                var paid=Paid(entry.Player);
                if(paid.GetValueOrDefault(id)>=period)continue;
                var reward=group.RankingRewardDataList.FirstOrDefault(x=>x.RankingMin<=rank&&rank<=x.RankingMax);
                if(reward!=null)
                {
                    var box=entry.Player.Mailbox;
                    var mail=new LocalMail{Time=cutoff,Kind=kind,Rank=(uint)rank,Territory=id,Message=$"一统天下{(weekly?"每周":"每日")}排名奖励 · 第 {rank} 名"};
                    for(int i=0;i<reward.RewardValue.Count;i++)if(reward.RewardValue[i]>0)
                        mail.Rewards.Add(new(){Type=(uint)reward.RewardIdType[i],Id=reward.RewardId[i],Count=reward.RewardValue[i]});
                    if(mail.Rewards.Count>0){mail.Sequence=box.NextSequence++;box.Pending.Add(mail);}
                }
                // Record even an unqualified rank; do not rerank it after another player starts a new period.
                paid[id]=period;changed=true;
            }
        }
        return changed;
    }
}
