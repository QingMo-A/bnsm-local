using Android.App;
using Android.Content;
using Android.OS;
using Bnsm.Server;

namespace Bnsm.AndroidHost;

internal static class PhoneSupportInfo
{
    private static readonly DateTime StartedUtc=DateTime.UtcNow;
    internal static object Snapshot(Context context)
    {
        var app=context.PackageManager!.GetPackageInfo(context.PackageName!,0)!;
        var power=(PowerManager)context.GetSystemService(Context.PowerService)!;
        var prefs=context.GetSharedPreferences("service-state",FileCreationMode.Private)!;
        var activity=(ActivityManager)context.GetSystemService(Context.ActivityService)!;
        using var process=new ActivityManager.RunningAppProcessInfo();ActivityManager.GetMyMemoryState(process);
        return new {
            package=context.PackageName,versionName=app.VersionName,versionCode=OperatingSystem.IsAndroidVersionAtLeast(28)?app.LongVersionCode:app.VersionCode,
            manufacturer=Build.Manufacturer,model=Build.Model,androidRelease=Build.VERSION.Release,androidApi=(int)Build.VERSION.SdkInt,
            startedUtc=StartedUtc,observedUtc=DateTime.UtcNow,instanceId=ServerService.InstanceId,
            ready=ServerService.Startup?.IsCompletedSuccessfully==true,runRequested=prefs.GetBoolean("runRequested",false),
            binding=GameBindingService.IsGameBound,batteryOptimizationIgnored=power.IsIgnoringBatteryOptimizations(context.PackageName),
            powerSaveMode=power.IsPowerSaveMode,deviceIdle=power.IsDeviceIdleMode,
            backgroundRestricted=OperatingSystem.IsAndroidVersionAtLeast(28)&&activity.IsBackgroundRestricted,
            processImportance=(int)process.Importance,lastHeartbeatUtc=ServerService.LastHeartbeatUtc,lastHeartbeatDelaySeconds=ServerService.LastHeartbeatDelaySeconds,
            notificationsEnabled=((NotificationManager)context.GetSystemService(Context.NotificationService)!).AreNotificationsEnabled(),
            lastStopReason=prefs.GetString("lastStopReason","尚未记录"),lastStopUtc=prefs.GetString("lastStopUtc",null),
            activeGameConnections=ServerService.ActiveConnections,lastGameRequestUtc=ServerService.LastRequestUtc,
            previousExits=Exits(context),saveFilesIncluded=false
        };
    }
    internal static object[] Exits(Context context)
    {
        if(!OperatingSystem.IsAndroidVersionAtLeast(30))return [];
        try {
            var manager=(ActivityManager)context.GetSystemService(Context.ActivityService)!;
            var exits=new List<object>();
            foreach(var exit in manager.GetHistoricalProcessExitReasons(context.PackageName,0,5))exits.Add(new {
                reason=(int)exit.Reason,reasonName=(int)exit.Reason switch {10=>"user-or-recents-force-stop",3=>"low-memory",4=>"crash",5=>"native-crash",6=>"anr",_=>"system-reason-"+exit.Reason},
                timeUtc=DateTimeOffset.FromUnixTimeMilliseconds(exit.Timestamp),description=exit.Description,pssKiB=exit.Pss,rssKiB=exit.Rss
            });
            return exits.ToArray();
        } catch(Exception error){return [new {unavailable=error.GetType().Name}];}
    }
    internal static void Stopping(Context context,string reason)
    {
        context.GetSharedPreferences("service-state",FileCreationMode.Private)!.Edit()!.PutString("lastStopReason",reason)!.PutString("lastStopUtc",DateTime.UtcNow.ToString("O"))!.Apply();
        ServerDiagnostics.Info("Service stop requested: reason="+reason+"; instance="+ServerService.InstanceId);
    }
}
