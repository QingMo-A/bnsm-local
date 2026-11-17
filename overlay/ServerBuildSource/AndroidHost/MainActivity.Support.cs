using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Widget;
using Bnsm.Server;

namespace Bnsm.AndroidHost;

public sealed partial class MainActivity
{
    private const int ExportDocument=3001,ImportSave=3002;
    private string? pendingExport;
    private bool supportBusy;
    private Action? refreshProfiles;
    private void AddSupportControls(LinearLayout layout)
    {
        var check=new Button(this){Text="检查本机连接 / 运行版本"};layout.AddView(check);check.Click+=async(_,_)=>await Work(async()=>{
            string text=await Task.Run(()=>System.Text.Json.JsonSerializer.Serialize(PhoneSupportInfo.Snapshot(this),new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            var info=new TextView(this){Text=text,TextSize=13};info.SetTextIsSelectable(true);var scroll=new ScrollView(this);scroll.AddView(info);
            var dialog=new AlertDialog.Builder(this);dialog.SetTitle("本机服务诊断");dialog.SetView(scroll);dialog.SetPositiveButton("关闭",(_,_)=>{});dialog.Show();
        });
        var logs=new Button(this){Text="查看 / 导出服务端日志"};layout.AddView(logs);logs.Click+=(_,_)=>ShowLogs();
        var saves=new Button(this){Text="存档管理 · 切换 / 备份 / 导入导出"};layout.AddView(saves);saves.Click+=(_,_)=>OpenSaveManager();
        var background=new Button(this){Text="后台运行状态和设置"};layout.AddView(background);background.Click+=(_,_)=>ShowBackgroundSettings();
    }
    private async Task Work(Func<Task> action)
    {
        if(supportBusy||serviceBusy)return;supportBusy=true;
        start.Enabled=false;stop.Enabled=false;
        try {await action();}catch(Exception error){Show("操作失败："+error.Message);ServerDiagnostics.Error(error.ToString());}
        finally {supportBusy=false;start.Enabled=!serviceBusy;stop.Enabled=!serviceBusy;}
    }
    private void ShowLogs()
    {
        var panel=new LinearLayout(this){Orientation=Orientation.Vertical};
        var text=new TextView(this){TextSize=12,Text=ServerDiagnostics.Current?.Tail()??"暂无日志"};text.SetTextIsSelectable(true);
        var scroll=new ScrollView(this);scroll.AddView(text);panel.AddView(scroll,new LinearLayout.LayoutParams(-1,700));
        var refresh=new Button(this){Text="刷新最近日志"};panel.AddView(refresh);refresh.Click+=(_,_)=>text.Text=ServerDiagnostics.Current?.Tail()??"暂无日志";
        var export=new Button(this){Text="导出日志 ZIP"};panel.AddView(export);
        export.Click+=async(_,_)=>await Work(async()=>{
            byte[] data=await Task.Run(()=>ServerDiagnostics.Current?.Export()??throw new InvalidOperationException("日志未启用"));
            string path=NewExportPath("logs");await File.WriteAllBytesAsync(path,data);ChooseExport(path,"bnsm-logs-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip");
        });
        var dialog=new AlertDialog.Builder(this);dialog.SetTitle("服务端日志 · UTC 时间");dialog.SetView(panel);dialog.SetMessage("保留最近约8 MiB日志；导出不包含存档。");dialog.SetPositiveButton("关闭",(_,_)=>{});dialog.Show();
    }
    private void ShowBackgroundSettings()
    {
        var power=(PowerManager)GetSystemService(PowerService)!;
        var panel=new LinearLayout(this){Orientation=Orientation.Vertical};
        panel.AddView(new TextView(this){TextSize=15,Text="常驻前台服务："+(ServerService.Startup?.IsCompletedSuccessfully==true?"运行中":"未启动")+"\n游戏进程绑定："+(GameBindingService.IsGameBound?"已绑定":"游戏进入前台后绑定")+"\n电池优化豁免："+(power.IsIgnoringBatteryOptimizations(PackageName)?"已允许":"未允许")+"\n\n游戏在前台时会绑定服务端，避免服务端进入缓存冻结状态。系统回收后会请求恢复；手动停止后保持停止。"});
        var app=new Button(this){Text="打开应用设置 · 后台电池 / 自启动"};panel.AddView(app);
        app.Click+=(_,_)=>OpenSetting(new Intent(Settings.ActionApplicationDetailsSettings,Android.Net.Uri.Parse("package:"+PackageName)));
        var battery=new Button(this){Text="打开系统电池优化名单"};panel.AddView(battery);battery.Click+=(_,_)=>OpenSetting(new Intent(Settings.ActionIgnoreBatteryOptimizationSettings));
        if(OperatingSystem.IsAndroidVersionAtLeast(33)) {
            var notifications=new Button(this){Text="允许常驻服务通知"};panel.AddView(notifications);
            notifications.Click+=(_,_)=>{if(OperatingSystem.IsAndroidVersionAtLeast(33))RequestPermissions([Android.Manifest.Permission.PostNotifications],3010);};
        }
        panel.AddView(new TextView(this){TextSize=13,Text="请勿在最近任务中上划清除服务端，这可能触发系统强制停止。可将服务端锁定后台，并允许后台运行与自启动。强制停止后需要重新打开服务端；返回游戏标题重新登录即可。无需悬浮窗权限。"});
        var dialog=new AlertDialog.Builder(this);dialog.SetTitle("后台运行");dialog.SetView(panel);dialog.SetPositiveButton("关闭",(_,_)=>{});dialog.Show();
    }
    private void OpenSetting(Intent intent)
    {
        try {StartActivity(intent);}catch(ActivityNotFoundException){Show("本机未提供此设置入口，请从系统应用设置进入。");}
    }
    private string NewExportPath(string kind)
    {
        string folder=Path.Combine(CacheDir!.AbsolutePath,"exports");Directory.CreateDirectory(folder);
        foreach(string old in Directory.GetFiles(folder,"*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(5))try{File.Delete(old);}catch(IOException){}
        return Path.Combine(folder,kind+"-"+Guid.NewGuid().ToString("N")+".zip");
    }
    private void ChooseExport(string source,string name)
    {
        pendingExport=source;
        var intent=new Intent(Intent.ActionCreateDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("application/zip");intent.PutExtra(Intent.ExtraTitle,name);
        StartActivityForResult(intent,ExportDocument);
    }
    protected override void OnSaveInstanceState(Bundle state){state.PutString("pendingExport",pendingExport);base.OnSaveInstanceState(state);}
    protected override async void OnActivityResult(int requestCode,Result resultCode,Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);
        if(resultCode!=Result.Ok||data?.Data==null)return;
        var uri=data.Data;
        if(requestCode==ExportDocument&&pendingExport!=null) {
            string source=pendingExport;pendingExport=null;
            await Work(async()=>{using var target=ContentResolver!.OpenOutputStream(uri,"wt")??throw new IOException("无法写入所选位置");using var input=File.OpenRead(source);await input.CopyToAsync(target);Show("已导出到你选择的位置。");});
        } else if(requestCode==ImportSave) {
            await Work(async()=>{
                await ServerService.StopRequested(this,"save-import");
                string temporary=NewExportPath("import");
                try {
                    using(var input=ContentResolver!.OpenInputStream(uri)??throw new IOException("无法读取所选文件"))
                    using(var output=File.Create(temporary)) {
                        byte[] buffer=new byte[65536];long total=0;int read;
                        while((read=await input.ReadAsync(buffer))>0){total+=read;if(total>64*1024*1024)throw new InvalidDataException("备份文件超过64 MiB");await output.WriteAsync(buffer.AsMemory(0,read));}
                    }
                    await Task.Run(()=>ServerService.SaveManager(this).Execute("import",name:"导入的进度 "+DateTime.Now.ToString("MM-dd HH:mm"),file:temporary));
                    Show("已导入为新存档，当前存档保持；可在存档管理中选择切换。");refreshProfiles?.Invoke();
                } finally {if(File.Exists(temporary))File.Delete(temporary);}
            });
        }
    }
    private void AskText(string title,string initial,Func<string,Task> action)
    {
        var input=new EditText(this){Text=initial};input.SetSingleLine(true);
        var dialog=new AlertDialog.Builder(this);dialog.SetTitle(title);dialog.SetView(input);dialog.SetNegativeButton("取消",(_,_)=>{});
        dialog.SetPositiveButton("确定",async(_,_)=>await Work(()=>action(input.Text??"")));dialog.Show();
    }
}
