using System.Net.Http.Json;
using System.Text.Json;
internal static class DebugRulesTest
{
    public static void Prepare(LocalRuntime r)
    {
        TrainingTest.Prepare(r);var c=r.Catalog;var p=r.Store.GetOrCreate("local-player");
        var group=c.UsableHeroes.GroupBy(x=>x.EvolveFirstAncestor).First(g=>g.Min(x=>x.Rank)<=4&&g.Max(x=>x.Rank)>7);
        var hero=group.OrderBy(x=>x.Rank).First();p.CardIds[0]=(uint)hero.Id;p.CardIds[1]=(uint)hero.Id;
        p.CardStates[1]=new();p.CardStates[2]=new();p.Level=99;p.Exp=ProgressionRules.PlayerFloor(c,99);
        p.Gold=0;p.CashPoint=0;p.Inventory.Clear();p.SkillPoint=0;p.SkillPointStamp=ProtocolRouter.Now;p.DebugFlags=0;r.Store.Save();
    }
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);if(!output.StartsWith(@"I:\BnsmMobileWork\verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(output);await using var r=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);if(r.Store.All().Count==0)Prepare(r);await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));await Task.Delay(TimeSpan.FromMinutes(12));
    }
    public static async Task Run(string root)
    {
        string save=Path.Combine(root,"verification/debug-rules",Guid.NewGuid().ToString("N"));await using var r=new LocalRuntime(root,save,0,0,0,false);Prepare(r);await using var app=await DebugToolsTest.Host(r);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        using var http=new HttpClient{BaseAddress=new Uri(addresses.Addresses.Single())};var p=r.Store.GetOrCreate("local-player");var c=r.Catalog;
        var checks=new List<string>();void Check(bool b,string text){if(!b)throw new Exception(text);checks.Add(text);}
        async Task<JsonElement> Post(string url,object body){using var response=await http.PostAsJsonAsync(url,body);response.EnsureSuccessStatusCode();return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();}
        async Task Toggle(int index,bool on){var result=await Post("/debug/options",new{uid=p.Uid,option=index,enabled=on});Check(result.GetProperty("ok").GetBoolean(),$"Rule {index} toggles {on}");}
        var connection=new ConnectionState{Player=p};r.Router.Handle(ServiceKind.Login,(ushort)LoginServerCommand.LS_LOGIN_REQUEST,WireCodec.Serialize(new LsLoginRequest{userID="local-player",userPW="local"}),connection);
        var login=(LsGameStartOk)r.Router.Handle(ServiceKind.Login,(ushort)LoginServerCommand.LS_GAME_START_REQUEST,WireCodec.Serialize(new LsGameStartRequest{serverId=1,bAgreement=true}),connection).Message!;
        var key=new SessionKeyInfo{uid=p.Uid,loginSessionKey=login.loginSessionKey};
        Reply Send(GameServerCommand cmd,object body,bool success=true,bool retry=false){if(!retry)key.sequenceNo++;var reply=r.Router.Handle(ServiceKind.Game,(ushort)cmd,WireCodec.Serialize(body),connection);if(reply.Name!=cmd.ToString().Replace("_REQUEST",success?"_OK":"_FAIL"))throw new Exception("Unexpected "+reply.Name+" "+(reply.Message as FailPacket)?.message);return reply;}
        string Wallet()=>JsonSerializer.Serialize(new{p.Gold,p.CashPoint,p.Inventory,p.SkillPoint});
        string State()=>JsonSerializer.Serialize(new{p.CardIds,p.CardStates,p.GrowthReceipts,p.DebugFlags,wallet=Wallet()});
        for(int pair=0;pair<5;pair++) {
            await Toggle(pair*2+1,true);Check((p.DebugFlags&(3u<<(pair*2)))==(3u<<(pair*2)),"Child enables parent "+pair);
            await Toggle(pair*2,false);Check((p.DebugFlags&(3u<<(pair*2)))==0,"Parent disables child "+pair);
            await Toggle(pair*2+1,true);await Toggle(pair*2+1,false);Check((p.DebugFlags&(3u<<(pair*2)))==(1u<<(pair*2)),"Disabling child preserves parent "+pair);await Toggle(pair*2,false);
        }
        var level=new GsCardLevelUpRequest{keyInfo=key,targetCardSeq=1,targetExp=1};var enchant=new GsCardEnchantRequest{keyInfo=key,targetCardSeq=1};var evolve=new GsCardEvolutionRequest{keyInfo=key,targetCardSeq=1};
        string before=State();Send(GameServerCommand.GS_LEVELUP_CARD_REQUEST,level,false);Check(State()==before,"Disabled level rule restores resource checks atomically");
        string wallet=Wallet();await Toggle(0,true);Send(GameServerCommand.GS_LEVELUP_CARD_REQUEST,level);Check(p.CardStates[1].Level==2&&Wallet()==wallet,"Free level raises one level without creating or consuming growth");
        before=State();Send(GameServerCommand.GS_LEVELUP_CARD_REQUEST,level,retry:true);Check(State()==before,"Free level retransmission cannot upgrade twice");
        await Toggle(1,true);Send(GameServerCommand.GS_LEVELUP_CARD_REQUEST,level);Check(p.CardStates[1].Level==99&&p.CardStates[1].Exp==ProgressionRules.CardCeiling(c,99)&&Wallet()==wallet,"One-click level reaches 99 even on low rank and stores matching cumulative EXP");
        await Toggle(0,false);Check(ProgressionRules.CardLevel(c,p.CardStates[1].Exp,99)==99,"Turning off level debug preserves earned level");
        before=State();Send(GameServerCommand.GS_ENCHANT_CARD_REQUEST,enchant,false);Check(State()==before,"Normal enchant consumes real resources after debug off");
        await Toggle(2,true);Send(GameServerCommand.GS_ENCHANT_CARD_REQUEST,enchant);Check(p.CardStates[1].Enchant==1&&Wallet()==wallet,"Free enchant advances exactly one step");await Toggle(3,true);Send(GameServerCommand.GS_ENCHANT_CARD_REQUEST,enchant);Check(p.CardStates[1].Enchant==c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_ENCHANT)&&Wallet()==wallet,"One-click enchant reaches original limit without material mutation");
        before=State();Send(GameServerCommand.GS_ENCHANT_CARD_REQUEST,enchant,false);Check(State()==before,"Max enchant rejects further upgrade");await Toggle(2,false);
        await Toggle(4,true);int rank=c.Cards.Single(x=>x.Id==p.CardIds[0]).Rank;Send(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve);Check(c.Cards.Single(x=>x.Id==p.CardIds[0]).Rank==rank+1&&Wallet()==wallet,"Free breakthrough follows one original link");
        await Toggle(5,true);Send(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve);Check(c.Cards.Single(x=>x.Id==p.CardIds[0]).Rank==7,"One-click breakthrough stops at seven stars: rank="+c.Cards.Single(x=>x.Id==p.CardIds[0]).Rank+", unit="+c.Numbers[BscTable.NUMBER_TYPE.CARD_RANK_UNIT_PER_STEP]+", MaxRank="+c.Cards.Single(x=>x.Id==p.CardIds[0]).MaxRank);
        before=State();Send(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve,false);Check(State()==before,"Breakthrough flag cannot enable the separate awakening stage");
        await Toggle(6,true);Send(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve);Check(c.Cards.Single(x=>x.Id==p.CardIds[0]).Rank==8&&Wallet()==wallet,"Free awakening follows rank-seven awakening link");
        await Toggle(7,true);Send(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve);var final=c.Cards.Single(x=>x.Id==p.CardIds[0]);Check(final.Rank==Math.Min(final.MaxRank,c.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_LIMITED_CARD_EVOLVE))&&Wallet()==wallet,"One-click awakening stops at the valid original chain limit");
        before=State();Send(GameServerCommand.GS_EVOLUTION_CARD_REQUEST,evolve,false);Check(State()==before,"Terminal awakening cannot manufacture invalid card IDs");await Toggle(4,false);await Toggle(6,false);
        var skill=new GsCardSkillLevelUpRequest{keyInfo=key,targetCardSeq=1};skill.levelUpInfo.Add(new(){skillType=CardSkillType.CARD_SKILL_ACTIVE_ONE,levelupValue=1});
        before=State();Send(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,false);Check(State()==before,"Normal skill upgrade requires gold and skill points");
        await Toggle(8,true);Send(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill);Check(p.CardStates[1].ActiveSkills[0]==2&&Wallet()==wallet,"Free skill advances one level at zero currency");await Toggle(9,true);Send(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill);Check(p.CardStates[1].ActiveSkills[0]==99&&Wallet()==wallet,"One-click skill caps selected skill at 99");
        before=State();Send(GameServerCommand.GS_CARD_SKILL_LEVELUP_REQUEST,skill,retry:true);Check(State()==before,"Skill retry cannot charge or exceed cap");await Toggle(8,false);
        await Post("/debug/level",new{level=300});Check(p.Level==99&&ProgressionRules.PlayerMax(c)==99,"Player debug maximum is 99, never 300");uint exp=p.Exp;await Post("/debug/level",new{level=109});Check(p.Level==99&&p.Exp==exp,"Increment above player 99 is a no-op");
        await Toggle(9,true);r.Store.Save();var restored=new PlayerStore(save,c).Find(p.Uid)!;Check(restored.DebugFlags==p.DebugFlags&&restored.CardStates[1].Level==99&&restored.CardStates[1].ActiveSkills[0]==99,"Persistent flags and free progress survive reload");
        p.Level=300;p.Exp=(uint)c.PlayerStates.First(x=>x.Level==300).Exp;r.Store.Save();restored=new PlayerStore(save,c).Find(p.Uid)!;Check(restored.Level==99&&restored.Exp==ProgressionRules.PlayerFloor(c,99),"Legacy level-300 save is backed up and repaired to 99");Check(Directory.GetFiles(save,"*.before-debug-exp-repair-*").Length==1,"Legacy cap migration preserves a complete original save backup");
        File.WriteAllText(Path.Combine(root,"evidence/debug-rules-protocol-test.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,productionSaveUsed=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("DEBUG_RULES_PASS "+checks.Count);
    }
}
