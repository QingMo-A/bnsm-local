using System.Text.Json;
using ProtoBuf;

/// <summary>
/// 签到（月度 + 短期）与运营活动的全部服务端规则。
///
/// 月度签到：客户端自己读 <c>RewardLoginDataTable</c> 渲染格子，但**能否领取、领哪一格、
/// 今天还能不能领**完全由服务端下发的 <c>PKAttendanceInfo</c> 决定。三个状态的确切含义
/// （依据 <c>BscDailyEventPopup.OnEventClickItem</c> 与 <c>PlayerInfoEx.AttendanceRwardStatus</c>）：
///   NONE   —— 今天还没领普档，客户端允许领 <c>loginRewardList[totalAttendanceDay]</c>
///   NORMAL —— 今天已领普档，且该格 <c>VipLevel&gt;0</c> 并达到玩家 VIP，客户端允许**再领一次同一格**
///   VIP    —— 今天没有可领的了
///
/// ⚠️ 一个必须守住的不变式：<c>BscDailyEventPopup.DoHide</c> 的守卫是
/// <c>status != NONE &amp;&amp; !currentShortTermAttendanceRewardFalg</c> 才允许关窗。
/// 也就是说只要下发 NONE，客户端就认为"月度还有东西没领"，关闭按钮会被隐藏、窗口关不掉。
/// 因此**宁可下发 VIP 也绝不能在无奖可领时下发 NONE**，否则玩家会被卡在签到窗里。
/// </summary>
internal sealed class AttendanceRules
{
    public sealed class RewardSpec
    {
        public string Type { get; set; } = "";
        public uint Id { get; set; }
        public int Count { get; set; }
    }

    public sealed class DaySpec
    {
        public List<RewardSpec> Rewards { get; set; } = [];
    }

    public sealed class OperationSpec
    {
        public uint Id { get; set; }
        public string Type { get; set; } = "";
        public int Value { get; set; }
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }

    private sealed class ShortTermSpec
    {
        public string Title { get; set; } = "";
        public List<DaySpec> Days { get; set; } = [];
    }

    private sealed class OperationGroup
    {
        public List<OperationSpec> Items { get; set; } = [];
    }

    private sealed class Root
    {
        public ShortTermSpec ShortTerm { get; set; } = new();
        public OperationGroup Operations { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<DaySpec> ShortTermDays { get; }
    public IReadOnlyList<OperationSpec> Operations { get; }
    /// <summary>月度签到表：客户端渲染用的同一份原始表，服务端据此决定发什么。</summary>
    public BscTable.RewardLoginDataTable? MonthlyRewards { get; }

    public AttendanceRules(string root)
    {
        string path = Path.Combine(root, "LocalServer/data/attendance.json");
        Root config = File.Exists(path)
            ? JsonSerializer.Deserialize<Root>(File.ReadAllText(path), Json) ?? new Root()
            : new Root();
        ShortTermDays = config.ShortTerm.Days;
        Operations = config.Operations.Items;
        MonthlyRewards = ReadTable<BscTable.RewardLoginDataTable>(root, "RewardLoginDataTable");
    }

    private static T? ReadTable<T>(string root, string name) where T : class
    {
        string path = Path.Combine(root, "LocalServer/data/tables", name + ".pb");
        if (!File.Exists(path)) return null;
        using var input = File.OpenRead(path);
        return Serializer.Deserialize<T>(input);
    }

    /// <summary>UTC+8 的日序号，与 <c>RefreshTime</c> 里的日界一致。</summary>
    public static ulong DayIndex(ulong now) => (now + 8 * 3600) / 86400;

    internal bool ResourceAllOpen(ulong now)
    {
        // Match PlayerInfoEx.GetOperationValue: first matching operation, inclusive
        // absolute dates in server time (UTC+8). The client receives these same specs.
        var spec = Operations.FirstOrDefault(x => EffectiveOperation(x) && x.Type is "RESOURCE_STAGE_ALL_OPEN" or "EVENT_OPERATION_TYPE_RESOURCE_STAGE_ALL_OPEN");
        if (spec == null) return false;
        const string format = "yyyy-MM-dd HH:mm:ss";
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!DateTime.TryParseExact(spec.Start, format, culture, System.Globalization.DateTimeStyles.None, out var start) ||
            !DateTime.TryParseExact(spec.End, format, culture, System.Globalization.DateTimeStyles.None, out var end)) return false;
        var serverTime = DateTimeOffset.FromUnixTimeSeconds((long)now).UtcDateTime.AddHours(8);
        return start <= serverTime && serverTime <= end;
    }

    // The recovery seed used an 80-year all-open placeholder. It is not a live
    // limited event. Keep real dated events without altering packaged catalogs.
    internal static bool EffectiveOperation(OperationSpec spec)
    {
        if (OperationName(spec.Type) is "NORMAL_BATTLE_ITEM_DROP" or "HARD_BATTLE_ITEM_DROP" or "LEGEND_BATTLE_ITEM_DROP")
            return spec.Value is >= 2 and <= 150 && TryOperationWindow(spec, out _, out _);
        if(spec.Type is not ("RESOURCE_STAGE_ALL_OPEN" or "EVENT_OPERATION_TYPE_RESOURCE_STAGE_ALL_OPEN"))return true;
        return TryOperationWindow(spec,out var a,out var b)&&(b-a).TotalDays<=366;
    }

    private static string OperationName(string type) => type.StartsWith("EVENT_OPERATION_TYPE_", StringComparison.Ordinal)
        ? type.Substring("EVENT_OPERATION_TYPE_".Length) : type;

    private static bool TryOperationWindow(OperationSpec spec, out DateTime start, out DateTime end)
    {
        const string format = "yyyy-MM-dd HH:mm:ss";
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var style = System.Globalization.DateTimeStyles.None;
        bool startValid = DateTime.TryParseExact(spec.Start, format, culture, style, out start);
        return DateTime.TryParseExact(spec.End, format, culture, style, out end) && startValid && end >= start;
    }

    internal int AdventureDropChanceMultiplier(BscTable.GAME_DIFFICULTY difficulty, bool dungeon, ulong now)
    {
        string type = dungeon ? "HARD_BATTLE_ITEM_DROP" : difficulty switch {
            BscTable.GAME_DIFFICULTY.DIFFICULTY_NORMAL => "NORMAL_BATTLE_ITEM_DROP",
            BscTable.GAME_DIFFICULTY.DIFFICULTY_HARD => "HARD_BATTLE_ITEM_DROP",
            BscTable.GAME_DIFFICULTY.DIFFICULTY_LEGEND => "LEGEND_BATTLE_ITEM_DROP",
            _ => ""
        };
        if (type.Length == 0) return 1;
        // Match the first published row and inclusive UTC+8 window used by the client.
        var spec = Operations.FirstOrDefault(x => OperationName(x.Type) == type && EffectiveOperation(x));
        if (spec == null || !TryOperationWindow(spec, out var start, out var end)) return 1;
        var time = DateTimeOffset.FromUnixTimeSeconds((long)now).UtcDateTime.AddHours(8);
        return start <= time && time <= end ? spec.Value : 1;
    }

    /// <summary>
    /// 按 UTC+8 当前年月找当月签到行；表止于某年时回退到表内最后一行，
    /// 避免跨年后签到整个失效（客户端自己按年月匹配，补表由 tools/patch-attendance-table.py 负责）。
    /// </summary>
    public BscTable.RewardLoginData? MonthlyRow(ulong now)
    {
        if (MonthlyRewards == null || MonthlyRewards.List.Count == 0) return null;
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)now).UtcDateTime.AddHours(8);
        BscTable.RewardLoginData? fallback = null;
        foreach (var row in MonthlyRewards.List)
        {
            if (!DateTime.TryParse(row.OpenDate, out var open)) continue;
            if (open.Year == local.Year && open.Month == local.Month) return row;
            // 表按月份升序，最后一条就是最近的模板
            if (fallback == null || open > DateTime.Parse(fallback.OpenDate)) fallback = row;
        }
        return fallback;
    }

    public static BscTable.REWARD_TYPE RewardType(string name) => name?.ToUpperInvariant() switch
    {
        "GOLD" => BscTable.REWARD_TYPE.REWARD_TYPE_GOLD,
        "CASH" => BscTable.REWARD_TYPE.REWARD_TYPE_CASH,
        "STAMINA" => BscTable.REWARD_TYPE.REWARD_TYPE_STAMINA,
        "ITEM" => BscTable.REWARD_TYPE.REWARD_TYPE_ITEM,
        _ => BscTable.REWARD_TYPE.REWARD_NONE
    };
}

internal sealed partial class ProtocolRouter
{
    private AttendanceRules? attendanceRules;
    private AttendanceRules Attendance => attendanceRules ??= new AttendanceRules(RootDirectory);

    private ulong Today => AttendanceRules.DayIndex(Now);

    /// <summary>当月签到表行；表缺失时返回空列表，签到整体降级为"只做短期"。</summary>
    private IReadOnlyList<BscTable.LoginRewardInfo> MonthlyRewards =>
        Attendance.MonthlyRow(Now)?.LoginRewardList ?? (IReadOnlyList<BscTable.LoginRewardInfo>)Array.Empty<BscTable.LoginRewardInfo>();

    private uint PlayerVipGrade(LocalPlayer p) => (uint)catalog.VipFor(p.VipExp).VipLv;

    /// <summary>
    /// 今天是否已经领过普档。存日序号而不是时间戳，跨日比较不受时区/分钟差影响。
    /// </summary>
    private bool MonthlyClaimedToday(LocalPlayer p) => p.AttendanceLastDay == Today;

    private bool ShortTermAvailable(LocalPlayer p) =>
        p.ShortTermDays < (uint)Attendance.ShortTermDays.Count && p.ShortTermLastDay != Today;

    private AttendanceRewardStatus AttendanceStatus(LocalPlayer p)
    {
        if (!MonthlyClaimedToday(p)) return AttendanceRewardStatus.ATTENDANCE_REWARD_NONE;
        // 今天的 VIP 加成档已经领过 → 今天没有可领的了
        if (p.AttendanceVipClaimedDay == Today) return AttendanceRewardStatus.ATTENDANCE_REWARD_VIP;
        // 每次现算而不用存档标记：玩家可能在领完普档之后才把 VIP 升上来
        var rows = MonthlyRewards;
        int index = (int)p.AttendanceDays - 1;
        bool eligible = index >= 0 && index < rows.Count && rows[index].VipLevel > 0 && rows[index].VipLevel <= PlayerVipGrade(p);
        return eligible ? AttendanceRewardStatus.ATTENDANCE_REWARD_NORMAL
                        : AttendanceRewardStatus.ATTENDANCE_REWARD_VIP;
    }

    private PKAttendanceInfo AttendanceInfo(LocalPlayer p) => new()
    {
        totalAttendanceDay = p.AttendanceDays,
        latestRewardTime = p.AttendanceLastTime,
        attendanceRwardStatus = AttendanceStatus(p),
        totalShortTermAttendanceDay = p.ShortTermDays,
        currentShortTermAttendanceRewardFalg = ShortTermAvailable(p)
    };

    private GsGetShortTermAttendanceRewardInfoOk ShortTermInfo(LocalPlayer p)
    {
        var days = Attendance.ShortTermDays;
        var result = new GsGetShortTermAttendanceRewardInfoOk
        {
            totalShortTermAttendanceDay = p.ShortTermDays,
            // 客户端用这个时间算 isNewUser = endTime.Year > now.Year + 5，
            // 给一年后即可稳定落在"老玩家"分支。
            shortTermAttendanceEndTime = Now + 365UL * 24 * 3600
        };
        for (int i = 0; i < days.Count; i++)
        {
            var first = days[i].Rewards.FirstOrDefault();
            result.rewardInfoList.Add(new PKShortTermAttendanceRewardItemInfo
            {
                rewardItemType = AttendanceRules.RewardType(first?.Type ?? ""),
                rewardItemData = first?.Id ?? 0,
                rewardItemCount = (uint)Math.Max(0, first?.Count ?? 0),
                // 客户端拿 accrueCount 与 TotalShortTermAttendanceDay 比对，必须等于格子序号+1
                accrueCount = (uint)(i + 1),
                rewardFlag = (uint)(i + 1) <= p.ShortTermDays
            });
        }
        return result;
    }

    private GsGetOperationEventDataOk OperationEvents()
    {
        var result = new GsGetOperationEventDataOk();
        foreach (var spec in Attendance.Operations)
        {
            if(!AttendanceRules.EffectiveOperation(spec))continue;
            // 配置里写短名（NORMAL_BATTLE_ITEM_DROP）或全名都能识别
            string enumName = spec.Type.StartsWith("EVENT_OPERATION_TYPE_", StringComparison.Ordinal)
                ? spec.Type
                : "EVENT_OPERATION_TYPE_" + spec.Type;
            if (!Enum.TryParse<BscTable.EVENT_OPERATION_TYPE>(enumName, out var type) ||
                type == BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_NONE) continue;
            result.operationDataList.Add(new BscTable.OperationData
            {
                Id = (int)spec.Id,
                OperationType = type,
                Value = spec.Value,
                StartDate = spec.Start,
                EndDate = spec.End,
                Relativity = false
            });
            result.totalCount++;
        }
        // currentCount 在原服是"已领取/已完成的活动数"。本地活动没有领取动作，等于开启数。
        result.currentCount = result.totalCount;
        return result;
    }

    /// <summary>把一条奖励记到玩家身上，并回填到领奖回包。</summary>
    private void ApplyReward(LocalPlayer p, BscTable.REWARD_TYPE type, uint id, int count, GsReceiveAttendanceRewardOk reply)
    {
        if (count <= 0) return;
        switch (type)
        {
            case BscTable.REWARD_TYPE.REWARD_TYPE_GOLD:
                p.Gold = (uint)Math.Min(uint.MaxValue, (ulong)p.Gold + (ulong)count);
                reply.earnedCoinInfo.gold += (uint)count;
                break;
            case BscTable.REWARD_TYPE.REWARD_TYPE_CASH:
                p.CashPoint = (uint)Math.Min(uint.MaxValue, (ulong)p.CashPoint + (ulong)count);
                reply.earnedCoinInfo.cashPoint += (uint)count;
                break;
            case BscTable.REWARD_TYPE.REWARD_TYPE_STAMINA:
                uint max = catalog.StaminaMax(p.Level);
                uint add = p.Stamina >= max ? 0 : Math.Min((uint)count, max - p.Stamina);
                p.Stamina += add;
                break;
            case BscTable.REWARD_TYPE.REWARD_TYPE_ITEM:
                long total = (long)p.Inventory.GetValueOrDefault(id) + count;
                if (total > int.MaxValue) total = int.MaxValue;
                p.Inventory[id] = (int)total;
                reply.earnedItemList.Add(ItemInfo(id, count));
                break;
            default:
                // CARD / RUNE / TITLE 等本地不投放的档位：跳过但不阻断领取，
                // 否则月度签到碰到一格不支持的奖励就整条链停住。
                break;
        }
    }

    private Reply? HandleAttendance(string name, object? request, LocalPlayer p, Func<object?, Reply> ok,
        Func<string, ServerErrorCode, Reply> fail)
    {
        switch (name)
        {
            case "GS_GET_SHORTTERM_ATTENDANCE_REWARD_INFO_REQUEST":
                return ok(ShortTermInfo(p));
            case "GS_GET_OPERATION_EVENT_DATA_REQUEST":
                return ok(OperationEvents());
            case "GS_RECEIVE_ATTENDANCE_REWARD_REQUEST":
                return ReceiveAttendance((GsReceiveAttendanceRewardRequest)request!, p, ok, fail);
            default:
                return null;
        }
    }

    private Reply ReceiveAttendance(GsReceiveAttendanceRewardRequest request, LocalPlayer p,
        Func<object?, Reply> ok, Func<string, ServerErrorCode, Reply> fail)
    {
        if (request.attendanceType == AttendanceType.ATENDANDE_SHORT)
        {
            if (!ShortTermAvailable(p)) return fail("short-term attendance reward is not available today", ServerErrorCode.ERROR_OVER_MAX_COUNT);
            uint day = p.ShortTermDays + 1;
            var spec = Attendance.ShortTermDays[(int)p.ShortTermDays];
            var reply = new GsReceiveAttendanceRewardOk
            {
                attendanceType = AttendanceType.ATENDANDE_SHORT,
                earnedCoinInfo = new PKCoinInfo()
            };
            foreach (var reward in spec.Rewards)
                ApplyReward(p, AttendanceRules.RewardType(reward.Type), reward.Id, reward.Count, reply);
            p.ShortTermDays = day;
            p.ShortTermLastDay = Today;
            p.AttendanceLastTime = Now;
            reply.attendanceInfo = AttendanceInfo(p);
            reply.coinInfo = Coins(p);
            store.Save();
            return ok(reply);
        }

        if (request.attendanceType != AttendanceType.ATENDANDE_MONTH)
            return fail("unknown attendance type", ServerErrorCode.ERROR_INVALID_ID);

        var rows = MonthlyRewards;
        if (rows.Count == 0) return fail("monthly attendance table is unavailable", ServerErrorCode.ERROR_INVALID_ID);

        var status = AttendanceStatus(p);
        bool vipPass = status == AttendanceRewardStatus.ATTENDANCE_REWARD_NORMAL;
        int index = vipPass ? (int)p.AttendanceDays - 1 : (int)p.AttendanceDays;
        if (index < 0 || index >= rows.Count)
            return fail("no monthly attendance reward left", ServerErrorCode.ERROR_OVER_MAX_COUNT);
        if (!vipPass && status != AttendanceRewardStatus.ATTENDANCE_REWARD_NONE)
            return fail("today's monthly attendance reward is already claimed", ServerErrorCode.ERROR_OVER_MAX_COUNT);

        var row = rows[index];
        var monthReply = new GsReceiveAttendanceRewardOk
        {
            attendanceType = AttendanceType.ATENDANDE_MONTH,
            earnedCoinInfo = new PKCoinInfo(),
            rewardItemType = row.RewardIdType,
            rewardItemData = (uint)Math.Max(0, row.RewardId),
            rewardItemCount = (uint)Math.Max(0, row.ItemQuantity)
        };
        ApplyReward(p, row.RewardIdType, (uint)Math.Max(0, row.RewardId), row.ItemQuantity, monthReply);

        if (vipPass)
        {
            // 第二档：同一条奖励再发一次，记下今天的 VIP 档已领
            p.AttendanceVipClaimedDay = Today;
        }
        else
        {
            p.AttendanceDays++;
            p.AttendanceLastDay = Today;
        }
        p.AttendanceLastTime = Now;
        monthReply.attendanceInfo = AttendanceInfo(p);
        monthReply.coinInfo = Coins(p);
        store.Save();
        return ok(monthReply);
    }
}
