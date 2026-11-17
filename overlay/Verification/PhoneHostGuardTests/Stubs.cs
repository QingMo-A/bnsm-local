namespace Android.App {public static class Application{public static object Context=>new();}}
namespace Android.Util {public static class Log{public static void Error(string tag,string message){}}}
namespace Bnsm.AndroidHost {internal static class PhoneSupportInfo {internal static object Snapshot(object context)=>new{ready=true,instanceId="fixture",versionCode=6,runRequested=true};}}
