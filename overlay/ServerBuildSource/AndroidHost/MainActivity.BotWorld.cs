using Android.Widget;
using Bnsm.Server;
using System.Text.Json;

namespace Bnsm.AndroidHost;

public sealed partial class MainActivity
{
    private Spinner botList=null!,botGrowth=null!,botStyle=null!,botAi=null!,newBotTier=null!;
    private TextView botSummary=null!;
    private Button addBots=null!,applyBot=null!;
    private readonly List<(uint Uid,string Name,string Strength,string Style,string Ai)> botRows=[];
    private static readonly string[] TierKeys=["casual","regular","expert","legend"];
    private static readonly string[] TierLabels=["普通","精英","高手","宗师"];
    private static readonly string[] StyleKeys=["balanced","burst","control","sustain","counter"];
    private static readonly string[] StyleLabels=["均衡","爆发","控制","续航","反击"];
    private bool botBusy,settingBotSelection;
    private Spinner Choice(string[] labels)
    {
        var spinner=new Spinner(this);
        var adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerItem,labels);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);spinner.Adapter=adapter;return spinner;
    }
    private void AddBotControls(LinearLayout layout)
    {
        botSummary=new TextView(this){Text="人机居民：服务启动后读取",TextSize=16};layout.AddView(botSummary);
        void Row(string caption,Spinner spinner)
        {
            var row=new LinearLayout(this){Orientation=Orientation.Horizontal};
            row.AddView(new TextView(this){Text=caption,TextSize=15},new LinearLayout.LayoutParams(0,-2,.36f));
            row.AddView(spinner,new LinearLayout.LayoutParams(0,-2,.64f));layout.AddView(row);
        }
        newBotTier=Choice(["混合强度","普通","精英","高手","宗师"]);Row("新增人机强度",newBotTier);
        addBots=new Button(this){Text="按所选强度新增4名人机"};layout.AddView(addBots);
        botList=Choice(["正在读取…"]);Row("管理现有人机",botList);
        botGrowth=Choice(TierLabels);Row("养成强度",botGrowth);
        botStyle=Choice(StyleLabels);Row("阵容和配装",botStyle);
        botAi=Choice(TierLabels);Row("AI策略档位（预留）",botAi);
        layout.AddView(new TextView(this){Text="势力战已可匹配人机，沿用原版自动战斗；养成强度和配装会影响实战。自定义操作策略、主动挑战及其他PVP模式尚未接入。",TextSize=13});
        applyBot=new Button(this){Text="保存该人机配置"};layout.AddView(applyBot);
        addBots.Enabled=applyBot.Enabled=false;
        botList.ItemSelected+=(_,args)=>{if(!settingBotSelection)SelectBot(args.Position);};
        addBots.Click+=async(_,_)=>await ChangeBots("/debug/bots/generate",new{count=4,strength=newBotTier.SelectedItemPosition==0?"mixed":TierKeys[newBotTier.SelectedItemPosition-1]});
        applyBot.Click+=async(_,_)=>{
            int index=botList.SelectedItemPosition;if(index<0||index>=botRows.Count)return;
            await ChangeBots("/debug/bots/configure",new{botUid=botRows[index].Uid,strength=TierKeys[botGrowth.SelectedItemPosition],style=StyleKeys[botStyle.SelectedItemPosition],aiStrength=TierKeys[botAi.SelectedItemPosition]});
        };
    }
    private void SelectBot(int index)
    {
        if(index<0||index>=botRows.Count)return;
        var row=botRows[index];botGrowth.SetSelection(Math.Max(0,Array.IndexOf(TierKeys,row.Strength)));
        botStyle.SetSelection(Math.Max(0,Array.IndexOf(StyleKeys,row.Style)));botAi.SetSelection(Math.Max(0,Array.IndexOf(TierKeys,row.Ai)));
    }
    private async Task<JsonElement> BotRequest(string method,string path,object? body=null)
    {
        var server=await(ServerService.Startup??throw new InvalidOperationException("请先启动服务端"));
        return await Task.Run(()=>JsonSerializer.SerializeToElement(((EmbeddedServer)server).HandleDebug(method,path,null,body==null?null:JsonSerializer.Serialize(body))));
    }
    private void FillBots(JsonElement data)
    {
        if(!data.TryGetProperty("ok",out var success)||!success.GetBoolean())throw new InvalidDataException(data.TryGetProperty("message",out var m)?m.GetString():"人机请求失败");
        uint selected=botList.SelectedItemPosition>=0&&botList.SelectedItemPosition<botRows.Count?botRows[botList.SelectedItemPosition].Uid:0;
        botRows.Clear();
        foreach(var item in data.GetProperty("bots").EnumerateArray())
            botRows.Add((item.GetProperty("uid").GetUInt32(),item.GetProperty("name").GetString()!,item.GetProperty("strength").GetString()!,item.GetProperty("style").GetString()!,item.GetProperty("aiStrength").GetString()!));
        settingBotSelection=true;
        var adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerItem,botRows.Select(x=>x.Name+" · "+TierLabels[Array.IndexOf(TierKeys,x.Strength)]).ToArray());
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);botList.Adapter=adapter;
        int index=Math.Max(0,botRows.FindIndex(x=>x.Uid==selected));botList.SetSelection(index);SelectBot(index);settingBotSelection=false;
        botSummary.Text="人机居民："+botRows.Count+"名 · 独立卡组和装备，重启保留";
    }
    private async Task RefreshBots()
    {
        try {FillBots(await BotRequest("GET","/debug/bots"));addBots.Enabled=applyBot.Enabled=true;}
        catch(Exception error){Show("读取人机失败："+error.Message);}
    }
    private async Task ChangeBots(string path,object body)
    {
        if(botBusy)return;botBusy=true;addBots.Enabled=applyBot.Enabled=false;
        try {FillBots(await BotRequest("POST",path,body));Show("人机配置已保存，好友资料及下次势力战匹配使用新配置。");}
        catch(Exception error){Show("人机操作失败："+error.Message);}
        finally {botBusy=false;addBots.Enabled=applyBot.Enabled=true;}
    }
}
