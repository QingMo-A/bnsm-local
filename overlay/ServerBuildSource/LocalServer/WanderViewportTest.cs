using System.Text.Json;

internal static class WanderViewportTest
{
    public static async Task Fixture(string root,string output)
    {
        output=Path.GetFullPath(output);
        if(!output.StartsWith(Path.GetFullPath(Path.Combine(root,"verification"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated fixture required");
        Directory.CreateDirectory(output);
        await using var runtime=new LocalRuntime(root,Path.Combine(output,"saves"),0,0,0,false);
        if(runtime.Store.All().Count==0) {
            RaidTest.Prepare(runtime);var p=runtime.Store.GetOrCreate("local-player");
            p.Wander.Difficulty=1;p.Wander.Spot=2;p.Wander.Points=5;p.Wander.BestPoints=5;p.Wander.BestSpots=[2,0,0];
            p.Wander.Resets=(uint)runtime.Catalog.VipFor(p.VipExp).WanderBattleEnterCount;
            p.Wander.PaidLeft=0;p.Wander.Cards[1]=new(){Sequence=1,Hp=.75f,Sp=.3f};
            runtime.Store.Save();
        }
        await using var app=await DebugToolsTest.Host(runtime);
        var addresses=app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        File.WriteAllText(Path.Combine(output,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=runtime.DirectoryPort,debugPort=new Uri(addresses.Addresses.Single()).Port,productionSaveUsed=false}));
        await Task.Delay(TimeSpan.FromMinutes(15));
    }
}
