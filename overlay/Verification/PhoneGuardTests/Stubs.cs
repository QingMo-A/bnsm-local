public static class Console {public static void LogLive(string text){} public static void LogLiveA(params string[] text){}}
public static class LocalAndroidDiagnostics {public static void Trace(string text){} public static void Stage(string text){}}
public class MonoSingleton<T> where T:new(){public static T Instance=new T();}
public class Network {public void AddSendByted(int bytes){} public void AddRecvByted(int bytes){}}
