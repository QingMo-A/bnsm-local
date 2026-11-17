string? Argument(string name)
{
    int index=Array.IndexOf(args,name);return index<0?null:index+1<args.Length?args[index+1]:throw new ArgumentException("Missing "+name);
}
int Port(string name,int fallback)
{
    string? value=Argument(name);return value==null?fallback:int.TryParse(value,out int port)&&port is >=0 and <=65535?port:throw new ArgumentException("Invalid "+name);
}
string ContentRoot()
{
    if(Argument("--content-root") is string supplied)return Path.GetFullPath(supplied);
    for(var folder=new DirectoryInfo(AppContext.BaseDirectory);folder!=null;folder=folder.Parent)
        if(File.Exists(Path.Combine(folder.FullName,"LocalServer/data/packet-keys.json")))return folder.FullName;
    throw new DirectoryNotFoundException("找不到 LocalServer/data 原始游戏表");
}
string storageRoot=Argument("--save-root")??SaveWorkspace.DefaultRoot;
// Manager commands keep machine-readable stdout and never compete with the live host's log file.
if(args.Contains("--save-command")&&SaveWorkspace.TryCommand(args,Path.Combine(ContentRoot(),"LocalServer/saves")))return;
await using var diagnostics=Bnsm.Server.ServerDiagnostics.Begin(Argument("--log-directory")??Path.Combine(Argument("--save-directory")??storageRoot,"logs"));
try {
string root=ContentRoot();
var workspace=new SaveWorkspace(storageRoot);
var profile=Argument("--save-directory")==null?workspace.Open(Argument("--legacy-save-directory")??Path.Combine(root,"LocalServer/saves")):default;
using var lease=profile.Lease;
string saves=Argument("--save-directory")??profile.Directory;
await using var runtime=new LocalRuntime(root,saves,Port("--directory-port",10101),Port("--login-port",10102),Port("--game-port",10103));
var builder=WebApplication.CreateSlimBuilder(args);builder.WebHost.UseUrls("http://127.0.0.1:"+Port("--debug-port",58601));
var app=builder.Build();
app.MapGet("/health",()=>Results.Json(new{status="ready",directoryPort=runtime.DirectoryPort,loginPort=runtime.LoginPort,gamePort=runtime.GamePort,saveProfile=new{profile.Id,profile.Name,directory=Path.GetFullPath(saves)},data=runtime.Router.Status(),bots=runtime.Router.Bots.All().Count}));
DebugApi.Map(app,runtime);
await app.RunAsync();
} catch(Exception error) { Bnsm.Server.ServerDiagnostics.Error("Host startup/runtime failure: "+error);throw; }
