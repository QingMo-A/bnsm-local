using Android.App;
using Android.Content;
using Android.Runtime;
using Bnsm.Server;

namespace Bnsm.AndroidHost;

[Application]
public sealed class HostApplication(IntPtr handle,JniHandleOwnership ownership):Application(handle,ownership)
{
    public override void OnCreate()
    {
        base.OnCreate();
        ServerDiagnostics.Begin(Path.Combine(FilesDir!.AbsolutePath,"logs"));
        ServerDiagnostics.Current!.ContextProvider=()=>PhoneSupportInfo.Snapshot(this);
        ServerDiagnostics.Info("Host environment: "+System.Text.Json.JsonSerializer.Serialize(PhoneSupportInfo.Snapshot(this)));
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>ServerDiagnostics.Error("Unhandled managed exception: "+args.ExceptionObject);
        AndroidEnvironment.UnhandledExceptionRaiser+=(_,args)=>ServerDiagnostics.Error("Unhandled Android callback: "+args.Exception);
        _=Task.Run(()=>{
            if(!OperatingSystem.IsAndroidVersionAtLeast(30))return;
            try {
                var manager=(ActivityManager)GetSystemService(ActivityService)!;
                foreach(var exit in manager.GetHistoricalProcessExitReasons(PackageName,0,3))
                    ServerDiagnostics.Info($"Previous process exit: reason={exit.Reason}; time={DateTimeOffset.FromUnixTimeMilliseconds(exit.Timestamp):O}; description={exit.Description}");
            } catch(Exception error){ServerDiagnostics.Error("Read previous exit reason: "+error.Message);}
        });
    }
}
