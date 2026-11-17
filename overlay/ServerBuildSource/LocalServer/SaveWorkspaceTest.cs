using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SaveWorkspaceTest
{
    public static void Run(string root)
    {
        string production=Path.Combine(root,"LocalServer/saves/players.json");
        string digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(production)));
        string area=Path.Combine(root,"verification/save-workspace",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(area);
        var checks=new List<string>();
        void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
        void Reject(Action action,string label) {bool failed=false;try {action();}catch(Exception ex) when(ex is IOException or InvalidDataException or JsonException or ArgumentException) {failed=true;}Check(failed,label);}
        string Active(string home)=>JsonNode.Parse(File.ReadAllText(Path.Combine(home,"workspace.json")))!["ActiveProfileId"]!.GetValue<string>();
        int Count(string home)=>Directory.GetDirectories(Path.Combine(home,"profiles")).Count(x=>Path.GetFileName(x).Length==32);
        string old=Path.Combine(area,"legacy");Directory.CreateDirectory(old);File.Copy(production,Path.Combine(old,"players.json"));
        File.WriteAllText(Path.Combine(old,"world-chat.json"),"{\"Messages\":[]}");
        string home=Path.Combine(area,"user-data");var w=new SaveWorkspace(home);
        w.Command("list",legacy:old);string original=Active(home);
        Check(File.ReadAllBytes(w.Saves(original)+"/players.json").SequenceEqual(File.ReadAllBytes(production)),"First start imports the real legacy save without rewriting its bytes");
        Check(File.Exists(w.Saves(original)+"/world-chat.json"),"World chat history moves with the same profile");
        Check(File.ReadAllBytes(old+"/players.json").SequenceEqual(File.ReadAllBytes(production)),"Legacy installation retains its original save");
        w.Command("list",legacy:old);Check(Count(home)==1&&Active(home)==original,"Repeated launches never import a duplicate legacy profile");
        w.Command("new",name:"第二段冒险 中文 空格");string fresh=Active(home);
        Check(fresh!=original&&Count(home)==2,"New game creates and selects a separate profile");
        Check(File.ReadAllText(w.Saves(fresh)+"/players.json")=="[]","A new profile starts with no inherited player, currency, deck or items");
        Check(File.ReadAllBytes(w.Saves(original)+"/players.json").SequenceEqual(File.ReadAllBytes(production)),"Starting a new game preserves the old progress");
        w.Command("switch",id:original);Check(Active(home)==original,"Switching profiles restores the previous active slot");
        w.Command("rename",id:fresh,name:"重命名 \"测试\"");Check(File.ReadAllText(Path.Combine(home,"profiles",fresh,"profile.json")).Contains("Id"),"Rename supports names without turning them into file paths");
        Reject(()=>w.Command("switch",id:"../../outside"),"Profile path traversal is rejected");
        Reject(()=>w.Command("new",name:" "),"Empty profile names are rejected");
        string zip=Path.Combine(area,"带空格 的备份.bnsm-save.zip");w.Command("export",id:original,file:zip);
        using(var archive=ZipFile.OpenRead(zip))Check(archive.Entries.Count<=4&&archive.Entries.Any(x=>x.FullName=="archive.json")&&archive.Entries.All(x=>!x.FullName.Contains("in-use")),"Export contains version, checksums and save data without process locks");
        Reject(()=>w.Command("export",id:original,file:zip),"Export never overwrites an existing backup silently");
        w.Command("import",file:zip,name:"导入进度");Check(Count(home)==3&&Active(home)==original,"Import creates a new profile without replacing the active save");
        string imported=Directory.GetDirectories(Path.Combine(home,"profiles")).Select(Path.GetFileName).Single(x=>x!=original&&x!=fresh)!;
        Check(File.ReadAllBytes(w.Saves(imported)+"/players.json").SequenceEqual(File.ReadAllBytes(production)),"Archive import preserves exact player progress");
        string snapshot=File.ReadAllText(w.Saves(original)+"/players.json");
        var altered=JsonNode.Parse(snapshot)!.AsArray();altered[0]!["Gold"]=345678;File.WriteAllText(w.Saves(original)+"/players.json",altered.ToJsonString());
        w.Command("restore",id:original,file:zip);
        Check(File.ReadAllText(w.Saves(original)+"/players.json")==snapshot,"Restore recovers the selected profile's exact previous data");
        string beforeRestore=Directory.GetFiles(Path.Combine(home,"backups",original),"*before-restore*").Single();
        using(var archive=ZipFile.OpenRead(beforeRestore))using(var reader=new StreamReader(archive.GetEntry("players.json")!.Open()))Check(JsonNode.Parse(reader.ReadToEnd())![0]!["Gold"]!.GetValue<uint>()==345678,"Restore first backs up the progress it replaces");
        string corrupt=Path.Combine(area,"corrupt.zip");File.Copy(zip,corrupt);
        using(var archive=ZipFile.Open(corrupt,ZipArchiveMode.Update)) {archive.GetEntry("players.json")!.Delete();using var output=new StreamWriter(archive.CreateEntry("players.json").Open());output.Write("[]");}
        int profiles=Count(home);Reject(()=>w.Command("import",file:corrupt),"Damaged backup checksums are rejected");
        Reject(()=>w.Command("restore",id:original,file:corrupt),"A damaged restore never replaces valid progress");
        Check(Count(home)==profiles&&File.ReadAllText(w.Saves(original)+"/players.json")==snapshot,"Failed import and restore leave profiles and saves unchanged");
        File.WriteAllText(w.Saves(original)+"/players.json","damaged current save");
        w.Command("restore",id:original,file:zip);
        Check(File.ReadAllText(w.Saves(original)+"/players.json")==snapshot,"A valid backup can restore a currently corrupt profile without discarding its raw recovery copy");
        string slip=Path.Combine(area,"traversal.zip");using(var archive=ZipFile.Open(slip,ZipArchiveMode.Create))using(var output=new StreamWriter(archive.CreateEntry("../escaped.json").Open()))output.Write("[]");
        Reject(()=>w.Command("import",file:slip),"Archive path traversal is rejected before extraction");
        Check(!File.Exists(Path.Combine(home,"escaped.json")),"An import cannot write outside its staging directory");
        string invalid=Path.Combine(area,"bad.json");File.WriteAllText(invalid,"{\"unexpected\":true}");Reject(()=>w.Command("import",file:invalid),"Invalid legacy JSON is rejected instead of opening an empty game");
        w.Command("import",file:production,name:"旧版单文件");Check(Count(home)==4,"Unversioned players.json can be imported as a new profile");
        string bom=Path.Combine(area,"bom-players.json");File.WriteAllText(bom,File.ReadAllText(production),new System.Text.UTF8Encoding(true));
        w.Command("import",file:bom,name:"PowerShell旧档");Check(Count(home)==5,"Legacy Windows PowerShell UTF-8 BOM saves import correctly");
        var opened=w.Open();
        using(opened.Lease) {
            Check(opened.Id==original&&opened.Directory==w.Saves(original),"The server opens the currently selected profile");
            Reject(()=>w.Command("new",name:"不应创建"),"Creating a profile is blocked during a running game");
            Reject(()=>w.Command("switch",id:fresh),"Switching is blocked during a running game");
            Reject(()=>w.Command("restore",file:zip),"Restore is blocked during a running game");
            Reject(()=>w.Command("backup"),"A live save cannot be exported halfway through an update");
            Reject(()=>w.Open(),"Two servers cannot open the workspace simultaneously");
            Check(JsonSerializer.Serialize(w.Command("list")).Contains("\"busy\":true"),"The manager can display the running-game status safely");
        }
        w.Command("switch",id:fresh);Check(Active(home)==fresh,"Operations resume after the game releases its save lease");
        w.Command("switch",id:original);
        string formatPath=Path.Combine(w.Saves(original),"save-format.json"),formatText=File.ReadAllText(formatPath);
        var future=JsonNode.Parse(formatText)!;future["SchemaVersion"]=99;future["MinimumReaderVersion"]=99;File.WriteAllText(formatPath,future.ToJsonString());
        Reject(()=>w.Open(),"A future incompatible save cannot be opened by an older game");
        Reject(()=>w.Command("switch",id:original),"Switch validates save schema before selecting it");
        Check(File.ReadAllText(w.Saves(original)+"/players.json")==snapshot&&File.ReadAllText(formatPath).Contains("99"),"Newer save metadata and progress are never downgraded or overwritten");
        File.WriteAllText(formatPath,formatText);
        var legacyFormat=JsonNode.Parse(formatText)!;legacyFormat["LastWriterBuild"]="previous-release";File.WriteAllText(formatPath,legacyFormat.ToJsonString());
        var upgrade=w.Open();upgrade.Lease.Dispose();
        Check(JsonNode.Parse(File.ReadAllText(formatPath))!["LastWriterBuild"]!.GetValue<string>()==SaveFormat.Build,"Compatible upgrades record the current writer build");
        Check(Directory.GetFiles(Path.Combine(home,"backups",original),"*before-launch*").Length==2,"Every launch takes an independent pre-migration backup");
        string typed=Path.Combine(area,"typed");Directory.CreateDirectory(typed);
        var c=new GameCatalog(root);var p=new LocalPlayer {Uid=5001,Account="typed",SkillPoint=10,SkillPointStamp=ProtocolRouter.Now,CardIds=[20103],CardStates=new() {[1]=new() {Level=20,Enchant=2,WeaponSequence=1UL<<40,AccessorySequences=[0,0],GemIds=[120101004,0,0]}},EquipmentItems=[new() {ItemId=100073099,Sequence=1UL<<40}]};
        var node=JsonNode.Parse(JsonSerializer.Serialize(new[]{p}))!;node[0]!["FutureOptionalCounter"]=123;node[0]!["CardStates"]!["1"]!["FutureCardCounter"]=456;
        File.WriteAllText(Path.Combine(typed,"players.json"),node.ToJsonString());
        var store=new PlayerStore(typed,c);store.Save();var roundtrip=JsonNode.Parse(File.ReadAllText(Path.Combine(typed,"players.json")))!;
        Check(roundtrip[0]!["FutureOptionalCounter"]!.GetValue<int>()==123&&roundtrip[0]!["CardStates"]!["1"]!["FutureCardCounter"]!.GetValue<int>()==456,"Unknown optional player and card fields survive a compatible save round trip");
        Check(store.Find(5001)!.CardStates[1].WeaponSequence==1UL<<40&&store.Find(5001)!.CardStates[1].GemIds[0]==120101004,"Growth and 64-bit equipment ownership survive save loading");
        var relocated=new SaveWorkspace(home);Check(JsonSerializer.Serialize(relocated.Command("list")).Contains(original),"A different installation can reopen the same user data root");
        Reject(()=>w.Command("archive",id:original),"The active save cannot be archived by mistake");
        int beforeArchive=Count(home);w.Command("archive",id:imported);
        Check(Count(home)==beforeArchive-1&&Directory.GetDirectories(Path.Combine(home,"archived")).Length==1,"Archiving preserves the entire inactive profile outside the selection list");
        string archivedBackup=Directory.GetFiles(Path.Combine(home,"backups",imported),"*before-archive*").Single();
        w.Command("import",file:archivedBackup,name:"恢复归档");Check(Count(home)==beforeArchive,"Archived progress can be restored by importing its automatic backup");
        File.WriteAllText(Path.Combine(home,"workspace.json"),"broken");Reject(()=>w.Command("list"),"A corrupt workspace index never causes a new empty profile to replace it");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(production)))==digest,"All save-management tests leave the production save unchanged");
        File.WriteAllText(Path.Combine(root,"evidence/save-workspace-test.json"),JsonSerializer.Serialize(new {passed=true,checkCount=checks.Count,checks},new JsonSerializerOptions {WriteIndented=true}));
        System.Console.WriteLine($"Save workspace: {checks.Count}/{checks.Count} checks passed");
    }
}
