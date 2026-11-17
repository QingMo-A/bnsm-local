using System.Security.Cryptography;

internal sealed partial class ProtocolRouter
{
    private IEnumerable<BscTable.GachaSetData> AvailableGachaSets => catalog.GachaSets.Where(x => x.GachaCategoryIndex is >= 1 and <= 5 or >= 11 and <= 15);
    private PKUserCardInfo CardInfo(uint id, ulong sequence, LocalPlayer? player=null)
    {
        var info = new PKUserCardInfo { cardSeq = sequence, cardID = id, cardLevel = 1, registTime = Now };
        if(player!=null) {
            var state=CardState(player,sequence);
            info.cardLevel=state.Level;info.cardExp=state.Exp;info.protectCard=state.Protect;info.cardColor=state.Color;info.cardSkinId=state.Skin;info.enchantGrade=state.Enchant;
            info.weaponSeq=state.WeaponSequence;info.accessorySeq.AddRange(state.AccessorySequences);info.jewelID.AddRange(state.GemIds);
            info.activeSkillLevel.AddRange(state.ActiveSkills);info.nonActiveSkillLevel.AddRange(state.PassiveSkills);
            info.cardSkillEnchant.AddRange(SkillEnchantSlots(state));
            info.training=InTraining(player,sequence);
            info.effectDataInfoList.AddRange(state.TranscendenceEffects.Select(x=>new PKEffectDataInfo{effectDataId=x.Id,effectDataValue=x.Value,effectGrade=x.Grade}));
        }
        else {info.activeSkillLevel.AddRange([1u,1u]);info.nonActiveSkillLevel.AddRange([1u,1u,1u,1u,1u]);}
        if(info.accessorySeq.Count==0) info.accessorySeq.AddRange([0UL,0UL]);
        if(info.jewelID.Count==0) info.jewelID.AddRange([0u,0u,0u]);
        if(info.cardSkillEnchant.Count==0) info.cardSkillEnchant.AddRange(new uint[7]);
        info.runeID.AddRange([0u,0u,0u,0u,0u,0u]);
        return info;
    }
    private PKGachaStackCountInfo GachaInfo(LocalPlayer player, BscTable.GachaSetData set)
    {
        uint id = (uint)set.GachaCategoryIndex;
        if (!player.Gacha.TryGetValue(id, out var progress)) player.Gacha[id] = progress = new();
        if (set.FreeActivate && (progress.ResetAt == 0 || Now >= progress.ResetAt))
        {
            progress.FreeUsed = 0;
            progress.ResetAt = Now + (ulong)Math.Max(60, set.RegenTime);
        }
        uint max = set.FreeActivate ? (uint)Math.Max(0, set.FreeCountMax) : 0;
        return new PKGachaStackCountInfo { GachaGroupIndex = id, StackCount = progress.Drawn,
            RemainFreeCount = max > progress.FreeUsed ? max - progress.FreeUsed : 0,
            NextFreeGachaTime = max > progress.FreeUsed ? 0 : set.FreeActivate ? progress.ResetAt : 0 };
    }
    private GsGetUserGameInfoOk GameInfo(LocalPlayer player)
    {
        // achieveInfo/hardAchieveInfo/legendAchieveInfo are indexed by stage*2 (stars)
        // and stage*2+1 (best clear time), so they must cover the highest stage id.
        // 客户端把索引夹在 MAX_PVE_STAGE_ID 内，所以普通表里 450..469 的特殊大关不参与定尺寸。
        int maxPve = (int)catalog.MaxPveStage;
        static int TopStage(Dictionary<uint, BscTable.BattleStageLink> stages, int cap) =>
            stages.Keys.Select(x => Math.Min((int)x, cap)).DefaultIfEmpty(0).Max();
        int achieveSize = (Math.Max(TopStage(catalog.Stages, maxPve),
            Math.Max(TopStage(catalog.HardStages, maxPve), TopStage(catalog.LegendStages, maxPve))) + 1) * 2;
        var info = new GsGetUserGameInfoOk {
            challengePoint=player.ChallengeTower.Points,challengeAchieveInfo=TowerAchievements(player),clearChallengeDifficulty=player.ChallengeTower.BestFloor,clearChallengeStage=player.ChallengeTower.BestArea,
            clearDifficultyResourceStage=ResourceClearArray(player),
            settings = player.UserSettings, stamina = player.Stamina, staminaMax = catalog.StaminaMax(player.Level), staminaStamp = player.StaminaStamp, clearStageID = player.ClearStage,
            clearHardStageID = player.ClearHardStage, clearLegendStageID = player.ClearLegendStage,
            coinInfo = Coins(player), namecardInfo = NameCard(player), tutorialStatus = player.TutorialStatus,bsBallUseFlag=player.BsBallUseFlag,
            serverTime = Now, serverTimeZone = ServerTimeZoneMinutes, skillPoint = player.SkillPoint ?? catalog.SkillPointMax(player),skillPointStamp=player.SkillPointStamp,
            clearEpisodeCutSceneID = player.ClearEpisodeCutScene,
            clearHardEpisodeCutSceneID = player.ClearHardEpisodeCutScene,
            clearLegendEpisodeCutSceneID = player.ClearLegendEpisodeCutScene,
            tutorialFlags = Enumerable.Repeat((byte)255, 20).ToArray(),
            // 星级必须来自存档，客户端只认这三个数组（stage*2 = 星级，stage*2+1 = 完美时间）。
            achieveInfo = AchievementArray(player.StageRecords, achieveSize),
            hardAchieveInfo = AchievementArray(player.HardStageRecords, achieveSize),
            legendAchieveInfo = AchievementArray(player.LegendStageRecords, achieveSize),
            dungeonAchieveInfo = AchievementArray(player.DungeonStageRecords,DungeonAchievementSize),
            hardDungeonAchieveInfo = AchievementArray(player.HardDungeonStageRecords,DungeonAchievementSize),
            legendDungeonAchieveInfo = AchievementArray(player.LegendDungeonStageRecords,DungeonAchievementSize),
            normalAchieveRewardInfo = FieldRewardArray(player.NormalFieldRewardClaims),
            hardAchieveRewardInfo = FieldRewardArray(player.HardFieldRewardClaims),
            legendAchieveRewardInfo = FieldRewardArray(player.LegendFieldRewardClaims), paidInfo = new PKPaidAccountInfo()
        };
        info.GachaStackCount.AddRange(AvailableGachaSets.Select(x => GachaInfo(player, x)));
        info.currentCardCollectInfo.AddRange(Collection(player));
        return info;
    }
    private Reply? HandleLocalFeature(string name, object? request, LocalPlayer player, SessionKeyInfo session,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        switch (name)
        {
            // 短期签到信息与签到领奖在 AttendanceFeatures.cs 里实现，这里不再占位。
            case "GS_SET_USER_NEWBIE_FLAG_REQUEST":
                var mode = ((GsSetUserNewbieFlagRequest)request!).newbieType;
                if (mode is not (NEWBIE_FLAG_TYPE.NEWBIE_FLAG_NEWBIE or NEWBIE_FLAG_TYPE.NEWBIE_FLAG_OLDBIE))
                    return fail("invalid tutorial mode", ServerErrorCode.ERROR_INVALID_ID);
                player.NewbieFlag = mode; store.Save(); return ok(null);
            case "GS_WRITE_TUTORIAL_LOG_REQUEST": return ok(null);
            case "GS_GET_USER_GACHA_INFO_REQUEST":
                var info = new GsGetUserGachaInfoOk();
                info.GachaStackCount.AddRange(AvailableGachaSets.Select(x => GachaInfo(player, x))); return ok(info);
            case "GS_VIP_GACHA_LIST_REQUEST":
                return ok(GachaPreview());
            case "GS_GACHA_REQUEST":
                // 寻宝规则（分池/花费/附赠/四星池/重复转碎片）在 GachaFeatures.cs。
                return HandleGacha(name, request, player, session, ok, fail);
            default: return null;
        }
    }
}
