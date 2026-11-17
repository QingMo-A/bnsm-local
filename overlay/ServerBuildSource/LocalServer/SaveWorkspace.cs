using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed class SaveProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
internal sealed class SaveIndex
{
    public int FormatVersion { get; set; } = 1;
    public string ActiveProfileId { get; set; } = "";
}
internal sealed class SaveFormat
{
    public const int CurrentSchema = 1;
    public const string Build = "20261031-BotWorld";
    public string Product { get; set; } = "BnsmLocalGame";
    public int SchemaVersion { get; set; } = CurrentSchema;
    public int MinimumReaderVersion { get; set; } = CurrentSchema;
    public string LastWriterBuild { get; set; } = Build;
}

/// <summary>Version-independent profiles. Only this class writes the workspace index.</summary>
internal sealed class SaveWorkspace
{
    public string Root { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] DataFiles = ["players.json", "world-chat.json", "save-format.json", "bot-world.json"];
    public SaveWorkspace(string root) { Root = Path.GetFullPath(root); }
    public static string DefaultRoot => Environment.GetEnvironmentVariable("BNSM_SAVE_ROOT") is string custom && !string.IsNullOrWhiteSpace(custom)
        ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BnsmLocalGame");
    private string Profiles => Path.Combine(Root, "profiles");
    private string IndexPath => Path.Combine(Root, "workspace.json");
    private static string ValidId(string id) => Regex.IsMatch(id, "^[a-f0-9]{32}$") ? id : throw new InvalidDataException("存档编号无效。");
    private string ProfileDirectory(string id) => Path.Combine(Profiles, ValidId(id));
    public string Saves(string id) => Path.Combine(ProfileDirectory(id), "saves");
    private static string ValidName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw new InvalidDataException("存档名称需为1至40个字符。");
        return name;
    }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("文件内容无效：" + path);
    private static void AtomicJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private FileStream Lock()
    {
        Directory.CreateDirectory(Root);
        try { return new(Path.Combine(Root,".workspace.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException) { throw new IOException("另一个启动器或存档操作正在进行，请稍后再试。"); }
    }
    private SaveIndex Index()
    {
        var index=Read<SaveIndex>(IndexPath);
        if(index.FormatVersion!=1)throw new InvalidDataException("存档管理格式较新，请使用对应或更新版本的游戏。");
        _=Profile(index.ActiveProfileId);return index;
    }
    private SaveProfile Profile(string id)
    {
        var profile=Read<SaveProfile>(Path.Combine(ProfileDirectory(id),"profile.json"));
        if(profile.Id!=id)throw new InvalidDataException("存档索引与目录不一致。");
        return profile;
    }
    private List<SaveProfile> ProfilesList() => Directory.Exists(Profiles) ? Directory.GetDirectories(Profiles)
        .Where(x=>Regex.IsMatch(Path.GetFileName(x),"^[a-f0-9]{32}$"))
        .Select(x=>Profile(Path.GetFileName(x))).OrderBy(x=>x.CreatedUtc).ToList() : [];
    private bool Busy(string id)
    {
        string path=Path.Combine(Saves(id),".in-use");
        if(!File.Exists(path))return false;
        try { using var check=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);return false; }
        catch(IOException) { return true; }
    }
    private void RequireIdle()
    {
        if(ProfilesList().Any(x=>Busy(x.Id)))throw new IOException("游戏或本地服务端正在运行，请先正常退出，再操作存档。");
    }
    public static void ValidateData(string directory,byte[]? recoveredBotWorld=null)
    {
        string formatFile=Path.Combine(directory,"save-format.json");
        if(File.Exists(formatFile)) {
            var format=Read<SaveFormat>(formatFile);
            if(format.Product!="BnsmLocalGame" || format.SchemaVersion<1 || format.SchemaVersion>SaveFormat.CurrentSchema || format.MinimumReaderVersion>SaveFormat.CurrentSchema)
                throw new InvalidDataException("此存档由较新或不同的游戏格式创建，当前版本无法安全读取；请更新游戏。原存档未覆盖。");
        }
        string path=Path.Combine(directory,"players.json");
        if(!File.Exists(path))return; // Fresh profile, created by the first login.
        using var data=JsonDocument.Parse(File.ReadAllText(path));
        if(data.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("players.json不是有效的玩家列表。");
        var ids=new HashSet<uint>();var accounts=new HashSet<string>();
        foreach(var player in data.RootElement.EnumerateArray()) {
            if(!player.TryGetProperty("Uid",out var uid) || !uid.TryGetUInt32(out uint value) || value==0 || !ids.Add(value) ||
                !player.TryGetProperty("Account",out var account) || account.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(account.GetString()) || !accounts.Add(account.GetString()!) ||
                !player.TryGetProperty("CardIds",out var cards) || cards.ValueKind!=JsonValueKind.Array)
                throw new InvalidDataException("玩家存档结构无效，已拒绝覆盖或以新档替代。");
        }
        // Full type checks also catch invalid nested card/equipment state.
        _=Read<List<LocalPlayer>>(path);
        string chat=Path.Combine(directory,"world-chat.json");
        if(File.Exists(chat)) {using var document=JsonDocument.Parse(File.ReadAllText(chat));}
        string bots=Path.Combine(directory,"bot-world.json");
        if(recoveredBotWorld!=null||File.Exists(bots)) {
            using var document=JsonDocument.Parse(recoveredBotWorld??File.ReadAllBytes(bots));var world=document.RootElement;
            if(world.ValueKind!=JsonValueKind.Object || !world.TryGetProperty("Schema",out var schema) || !schema.TryGetInt32(out int version) || version!=1 ||
                !world.TryGetProperty("Bots",out var roster) || roster.ValueKind!=JsonValueKind.Array || roster.GetArrayLength()>LocalBotWorld.MaximumPopulation)
                throw new InvalidDataException("人机存档格式无效，原存档未覆盖。");
            var residents=JsonSerializer.Deserialize<List<LocalBot>>(roster.GetRawText())!;
            var botIds=new HashSet<uint>();
            if(residents.Any(bot=>bot==null || bot.Uid<900000 || !botIds.Add(bot.Uid) || ids.Contains(bot.Uid) || bot.Profile==null || bot.Profile.Uid!=bot.Uid ||
                !LocalBotWorld.Strengths.Contains(bot.Strength) || !LocalBotWorld.Strengths.Contains(bot.AiStrength) || !LocalBotWorld.Styles.Contains(bot.Style)))
                throw new InvalidDataException("人机身份或配置无效，原存档未覆盖。");
        }
    }
    private void Initialize(string? legacy)
    {
        if(File.Exists(IndexPath)) {_=Index();return;}
        if(ProfilesList().Count>0)throw new InvalidDataException("存档索引缺失，但已有存档目录。请保留目录，通过导入备份恢复，不会自动清空。");
        bool migrate=legacy!=null&&File.Exists(Path.Combine(legacy,"players.json"));
        var profile=CreateProfile(migrate?"原有进度":"初次冒险",migrate?legacy:null);
        AtomicJson(IndexPath,new SaveIndex {ActiveProfileId=profile.Id});
    }
    private SaveProfile CreateProfile(string name,string? source=null)
    {
        if(source!=null)ValidateData(source);
        var profile=new SaveProfile {Id=Guid.NewGuid().ToString("N"),Name=ValidName(name)};
        string directory=ProfileDirectory(profile.Id),temp=directory+".creating";
        Directory.CreateDirectory(Path.Combine(temp,"saves"));
        try {
            if(source!=null)foreach(string file in DataFiles)if(File.Exists(Path.Combine(source,file)))File.Copy(Path.Combine(source,file),Path.Combine(temp,"saves",file));
            if(!File.Exists(Path.Combine(temp,"saves/players.json")))File.WriteAllText(Path.Combine(temp,"saves/players.json"),"[]");
            ValidateData(Path.Combine(temp,"saves"));
            AtomicJson(Path.Combine(temp,"saves/save-format.json"),new SaveFormat());
            AtomicJson(Path.Combine(temp,"profile.json"),profile);
            Directory.Move(temp,directory);
        } catch {if(Directory.Exists(temp))Directory.Delete(temp,true);throw;}
        return profile;
    }
    private string Backup(string id,string reason,bool validate=true)
    {
        string directory=Path.Combine(Root,"backups",ValidId(id));Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+reason+"-"+Guid.NewGuid().ToString("N")[..8]+".bnsm-save.zip");
        Export(id,path,validate);return path;
    }
    private void Export(string id,string destination,bool validate=true)
    {
        var profile=Profile(id);string saves=Saves(id);if(validate)ValidateData(saves);
        ExportSnapshot(id,destination,DataFiles.Where(f=>File.Exists(Path.Combine(saves,f))).ToDictionary(f=>f,f=>File.ReadAllBytes(Path.Combine(saves,f))));
    }
    internal void ExportSnapshot(string id,string destination,IReadOnlyDictionary<string,byte[]> snapshot,CompressionLevel compression=CompressionLevel.Optimal)
    {
        var profile=Profile(id);
        if(!snapshot.ContainsKey("players.json") || snapshot.Keys.Any(f=>!DataFiles.Contains(f)))throw new InvalidDataException("Invalid captured save files");
        if(File.Exists(destination))throw new IOException("目标文件已存在，请选择新的备份文件名。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            var hashes=new Dictionary<string,string>();
            using(var archive=ZipFile.Open(temp,ZipArchiveMode.Create)) {
                foreach(string file in DataFiles.Where(snapshot.ContainsKey)) {
                    byte[] bytes=snapshot[file];hashes[file]=Convert.ToHexString(SHA256.HashData(bytes));
                    using var stream=archive.CreateEntry(file,compression).Open();stream.Write(bytes);
                }
                using var manifest=archive.CreateEntry("archive.json").Open();
                JsonSerializer.Serialize(manifest,new {Product="BnsmLocalGame",ArchiveVersion=1,Name=profile.Name,CreatedUtc=DateTime.UtcNow,Files=hashes},Json);
            }
            File.Move(temp,destination);
        } finally {if(File.Exists(temp))File.Delete(temp);}
    }
    private string ReadImport(string source)
    {
        string temp=Path.Combine(Root,"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try {
            if(Directory.Exists(source))foreach(string file in DataFiles) {string input=Path.Combine(source,file);if(File.Exists(input))File.Copy(input,Path.Combine(temp,file));}
            else if(Path.GetExtension(source).Equals(".json",StringComparison.OrdinalIgnoreCase))File.Copy(source,Path.Combine(temp,"players.json"));
            else {
                using var archive=ZipFile.OpenRead(source);
                if(archive.Entries.Count>DataFiles.Length+1 || archive.Entries.Sum(x=>x.Length)>64*1024*1024)throw new InvalidDataException("备份过大或含有不支持的文件。");
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in archive.Entries) {
                    if(!(DataFiles.Contains(entry.FullName)||entry.FullName=="archive.json") || !names.Add(entry.FullName))throw new InvalidDataException("备份包含重复、路径越界或未知文件。");
                    using var input=entry.Open();using var output=File.Create(Path.Combine(temp,entry.FullName));input.CopyTo(output);
                }
                using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(temp,"archive.json")));
                var manifest=document.RootElement;
                if(manifest.GetProperty("Product").GetString()!="BnsmLocalGame" || manifest.GetProperty("ArchiveVersion").GetInt32()!=1)throw new InvalidDataException("不是本地游戏存档备份。");
                foreach(string file in DataFiles) {
                    bool declared=manifest.GetProperty("Files").TryGetProperty(file,out var hash),exists=File.Exists(Path.Combine(temp,file));
                    if(declared!=exists || exists&&hash.GetString()!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(temp,file)))))throw new InvalidDataException("备份校验失败，文件不完整或已损坏。");
                }
            }
            if(!File.Exists(Path.Combine(temp,"players.json")))throw new InvalidDataException("备份里没有players.json。");
            ValidateData(temp);return temp;
        } catch {Directory.Delete(temp,true);throw;}
    }
    public object Command(string command,string? id=null,string? name=null,string? file=null,string? legacy=null)
    {
        using var gate=Lock();Initialize(legacy);var index=Index();id??=index.ActiveProfileId;
        if(command=="list")return new {root=Root,activeProfileId=index.ActiveProfileId,profiles=ProfilesList().Select(p=>new {p.Id,p.Name,p.CreatedUtc,active=p.Id==index.ActiveProfileId,busy=Busy(p.Id),saveDirectory=Saves(p.Id),backups=Directory.Exists(Path.Combine(Root,"backups",p.Id))?Directory.GetFiles(Path.Combine(Root,"backups",p.Id),"*.bnsm-save.zip").Length:0})};
        RequireIdle();_=Profile(id);
        switch(command) {
            case "new":
                var created=CreateProfile(name??"新冒险");index.ActiveProfileId=created.Id;AtomicJson(IndexPath,index);return new {profile=created,active=true};
            case "switch":ValidateData(Saves(id));index.ActiveProfileId=id;AtomicJson(IndexPath,index);return new {activeProfileId=id};
            case "rename":var renamed=Profile(id);renamed.Name=ValidName(name??"");AtomicJson(Path.Combine(ProfileDirectory(id),"profile.json"),renamed);return renamed;
            case "backup":return new {path=Backup(id,"manual")};
            case "archive":
                if(id==index.ActiveProfileId)throw new InvalidDataException("请先切换到其他存档，再归档当前存档。");
                string archivedBackup=Backup(id,"before-archive",false);
                string archived=Path.Combine(Root,"archived",id+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
                Directory.CreateDirectory(Path.GetDirectoryName(archived)!);Directory.Move(ProfileDirectory(id),archived);
                return new {profileId=id,archiveDirectory=archived,backup=archivedBackup};
            case "export":Export(id,Path.GetFullPath(file??throw new ArgumentException("需要导出文件路径。")));return new {path=Path.GetFullPath(file)};
            case "import":
                string imported=ReadImport(Path.GetFullPath(file??throw new ArgumentException("需要导入文件路径。")));
                try {var p=CreateProfile(name??"导入的进度",imported);return new {profile=p,active=false};} finally {Directory.Delete(imported,true);}
            case "restore":
                string restored=ReadImport(Path.GetFullPath(file??throw new ArgumentException("需要恢复文件路径。")));
                try {
                    string backup=Backup(id,"before-restore",false),saves=Saves(id),previous=saves+".before-restore-"+Guid.NewGuid().ToString("N");
                    Directory.Move(saves,previous);
                    try {Directory.Move(restored,saves);} catch {Directory.Move(previous,saves);throw;}
                    Directory.Delete(previous,true);return new {profileId=id,previousBackup=backup};
                } finally {if(Directory.Exists(restored))Directory.Delete(restored,true);}
            default:throw new ArgumentException("未知存档操作："+command);
        }
    }
    // Root lock prevents a manager operation racing a server startup. The open
    // profile lease then protects the entire running session, including new saves.
    public (string Id,string Name,string Directory,FileStream Lease) Open(string? legacy=null,bool recoverAndroidBotNumbers=false)
    {
        using var gate=Lock();Initialize(legacy);RequireIdle();var index=Index();var p=Profile(index.ActiveProfileId);
        string saves=Saves(p.Id);if(recoverAndroidBotNumbers)AndroidBotSaveRecovery.Recover(saves);ValidateData(saves);
        _=Backup(p.Id,"before-launch");
        AtomicJson(Path.Combine(saves,"save-format.json"),new SaveFormat());
        var lease=new FileStream(Path.Combine(saves,".in-use"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        return(p.Id,p.Name,saves,lease);
    }
    public static bool TryCommand(string[] args,string legacy)
    {
        string? Value(string flag) {int i=Array.IndexOf(args,flag);return i<0?null:i+1<args.Length?args[i+1]:throw new ArgumentException("缺少参数："+flag);}
        string? command=Value("--save-command");if(command==null)return false;
        try {
            var workspace=new SaveWorkspace(Value("--save-root")??DefaultRoot);
            object result=workspace.Command(command,Value("--profile-id"),Value("--profile-name"),Value("--save-file"),Value("--legacy-save-directory")??legacy);
            System.Console.WriteLine(JsonSerializer.Serialize(result));
        } catch(Exception ex) {System.Console.Error.WriteLine(ex.Message);Environment.ExitCode=2;}
        return true;
    }
}
