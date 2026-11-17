using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;

/// <summary>
/// 签到（月度 + 短期）与运营活动的端到端检查。
///
/// 覆盖的关键不变式：
///   1. 没有可领奖励时**不能**下发 NONE，否则客户端签到窗关不掉（DoHide 守卫）；
///   2. 同一天不能重复领月度普档或短期签到；
///   3. 跨日后重新可领；
///   4. 短期签到 accrueCount 必须等于格子序号 + 1，否则格子状态错位；
///   5. 月度表必须覆盖当前年份，否则客户端列表空白。
/// </summary>
internal static class AttendanceTest
{
    internal static async Task Run(string root)
    {
        string production = Path.Combine(root, "LocalServer/saves/players.json");
        byte[]? original = File.Exists(production) ? SHA256.HashData(File.ReadAllBytes(production)) : null;
        string save = Path.Combine(root, "verification/attendance", Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        void Assert(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        T Read<T>(byte[] data) => (T)WireCodec.Deserialize(typeof(T), data)!;
        var codec = new WireCodec(JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(root, "LocalServer/data/packet-keys.json")))!);

        // 表必须在进入游戏前就能覆盖当前年份
        var rules = new AttendanceRules(root);
        var row = rules.MonthlyRow(ProtocolRouter.Now);
        Assert(row != null, "月度签到表可读取");
        Assert(DateTime.Parse(row!.OpenDate).Year >= DateTimeOffset.FromUnixTimeSeconds((long)ProtocolRouter.Now).UtcDateTime.AddHours(8).Year,
            "月度签到表覆盖当前年份（否则客户端列表空白且窗口关不掉）");
        Assert(row.LoginRewardList.Count > 0, "月度签到表当月有奖励条目");
        Assert(rules.ShortTermDays.Count > 0, "短期签到配置已加载");

        await using var server = new LocalRuntime(root, save, 0, 0, 0);
        var c = server.Catalog;
        async Task<(ushort Command, byte[] Payload)> Send(TcpClient socket, ushort command, object message)
        {
            await socket.GetStream().WriteAsync(codec.Encode(command, WireCodec.Serialize(message), true));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var frame = (await WireCodec.ReadFrame(socket.GetStream(), deadline.Token))!;
            Assert(frame.Length <= 8192, "Original frame size respected for " + (GameServerCommand)command);
            var result = codec.Decode(frame, true); return (result.Command, result.Payload);
        }
        using var login = new TcpClient(); await login.ConnectAsync("127.0.0.1", server.LoginPort);
        await Send(login, (ushort)LoginServerCommand.LS_LOGIN_REQUEST, new LsLoginRequest { userID = "attendance-test", userPW = "local", osType = DeviceOSType.DEVICE_PC });
        var account = Read<LsGameStartOk>((await Send(login, (ushort)LoginServerCommand.LS_GAME_START_REQUEST, new LsGameStartRequest { serverId = 1, bAgreement = true })).Payload);
        var key = new SessionKeyInfo { uid = account.uid, loginSessionKey = account.loginSessionKey };
        using var game = new TcpClient(); await game.ConnectAsync("127.0.0.1", server.GamePort);
        async Task<byte[]> Request(GameServerCommand command, object message, bool success = true)
        {
            key.sequenceNo++;
            var response = await Send(game, (ushort)command, message);
            string expected = command.ToString().Replace("_REQUEST", success ? "_OK" : "_FAIL");
            if (((GameServerCommand)response.Command).ToString() != expected)
                throw new Exception(expected + ": " + Read<FailPacket>(response.Payload).message);
            return response.Payload;
        }
        var p = server.Store.Find(account.uid)!;
        async Task<PKAttendanceInfo> Entry()
        {
            var ok = Read<GsCheckGameEntryOk>(await Request(GameServerCommand.GS_CHECK_GAME_ENTRY_REQUEST, new GsCheckGameEntryRequest { keyInfo = key }));
            return ok.userCheckGameEntryData!.attendanceInfo!;
        }
        async Task<GsGetShortTermAttendanceRewardInfoOk> ShortInfo() =>
            Read<GsGetShortTermAttendanceRewardInfoOk>(await Request(GameServerCommand.GS_GET_SHORTTERM_ATTENDANCE_REWARD_INFO_REQUEST, new GsGetShortTermAttendanceRewardInfoRequest { keyInfo = key }));
        async Task<GsReceiveAttendanceRewardOk> Claim(AttendanceType type) =>
            Read<GsReceiveAttendanceRewardOk>(await Request(GameServerCommand.GS_RECEIVE_ATTENDANCE_REWARD_REQUEST, new GsReceiveAttendanceRewardRequest { keyInfo = key, attendanceType = type }));

        // ── 初始状态：月度可领（NONE）+ 短期可领 ──
        var entry = await Entry();
        Assert(entry.totalAttendanceDay == 0 && entry.attendanceRwardStatus == AttendanceRewardStatus.ATTENDANCE_REWARD_NONE,
            "新存档下月度签到为 NONE（可领）");
        Assert(entry.currentShortTermAttendanceRewardFalg && entry.totalShortTermAttendanceDay == 0, "短期签到当天可领");

        var shortInfo = await ShortInfo();
        Assert(shortInfo.rewardInfoList.Count == rules.ShortTermDays.Count, "短期签到格子数与配置一致");
        Assert(shortInfo.rewardInfoList.Select((x, i) => (int)x.accrueCount == i + 1).All(x => x),
            "accrueCount 等于格子序号+1（客户端据此判断已领格）");
        Assert(shortInfo.rewardInfoList.All(x => x.rewardItemType != BscTable.REWARD_TYPE.REWARD_NONE),
            "每个短期签到格子都有可识别的奖励类型");
        Assert(shortInfo.shortTermAttendanceEndTime > ProtocolRouter.Now, "短期签到结束时间在未来");

        // ── 短期签到：首日可领、当天不可重复 ──
        uint goldBefore = p.Gold;
        int firstReward = rules.ShortTermDays[0].Rewards.First(x => x.Type.Equals("GOLD", StringComparison.OrdinalIgnoreCase)).Count;
        var first = await Claim(AttendanceType.ATENDANDE_SHORT);
        Assert(p.ShortTermDays == 1, "短期签到天数 +1");
        Assert(p.Gold == goldBefore + (uint)firstReward, "短期签到第 1 天按配置发放金币");
        Assert(!first.attendanceInfo!.currentShortTermAttendanceRewardFalg, "领完后当天不再显示可领");
        await Request(GameServerCommand.GS_RECEIVE_ATTENDANCE_REWARD_REQUEST,
            new GsReceiveAttendanceRewardRequest { keyInfo = key, attendanceType = AttendanceType.ATENDANDE_SHORT }, success: false);
        Assert(p.ShortTermDays == 1, "同一天重复领短期签到被拒绝");

        // ── 月度签到：普档领取，同一天不能再领 ──
        goldBefore = p.Gold;
        var monthRow = rules.MonthlyRow(ProtocolRouter.Now)!.LoginRewardList[0];
        var monthly = await Claim(AttendanceType.ATENDANDE_MONTH);
        Assert(p.AttendanceDays == 1, "月度签到已领格数 +1");
        if (monthRow.RewardIdType == BscTable.REWARD_TYPE.REWARD_TYPE_GOLD)
            Assert(p.Gold >= goldBefore, "月度普档发放了金币");
        Assert(monthly.attendanceInfo!.attendanceRwardStatus != AttendanceRewardStatus.ATTENDANCE_REWARD_NONE,
            "领完普档后不得再下发 NONE（否则客户端签到窗关不掉）");
        await Request(GameServerCommand.GS_RECEIVE_ATTENDANCE_REWARD_REQUEST,
            new GsReceiveAttendanceRewardRequest { keyInfo = key, attendanceType = AttendanceType.ATENDANDE_MONTH }, success: false);
        Assert(p.AttendanceDays == 1, "同一天重复领月度普档被拒绝");

        // ── VIP 加成档：领完普档之后才升 VIP，也必须能补领（不能把当时不够 VIP 固化下来）──
        p.VipExp = 1_000_000;   // 拉满 VIP，让第一格 VipLevel 一定达标
        var vipEntry = await Entry();
        uint grade = (uint)c.VipFor(p.VipExp).VipLv;
        bool firstRowHasVip = monthRow.VipLevel > 0 && monthRow.VipLevel <= grade;
        if (firstRowHasVip)
        {
            Assert(vipEntry.attendanceRwardStatus == AttendanceRewardStatus.ATTENDANCE_REWARD_NORMAL,
                "普档领完后升 VIP 仍切到 NORMAL（客户端据此亮 VIP 档）");
            uint beforeVip = p.AttendanceDays;
            var vipClaim = await Claim(AttendanceType.ATENDANDE_MONTH);
            Assert(p.AttendanceDays == beforeVip, "VIP 档不推进签到天数（与客户端 index 判断一致）");
            Assert(p.AttendanceVipClaimedDay != 0, "领完 VIP 档后记录当天已领");
            Assert(vipClaim.attendanceInfo!.attendanceRwardStatus == AttendanceRewardStatus.ATTENDANCE_REWARD_VIP,
                "VIP 档领完后状态变为 VIP（不可再领）");
            await Request(GameServerCommand.GS_RECEIVE_ATTENDANCE_REWARD_REQUEST,
                new GsReceiveAttendanceRewardRequest { keyInfo = key, attendanceType = AttendanceType.ATENDANDE_MONTH }, success: false);
        }
        else
        {
            Assert(vipEntry.attendanceRwardStatus == AttendanceRewardStatus.ATTENDANCE_REWARD_VIP,
                "该档无 VIP 加成时直接进入 VIP 状态");
        }

        // ── 跨日重置 ──
        p.AttendanceLastDay = 0; p.ShortTermLastDay = 0;
        var nextDay = await Entry();
        Assert(nextDay.attendanceRwardStatus == AttendanceRewardStatus.ATTENDANCE_REWARD_NONE, "跨日后月度重新可领");
        Assert(nextDay.currentShortTermAttendanceRewardFalg, "跨日后短期签到重新可领");
        var nextShort = await Claim(AttendanceType.ATENDANDE_SHORT);
        Assert(p.ShortTermDays == 2 && nextShort.attendanceInfo!.totalShortTermAttendanceDay == 2, "第二天短期签到可继续累进");

        // ── 存档持久化（必须在下面人为构造状态之前验证，否则比的是内存值不是存档）──
        var restored = new PlayerStore(save, c).Find(p.Uid)!;
        Assert(restored.AttendanceDays == p.AttendanceDays && restored.AttendanceVipClaimedDay == p.AttendanceVipClaimedDay,
            "月度签到进度写入存档");
        Assert(restored.ShortTermDays == p.ShortTermDays && restored.ShortTermLastDay == p.ShortTermLastDay,
            "短期签到进度写入存档");

        // ── 短期签到领到上限后不再可领 ──
        p.ShortTermDays = (uint)rules.ShortTermDays.Count;
        p.ShortTermLastDay = 0;
        var capped = await Entry();
        Assert(!capped.currentShortTermAttendanceRewardFalg, "短期签到领满后不再显示可领");
        await Request(GameServerCommand.GS_RECEIVE_ATTENDANCE_REWARD_REQUEST,
            new GsReceiveAttendanceRewardRequest { keyInfo = key, attendanceType = AttendanceType.ATENDANDE_SHORT }, success: false);

        // ── 运营活动 ──
        var events = Read<GsGetOperationEventDataOk>(await Request(GameServerCommand.GS_GET_OPERATION_EVENT_DATA_REQUEST, new GsGetOperationEventDataRequest { keyInfo = key }));
        Assert(events.operationDataList.Count == rules.Operations.Count(AttendanceRules.EffectiveOperation),
            "活动数量与配置一致（实际 " + events.operationDataList.Count + " / 配置 " + rules.Operations.Count + "）");
        Assert(events.totalCount == events.operationDataList.Count, "totalCount 与实际下发条数一致");
        Assert(events.operationDataList.All(x => x.OperationType != BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_NONE),
            "下发的活动类型都能被客户端识别");
        Assert(events.operationDataList.Any(x => x.OperationType == BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_RESOURCE_STAGE_ALL_OPEN)==rules.Operations.Any(x=>AttendanceRules.EffectiveOperation(x)&&x.Type is "RESOURCE_STAGE_ALL_OPEN" or "EVENT_OPERATION_TYPE_RESOURCE_STAGE_ALL_OPEN"),
            "资源全开仅下发有效限时活动");
        Assert(events.operationDataList.Any(x => x.OperationType == BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_NORMAL_BATTLE_ITEM_DROP),
            "包含普通关卡掉落加成活动");
        // 客户端 GetOperationValue 对非白名单类型要求 Value > 1，否则当成未开启
        var guarded = events.operationDataList.Where(x => x.OperationType is not (
            BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_RESOURCE_STAGE_ALL_OPEN or
            BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_GACHA_GOODSECT or
            BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_GACHA_EVILSECT or
            BscTable.EVENT_OPERATION_TYPE.EVENT_OPERATION_TYPE_GACHA_NEUTRALSECT));
        Assert(guarded.All(x => x.Value > 1), "掉率类活动的 Value 必须 >1，否则客户端视为未开启");
        Assert(events.operationDataList.All(x => !string.IsNullOrWhiteSpace(x.StartDate) && !string.IsNullOrWhiteSpace(x.EndDate)),
            "活动都带起止时间（客户端 Relativity=false 时按绝对时间判断）");

        // ── 存档持久化 ──
        if (original != null)
            Assert(SHA256.HashData(File.ReadAllBytes(production)).SequenceEqual(original), "签到检查不会改动正式存档");

        File.WriteAllText(Path.Combine(root, "evidence/attendance-test.json"), JsonSerializer.Serialize(new
        {
            completedAt = DateTimeOffset.Now,
            passed = checks.Count,
            checks,
            saveDirectory = save,
            shortTermDays = rules.ShortTermDays.Count,
            operationCount = events.operationDataList.Count,
            monthlyRowOpenDate = row.OpenDate,
            monthlyRewardCount = row.LoginRewardList.Count
        }, new JsonSerializerOptions { WriteIndented = true }));
        System.Console.WriteLine($"PASS: {checks.Count} attendance, short-term sign-in and operation event checks.");
    }
}
