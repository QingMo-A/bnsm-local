using System.Text.Json;
internal static class InfiniteDefenceFixture
{
    internal static async Task Run(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith(@"I:\BnsmModernWork\Verification\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("isolated defence fixture required");
        Directory.CreateDirectory(folder);await using var runtime=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0){CompetitiveRegression.Prepare(runtime);var p=runtime.Store.All().Single();p.Decks[56]=Enumerable.Repeat(0UL,15).ToList();p.Decks[56][0]=1;runtime.Store.Save();}
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort}));
        var end=DateTime.UtcNow.AddMinutes(20);while(DateTime.UtcNow<end){await Task.Delay(100);lock(runtime.Store.Gate){
            var p=runtime.Store.All().Single();
            if(File.Exists(Path.Combine(folder,"request-training-lock"))&&!File.Exists(Path.Combine(folder,"training-lock-ready"))){p.TrainingSlots[0]=new(){CardSequence=1,EndTime=ProtocolRouter.Now+3600};runtime.Store.Save();File.WriteAllText(Path.Combine(folder,"training-lock-ready"),"ok");}
            if(File.Exists(Path.Combine(folder,"request-unlock"))&&!File.Exists(Path.Combine(folder,"unlock-ready"))){p.TrainingSlots.Clear();runtime.Store.Save();File.WriteAllText(Path.Combine(folder,"unlock-ready"),"ok");}
        }}
    }
}
