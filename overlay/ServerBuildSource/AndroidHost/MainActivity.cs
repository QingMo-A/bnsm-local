using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Text.Json;

namespace Bnsm.AndroidHost;

[Activity(Label = "剑灵手机本地服（测试）", MainLauncher = true, Exported = true, Theme = "@style/BnsmHostTheme", ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait)]
[Android.Runtime.Register("local/bnsm/serverhost/MainActivity")]
public sealed partial class MainActivity : Activity
{
    private TextView output = null!;
    private Button start = null!, stop = null!;
    private bool serviceBusy;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetOnApplyWindowInsetsListener(new SafeInsets());
        var title = new TextView(this) { Text = "战斗吧剑灵 · 手机本地服\n服务端测试版，非游戏客户端", TextSize = 23 };
        layout.AddView(title);
        output = new TextView(this) { TextSize = 15 };output.SetMaxLines(10);layout.AddView(output);
        AddSupportControls(layout);
        start = new Button(this) { Text = "启动手机内服务端" }; stop = new Button(this) { Text = "保存并停止服务端" };
        layout.AddView(start); layout.AddView(stop);
        var botToggle=new Button(this){Text="展开人机管理 ▾"};layout.AddView(botToggle);
        var botControls=new LinearLayout(this){Orientation=Orientation.Vertical,Visibility=ViewStates.Gone};layout.AddView(botControls);AddBotControls(botControls);
        botToggle.Click+=(_,_)=>{botControls.Visibility=botControls.Visibility==ViewStates.Gone?ViewStates.Visible:ViewStates.Gone;};
        var scroll=new ScrollView(this);scroll.AddView(layout);SetContentView(scroll);
        layout.RequestApplyInsets();
        start.Click += async (_, _) => await StartServer();
        stop.Click += (_, _) => {
            var confirm=new AlertDialog.Builder(this);confirm.SetTitle("保存并停止服务端？");confirm.SetMessage("正在运行的游戏会断开。继续玩时请重新启动服务端，再返回游戏标题登录。");
            confirm.SetNegativeButton("继续运行",(_,_)=>{});confirm.SetPositiveButton("保存并停止",async(_,_)=>await Work(async()=>{await ServerService.StopRequested(this,"host-stop-button");Show("服务端已保存并停止。");}));confirm.Show();
        };
        pendingExport=savedInstanceState?.GetString("pendingExport");
        _ = StartServer();
    }
    private void Show(string text)
    {
        Bnsm.Server.ServerDiagnostics.Info(text);
        RunOnUiThread(()=>{output.Text=string.Join('\n',((output.Text??"")+text+"\n").Split('\n').TakeLast(16));});
    }
    private async Task StartServer()
    {
        if(serviceBusy||supportBusy)return;
        serviceBusy=true;start.Enabled = false;stop.Enabled=false;
        string? token = Intent?.GetStringExtra("probeRun");
        try
        {
            string? keep=Intent?.GetStringExtra("keepProfile");
            if(keep!=null)ServerService.KeepPlayedProfile(this,keep);
            var service = new Intent(this, typeof(ServerService));
            if (token != null) service.PutExtra("probeRun", token);
            StartForegroundService(service);
            for (int i = 0; ServerService.Startup == null && i < 100; i++) await Task.Delay(50);
            var server = await (ServerService.Startup ?? throw new TimeoutException("服务端未启动"));
            Show($"已在手机启动：127.0.0.1\n目录 {server.DirectoryPort} / 登录 {server.LoginPort} / 游戏 {server.GamePort}\n存档独立保存在本应用内部；外服入口暂不启用。");
            Show("当前存档："+ServerService.ActiveProfileName+"。可通过存档管理切换、备份或导出；原手机档保留。");
            await RefreshBots();
            if (token != null && ServerService.ProbeRun == token)
            {
                var report = await Task.Run(() => PhoneProtocolProbe.Run(ServerService.ContentRoot!, server, token, Show));
                PublishProbeReport(token, report);
                Show("本机协议和存档验证通过，可以打开游戏客户端继续验证。");
            }
        }
        catch (Exception ex)
        {
            Show("失败：" + ex); Android.Util.Log.Error("BnsmLocalHost", ex.ToString());
            PublishProbeReport(token, new { passed = false, error = ex.ToString() });
        }
        finally { serviceBusy=false;start.Enabled = !supportBusy;stop.Enabled=!supportBusy; }
    }
    private void PublishProbeReport(string? token, object report)
    {
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath,"phone-result.json"),json);
        // Only explicit isolated runs export diagnostics for adb inspection.
        if (token == null || !System.Text.RegularExpressions.Regex.IsMatch(token,"^[a-f0-9]{32}$")) return;
        string folder = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath,"verification",token);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"server-result.json"),json);
    }
    private sealed class SafeInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                view.SetPadding(safe.Left + 24, safe.Top + 16, safe.Right + 24, safe.Bottom + 16);
            }
            else view.SetPadding(insets.SystemWindowInsetLeft + 24, insets.SystemWindowInsetTop + 16, insets.SystemWindowInsetRight + 24, insets.SystemWindowInsetBottom + 16);
            return insets;
        }
    }
}
