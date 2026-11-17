internal static class LocalFailureMessages
{
    internal static FailPacket Create(string command,string reason,ServerErrorCode code)
    {
        string text=code switch {
            ServerErrorCode.ERROR_INVALID_USER_LEVEL=>"玩家等级尚未达到该功能的开放要求。",
            ServerErrorCode.ERROR_INVALID_TERRITORY_ID=>"一统天下领地信息已变化，请返回大厅重新选择。",
            ServerErrorCode.ERROR_INVALID_TERRITORY_ENTRANCE_LEVEL=>"玩家等级不符合该一统天下领地的进入范围，请选择对应等级的领地。",
            ServerErrorCode.ERROR_NOT_REGISTER_DEFENCE_DECK=>"请先配置一统天下的三组防御卡组，共十五张不重复的可用卡牌。",
            ServerErrorCode.ERROR_WORLD_WAR_BREAK_TIME=>"一统天下当前为每日休战时段，请在开放后再战。",
            ServerErrorCode.ERROR_ALREADY_BATTLED_WITH_WANTED_USER=>"今日已挑战过该通缉对手，请选择其他对手。",
            ServerErrorCode.ERROR_NOT_EXIST_PVP_AI_DECK=>"对手信息已更新，请返回大厅重新搜索。",
            ServerErrorCode.ERROR_NOT_ENOUGH_REVIVAL_COUNT=>"今日一统天下的恢复次数已用完。",
            ServerErrorCode.ERROR_INVALID_BATTLE_STATUS_FLAG=>"战斗准备信息已变化，请重新打开选卡界面后开始战斗。",
            ServerErrorCode.ERROR_INVALID_VIP_LEVEL=>"VIP等级尚未达到该地块的开放要求。",
            ServerErrorCode.ERROR_INVALID_CASTLE_DATA=>"城池信息无效，请返回攻城战地图重新选择。",
            ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_MAP_SPOT_INFO=>"该城池当前未被此城主占领，请刷新地图后选择已占领的城池。",
            ServerErrorCode.ERROR_INVALID_CASTLE_BATTLE_CARD=>"入侵卡组中有已阵亡的卡牌，请更换卡牌，或使用选卡界面的恢复功能后再战。",
            ServerErrorCode.ERROR_INVALID_SIDE_DECK_CARD_INFO=>"备用卡组无效，请检查是否有重复、已上阵、修炼中或超出数量限制的卡牌。",
            ServerErrorCode.ERROR_USED_BAN_CARD=>"卡组中有本层永久禁用或今日禁用的卡牌，请查看挑战塔禁用列表并更换。",
            ServerErrorCode.ERROR_INVALID_STAGE_ID=>"关卡信息无效，请返回对应模式列表后重新选择。",
            ServerErrorCode.ERROR_NO_OPEN_FLOOR=>"挑战塔该楼层尚未解锁，请先通关前一层。",
            ServerErrorCode.ERROR_ALREADY_CLEARED_CHALLENGE_STAGE=>"挑战塔该区域今日已通关，请选择下一个区域。",
            ServerErrorCode.ERROR_NOT_ENOUGH_STEP_CONDITION=>"请按顺序完成当前楼层前面的区域，再挑战此区域。",
            ServerErrorCode.ERROR_INVALID_OVER_ENTER_COUNT=>"今日挑战塔的进入次数已用完。",
            ServerErrorCode.ERROR_FRIEND_RAID_INFO_MISSING=>"请先在自己的攻城战地图探索该魔物地块，再召唤魔物。",
            ServerErrorCode.ERROR_ALREADY_RAID_BOSS_EXISTS=>"该地块已有魔物，请先完成本次讨伐。",
            ServerErrorCode.ERROR_ALREADY_COMPLETE_FRIEND_RAID=>"本次魔物挑战已完成，请刷新攻城战地图查看当前状态。",
            ServerErrorCode.ERROR_INVALID_FRIEND_RAID_INFO=>"魔物状态已更新，请返回攻城战地图后重新打开。",
            ServerErrorCode.ERROR_NOT_ENOUGH_STAMINA=>"体力不足，请补充体力后重试。",
            ServerErrorCode.ERROR_NOT_ENOUGH_GOLD=>"金币不足。",
            ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT=>"钻石不足。",
            ServerErrorCode.ERROR_NOT_ENOUGH_ARENA_POINT=>"比武币不足，可通过比武玩法和排名结算获取。",
            ServerErrorCode.ERROR_NOT_ENOUGH_RANKING_POINT=>"武林币不足。",
            ServerErrorCode.ERROR_INVALID_GIFT_BOX_REWARD_INFO=>"这份邮件已经领取，请刷新礼物箱。",
            ServerErrorCode.ERROR_RESOURCE_DUNGEON_NOT_OPEN=>"该关卡尚未到开放时间，请按列表中的开放日期和时段挑战。",
            ServerErrorCode.ERROR_CARD_IN_TRAINING=>"出战卡组中有修炼中的卡牌，请先调整卡组。",
            ServerErrorCode.ERROR_INVALID_CARD_SEQUENCE or ServerErrorCode.ERROR_INVALID_DECK_CARD_INFO=>"出战卡组无效，请重新选择已有、未修炼的卡牌。",
            ServerErrorCode.ERROR_INVALID_USER_BATTLE_STATUS=>"上一场战斗尚未结束，请返回该模式大厅结束未完成的战斗。",
            ServerErrorCode.ERROR_BATTLE_RESULT_VERIFY=>"战斗结算数据校验未通过，请导出日志以便排查。",
            ServerErrorCode.ERROR_BIG_BOSS_BATTLE_COOLTIME or ServerErrorCode.ERROR_WORLD_BOSS_BATTLE_COOLTIME=>"魔王战正在冷却，请等待倒计时结束后再挑战。",
            ServerErrorCode.ERROR_BOSS_IS_ALREADY_CLEARED=>"这只魔王已被击败，请选择其他魔王或刷新列表。",
            ServerErrorCode.ERROR_NOT_CLEARED_REQUIRE_STAGE=>"尚未通关此节点要求的主线关卡，请先完成对应的困难主线。",
            ServerErrorCode.ERROR_INVALID_NORMAL_BATTLE_STAGE=>"关卡信息已变化或尚未解锁，请返回关卡列表刷新。",
            ServerErrorCode.ERROR_OVER_MAX_COUNT=>"本日可用次数已用完。",
            ServerErrorCode.ERROR_UNKNOWN_COMMAND=>"这个操作尚未恢复，请关闭提示后使用其他功能。",
            _=>"操作未完成，请返回当前界面刷新后重试。若反复出现，请导出游戏和服务端日志。"
        };
        if(reason.Contains("storage",StringComparison.OrdinalIgnoreCase)||reason.Contains("capacity",StringComparison.OrdinalIgnoreCase))text="奖励空间不足，请先整理背包或卡牌容量，再领取奖励。";
        if(reason.Contains("reset opportunities"))text="今日闯荡江湖的重置次数已用完。";
        if(reason.Contains("reset the journey before changing"))text="当前还有未结束的闯荡江湖进度，请先继续或重置当前难度。";
        if(reason.Contains("dead card")||reason.Contains("fallen wander"))text="卡组中有本轮闯荡江湖已阵亡的卡牌，请替换后再战；重置旅程后会恢复。";
        if(reason.Contains("world-war attack deck contains dead cards"))text="一统天下卡组中有已阵亡的卡牌，请更换卡牌，或使用选卡界面的恢复功能后再战。";
        if(reason.Contains("world-war attack deck needs five"))text="一统天下进攻卡组需要五张不重复的可用卡牌，请补齐后再战。";
        if(reason.Contains("tower deck contains fallen card"))text="卡组中有本轮挑战塔已阵亡的卡牌，请更换后再战。";
        if(reason.Contains("no matching")||reason.Contains("ended or abandoned")||reason.Contains("stale battle"))text="这场战斗已结束或已退出，请返回模式大厅重新开战。";
        if(reason.Contains("training cards"))text="卡组中包含缺失或修炼中的卡牌，请重新选择出战卡牌。";
        if(reason.Contains("instant castle action requires")||reason.Contains("castle encounter changed")||reason.Contains("castle preview belongs"))text="所选城池信息已更新，请关闭选卡界面后重新打开该城池。";
        if(reason.Contains("visible neighbouring hex"))text="请先探索已显示的相邻地块，再探索这里。";
        if(reason.Contains("castle monster requires its linked occupied castle"))text="请先占领并恢复与该魔物地块相连的城池。";
        if(reason.Contains("castle monster daily summon limit"))text="今日该地块的魔物召唤次数已用完。";
        if(reason.Contains("castle monster summon materials missing"))text="魔物召唤材料不足。";
        if(reason.Contains("castle monster phase changed or final battle requires owner"))text="魔物阶段已更新；最终战只能由城池主人挑战。";
        if(reason.Contains("castle monster participation already completed"))text="你已完成本次协力战挑战，请邀请其他好友协力讨伐。";
        if(reason=="castle monster help active battle")text="当前有人正在挑战这只魔物，请等战斗结束或退出后再邀请人机好友。";
        if(reason=="castle monster help friend unavailable")text="所选人机好友已助战，或等级、卡组不符合此幻魔洞要求，请重新打开求助界面。";
        if(reason=="castle monster help summon limit")text="本次召唤的人机好友协力名额已用完。";
        if(reason=="castle monster help invalid selection")text="请选择一名或多名不同的人机好友。";
        if(reason.StartsWith("castle level locked:")){var required=reason.Split("required=")[1].Split(' ')[0];text="此城池需要玩家等级达到"+required+"级后才能入侵或挑战。";}
        if(reason.Contains("castle visit missing"))text="尚未选定城主，请返回攻城战地图搜索对手后再选择城池。";
        if(reason.Contains("castle already plundered"))text="本次访问中已入侵过此城池，请选择其他城池。";
        if(reason.Contains("castle encounter unavailable"))text="攻城战准备状态已变化，请关闭选卡界面并重新打开城池；若已有战斗，请先正常退出该场战斗。";
        if(reason.Contains("castle native roster restrictions"))text="卡组不符合该城池的阵营或职业限制，请按选卡界面上方的条件更换卡牌。";
        if(reason.Contains("castle defence mercenary unavailable"))text="该雇佣卡当前不可用，请先使用自己的卡牌配置城池防守。";
        if(reason.Contains("world boss settlement mismatch:"))text="幻魔战结算与当前战斗不一致，请返回幻魔战列表后重新进入；若正常打完仍出现，请导出游戏和服务端日志。";
        if(reason.Contains("old world boss completion"))text="这是上一场幻魔战的重复结算回复，当前战斗不受影响。";
        if(reason.Contains("guild battle cooldown"))text="门派对抗战仍在冷却，请等待倒计时结束或使用重置功能。";
        if(reason.Contains("guild volunteer unavailable"))text="该同门助战卡今日已使用，或已不在同一门派，请更换卡牌。";
        if(reason.Contains("guild battle offer changed"))text="门派对手信息已变化，请返回门派对抗战重新选择。";
        if(command=="GS_HELPER_COSTUME_CRAFT_REQUEST") {
            if(code==ServerErrorCode.ERROR_INVALID_CRAFT_REQUEST)text="该服装已达强化上限，或所选强化路线无效，请重新选择服装。";
            if(code==ServerErrorCode.ERROR_NOT_ENOUGH_ITEM_COUNT)text="同款服装副本或布料不足，请查看强化界面的材料需求。";
        }
        if(command=="GS_RECEIVE_PROMOTION_REWARD_REQUEST"&&code==ServerErrorCode.ERROR_OVER_MAX_COUNT&&!reason.Contains("storage"))text="活动任务尚未完成，或该档奖励已经领取，请刷新活动进度。";
        if (reason==LocalCardRegistry.UniversalFragmentsDisabledMessage)text=reason;
        if (reason.StartsWith("先完成「", StringComparison.Ordinal) ||
            reason is "先手动击败该魔王并通关本篇，才能扫荡" or "今日该魔王的扫荡次数不足" or
                "请先通关老小篇，碎片用于升星" or "扫荡参数无效" or "奖励超过库存上限")
            text=reason+"。";
        string id=((uint)code)+"/"+command.Replace("GS_","").Replace("_REQUEST","");
        return new FailPacket{errorCode=(uint)code,message="BNSM/1\n"+text+"\n问题编号："+id+"\n诊断："+reason};
    }
}
