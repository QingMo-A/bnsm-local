using System.Text.RegularExpressions;

// These are offline intents only. Java never fabricates cards/equipment. The
// original builder applies each intent before the host admits a connection.
internal sealed class BotManagementRequest
{
    public string Id {get;set;}="";
    public string Action {get;set;}="";
    public int Count {get;set;}
    public uint BotUid {get;set;}
    public int ExpectedRevision {get;set;}
    public string Strength {get;set;}="";
    public string Style {get;set;}="";
    public string AiStrength {get;set;}="";
    public string CreatedUtc {get;set;}="";
}
internal sealed class BotManagementResult
{
    public string Id {get;set;}="";
    public string Action {get;set;}="";
    public bool Applied {get;set;}
    public string Message {get;set;}="";
    public string CompletedUtc {get;set;}="";
}
internal sealed partial class LocalBotWorld
{
    private bool ApplyManagementRequests(IReadOnlyList<LocalPlayer> humans)
    {
        if(world.ManagementRequests.Count==0)return false;
        if(world.ManagementRequests.Count>128)throw new InvalidDataException("人机待应用设置过多，请在存档管家中检查");
        var seen=new HashSet<string>(world.ManagementResults.Select(x=>x.Id));
        foreach(var request in world.ManagementRequests)
        {
            int count=world.Bots.Count,desired=world.Settings.DesiredPopulation;
            uint next=world.NextUid;
            int changedIndex=-1;LocalBot? previous=null;
            var result=new BotManagementResult{Id=request.Id,Action=request.Action,CompletedUtc=DateTime.UtcNow.ToString("O")};
            try
            {
                if(!Regex.IsMatch(request.Id,"^[a-f0-9]{32}$")||!seen.Add(request.Id))
                    throw new ArgumentException("重复或无效的人机设置编号，未重复应用");
                if(request.Action=="generate")
                {
                    if(request.Count is <1 or >MaximumPopulation||world.Bots.Count+request.Count>MaximumPopulation)
                        throw new ArgumentException("人机总数最多100名，未新增");
                    if(request.Strength!="mixed"&&!Strengths.Contains(request.Strength))throw new ArgumentException("新增人机强度无效");
                    AddCore(request.Count,humans,request.Strength);
                    world.Settings.DesiredPopulation=world.Bots.Count;
                    result.Message="已新增"+request.Count+"名人机";
                }
                else if(request.Action=="configure")
                {
                    if(!Strengths.Contains(request.Strength)||!Strengths.Contains(request.AiStrength)||!Styles.Contains(request.Style))
                        throw new ArgumentException("人机强度或配装选项无效");
                    changedIndex=world.Bots.FindIndex(x=>x.Uid==request.BotUid);
                    if(changedIndex<0)throw new ArgumentException("人机已不存在，请刷新后重新设置");
                    previous=world.Bots[changedIndex];
                    if(previous.Revision!=request.ExpectedRevision)throw new ArgumentException("人机配置已变化，未覆盖新配置，请刷新后重试");
                    var replacement=builder.Build(previous.Uid,previous.Profile.Nickname,request.Strength,request.Style,
                        previous.Seed,previous.CreatedAt,request.AiStrength);
                    replacement.Topics=previous.Topics;
                    replacement.Revision=previous.Revision+1;
                    world.Bots[changedIndex]=replacement;
                    result.Message="已更新人机养成和配装";
                }
                else throw new ArgumentException("不支持的人机设置操作");
                result.Applied=true;
            }
            catch(Exception error)
            {
                if(world.Bots.Count>count)world.Bots.RemoveRange(count,world.Bots.Count-count);
                world.NextUid=next;world.Settings.DesiredPopulation=desired;
                if(changedIndex>=0&&previous!=null)world.Bots[changedIndex]=previous;
                result.Message=error.Message;
            }
            world.ManagementResults.Add(result);
        }
        // World changes, receipts and removal of the queue share the original
        // single atomic Save. A crash cannot commit a generated roster and then
        // leave the same generation queued for the next startup.
        world.ManagementRequests.Clear();
        world.ManagementResults=world.ManagementResults.TakeLast(20).ToList();
        return true;
    }
}
