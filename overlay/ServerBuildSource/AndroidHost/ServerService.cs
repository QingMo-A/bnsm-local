using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Bnsm.Server;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Bnsm.AndroidHost;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[Android.Runtime.Register("local/bnsm/serverhost/ServerService")]
public sealed class ServerService : Service
{
    private const string Channel = "local-server";
    private static readonly SemaphoreSlim Gate = new(1);
    public static Task<IEmbeddedGameServer>? Startup { get; private set; }
    public static string? Failure { get; private set; }
    public static string? ProbeRun { get; private set; }
    public static string? ActiveProfileToken { get; private set; }
    private static IEmbeddedGameServer? server;
    private static PhoneDebugHttpHost? debug;
    private static ManagedSaveSession? saveSession;
    private readonly CancellationTokenSource lifetime=new();
    private readonly TaskCompletionSource destroyed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ServerService? owner;
    public static string? InstanceId {get;private set;}
    internal static int ActiveConnections=>(server as EmbeddedServer)?.ActiveGameConnections??0;
    internal static DateTime? LastRequestUtc=>(server as EmbeddedServer)?.LastGameRequestUtc;
    internal static DateTime? LastHeartbeatUtc {get;private set;}
    internal static double LastHeartbeatDelaySeconds {get;private set;}
    public static string? ActiveProfileName {get;private set;}
    public static string? ActiveProfileId {get;private set;}
    internal static ManagedSaveWorkspace SaveManager(Context context)=>new(Path.Combine(context.FilesDir!.AbsolutePath,"save-manager"),LegacySaveDirectory(context));
    private static string LegacySaveDirectory(Context context)
    {
        string? selected=context.GetSharedPreferences("save-profile",FileCreationMode.Private)!.GetString("activeVerificationProfile",null);
        if(selected!=null&&!ValidToken(selected))throw new InvalidDataException("存档选择无效");
        return selected==null?Path.Combine(context.FilesDir!.AbsolutePath,"profiles/default/saves"):Path.Combine(context.FilesDir!.AbsolutePath,"verification",selected,"saves");
    }
    public static string? ContentRoot { get; private set; }
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnCreate()
    {
        base.OnCreate();
        owner=this;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(Channel, "手机内的游戏服务端", NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(this, Channel)
            .SetContentTitle("剑灵本地服务端")
            .SetContentText("本机服务运行中，请勿从最近任务清除；点击管理")
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo).SetContentIntent(open).SetOngoing(true)
            .AddAction(new Notification.Action.Builder(null,"查看运行状态",open).Build()).Build();
        StartForeground(10101, notification);
        _=Monitor();
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        ServerDiagnostics.Info("Service start command: action="+(intent?.Action??"default")+"; flags="+flags+"; startId="+startId);
        var runState=GetSharedPreferences("service-state",FileCreationMode.Private)!;
        if(intent?.Action=="local.bnsm.serverhost.STOP") {runState.Edit()!.PutBoolean("runRequested",false)!.Commit();_=StopFromNotification();return StartCommandResult.NotSticky;}
        if(intent==null&&!runState.GetBoolean("runRequested",false)){ServerDiagnostics.Info("Sticky restart skipped: user previously stopped service");StopSelf();return StartCommandResult.NotSticky;}
        if (Startup == null || Startup.IsFaulted || Startup.IsCanceled)
        {
            string? token = intent?.GetStringExtra("probeRun");
            if(intent==null)token=runState.GetString("probeRun",null);
            ProbeRun = token != null && System.Text.RegularExpressions.Regex.IsMatch(token, "^[a-f0-9]{32}$") ? token : null;
            runState.Edit()!.PutBoolean("runRequested",true)!.PutString("probeRun",ProbeRun)!.Commit();
            ActiveProfileToken = ProbeRun ?? GetSharedPreferences("save-profile",FileCreationMode.Private)!.GetString("activeVerificationProfile",null);
            if(ActiveProfileToken!=null && !ValidToken(ActiveProfileToken))throw new InvalidDataException("存档选择无效");
            Failure = null;
            // Archive verification, extraction and catalog loading must stay off Android's UI thread.
            Startup = Task.Run(StartAsync);
        }
        return StartCommandResult.Sticky;
    }
    private async Task<IEmbeddedGameServer> StartAsync()
    {
        await Gate.WaitAsync();
        try
        {
            // Content updates select a new immutable directory. Saves never live in it.
            string staging = Path.Combine(FilesDir!.AbsolutePath, "server-content.zip.tmp");
            using (var source = Assets!.Open("server-content.zip"))
            using (var target = File.Create(staging)) await source.CopyToAsync(target);
            string digest;
            using (var input = File.OpenRead(staging)) digest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            string root = Path.Combine(FilesDir.AbsolutePath, "content", digest);
            if (!File.Exists(Path.Combine(root, ".complete")))
            {
                Directory.CreateDirectory(root);
                ZipFile.ExtractToDirectory(staging, root, true);
                File.WriteAllText(Path.Combine(root, ".complete"), digest);
            }
            File.Delete(staging); ContentRoot = root;
            string saves;
            if(ProbeRun!=null) {saves=Path.Combine(FilesDir.AbsolutePath,"verification",ProbeRun,"saves");ActiveProfileName="独立协议验证档";}
            else {saveSession=await Task.Run(()=>SaveManager(this).Open());saves=saveSession.Directory;ActiveProfileId=saveSession.Id;ActiveProfileName=saveSession.Name;}
            // Mobile release policy deliberately has no external bind/config switch.
            var embedded = await Task.Run(() => new EmbeddedServer(root, saves));
            server = embedded;
            InstanceId=Guid.NewGuid().ToString("N");
            debug = new PhoneDebugHttpHost(embedded);
            ServerDiagnostics.Info("Server ready; profile="+ActiveProfileName+"; game binding="+GameBindingService.IsGameBound);
            return server;
        }
        catch (Exception ex)
        {
            Failure = ex.ToString();
            ServerDiagnostics.Error("Server start failed: "+ex);
            if(server!=null) { await server.DisposeAsync(); server=null; }
            saveSession?.Dispose();saveSession=null;
            throw;
        }
        finally { Gate.Release(); }
    }
    public static async Task StopAsync()
    {
        // Wait for construction as well, so stopping cannot leave listening sockets behind.
        var starting = Startup;
        if (starting != null) { try { await starting; } catch { } }
        await Gate.WaitAsync();
        try
        {
            if (debug != null) await debug.DisposeAsync();
            debug = null;
            if (server != null) await server.DisposeAsync();
            server = null; Startup = null; Failure = null; ProbeRun = null; ActiveProfileToken = null;
            saveSession?.Dispose();saveSession=null;ActiveProfileId=null;ActiveProfileName=null;
            ServerDiagnostics.Info("Server saved and stopped");
        }
        finally { Gate.Release(); }
    }
    internal static async Task StopRequested(Context context,string reason="host-control")
    {
        PhoneSupportInfo.Stopping(context,reason);
        context.GetSharedPreferences("service-state",FileCreationMode.Private)!.Edit()!.PutBoolean("runRequested",false)!.Remove("probeRun")!.Commit();
        var stopping=owner;
        await StopAsync();context.StopService(new Intent(context,typeof(ServerService)));
        if(stopping!=null)try {await stopping.destroyed.Task.WaitAsync(TimeSpan.FromSeconds(3));}
        catch(TimeoutException){ServerDiagnostics.Error("Stop requested but Android onDestroy has not arrived after 3 seconds");}
    }
    private async Task StopFromNotification(){await StopRequested(this,"notification-action");StopSelf();}
    private async Task Monitor()
    {
        try {while(!lifetime.IsCancellationRequested){
            long expected=SystemClock.ElapsedRealtime()+60000;
            await Task.Delay(TimeSpan.FromSeconds(60),lifetime.Token);
            LastHeartbeatUtc=DateTime.UtcNow;LastHeartbeatDelaySeconds=Math.Max(0,(SystemClock.ElapsedRealtime()-expected)/1000.0);
            if(server is EmbeddedServer running)ServerDiagnostics.Info($"Foreground heartbeat: gameBound={GameBindingService.IsGameBound}; gameConnections={running.ActiveGameConnections}; managedMemoryMiB={GC.GetTotalMemory(false)/1048576}; schedulingDelaySeconds={LastHeartbeatDelaySeconds:F1}; lastGameRequestUtc={running.LastGameRequestUtc:O}");
        }}
        catch(System.OperationCanceledException){}
    }
    public override void OnTrimMemory(TrimMemory level){base.OnTrimMemory(level);ServerDiagnostics.Info("Android trim callback: "+level+"; bound="+GameBindingService.IsGameBound+"; Background alone does not mean low memory");}
    public override void OnTaskRemoved(Intent? rootIntent){ServerDiagnostics.Info("Service screen removed from recents; foreground service remains requested");base.OnTaskRemoved(rootIntent);}
    public override void OnDestroy() {
        bool requested=GetSharedPreferences("service-state",FileCreationMode.Private)!.GetBoolean("runRequested",false);
        ServerDiagnostics.Info("Service onDestroy; runRequested="+requested);
        if(requested)PhoneSupportInfo.Stopping(this,"system-service-onDestroy");
        lifetime.Cancel();if(server!=null||Startup!=null)_=StopAsync();if(ReferenceEquals(owner,this))owner=null;base.OnDestroy();destroyed.TrySetResult();
    }
    internal static void RecoverWhenBound(Context context)
    {
        if(Startup?.IsCompletedSuccessfully==true || Startup is {IsCompleted:false})return;
        if(!context.GetSharedPreferences("service-state",FileCreationMode.Private)!.GetBoolean("runRequested",false)) {
            ServerDiagnostics.Info("Visible-game binding: automatic start skipped; no running request or user stopped service");return;
        }
        try {context.StartForegroundService(new Intent(context,typeof(ServerService)).SetAction("local.bnsm.serverhost.RECOVER"));ServerDiagnostics.Info("Visible-game binding requested foreground service recovery");}
        catch(Exception error){ServerDiagnostics.Error("Foreground recovery not permitted; open host to resume: "+error.GetType().Name);}
    }
    private static bool ValidToken(string value) => System.Text.RegularExpressions.Regex.IsMatch(value,"^[a-f0-9]{32}$");
    internal static void KeepPlayedProfile(Context context,string token)
    {
        if(!ValidToken(token))throw new InvalidDataException("试玩存档标识无效");
        string directory=Path.Combine(context.FilesDir!.AbsolutePath,"verification",token,"saves");
        if(!File.Exists(Path.Combine(directory,"players.json")))throw new FileNotFoundException("试玩存档尚未保存");
        using var format=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"save-format.json")));
        if(format.RootElement.GetProperty("SchemaVersion").GetInt32()!=EmbeddedServer.SaveSchema)throw new InvalidDataException("试玩存档版本不匹配");
        // Select the existing directory. Neither the played save nor the old default is copied or overwritten.
        bool saved=context.GetSharedPreferences("save-profile",FileCreationMode.Private)!.Edit()!.PutString("activeVerificationProfile",token)!.Commit();
        if(!saved)throw new IOException("存档选择未能保存");
    }
}
