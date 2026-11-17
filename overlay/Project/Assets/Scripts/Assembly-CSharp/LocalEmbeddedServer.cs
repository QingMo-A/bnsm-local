using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Android-only host adapter. The original client and isolated server keep separate static state.</summary>
public sealed class LocalEmbeddedServer : MonoBehaviour
{
    public static bool Enabled { get { return LocalMode.MobileLocal; } }
    public static bool Ready { get; private set; }
    public static string Status = "准备内置服务端";
#if UNITY_ANDROID && !UNITY_EDITOR
    private static LocalEmbeddedServer instance;
    private string workspace, cache, evidence;
    private byte[] archive;
    private Task operation;
    private string completed;
    private bool exporting;
    private bool selected, preparing;
    private Task pauseCheckpoint;
    private static TaskRemovalCallback taskRemovalCallback;
    [UnityEngine.Scripting.Preserve]
    private sealed class TaskRemovalCallback : AndroidJavaProxy
    {
        public TaskRemovalCallback() : base("local.bnsm.bridge.SaveBridge$GameCallbacks") { }
        [UnityEngine.Scripting.Preserve]
        public void requestExit()
        {
            // Java invokes this even if Unity has stopped pumping paused frames.
            // No scene API is accessed from this worker; complete saves first.
            _ = Task.Run(async () => {
                try
                {
                    await Bnsm.UnityHost.Host.Stop();
                    CompleteNativeTaskExit(null);
                }
                catch (Exception error)
                {
                    LocalAndroidDiagnostics.Trace("task-exit-failed " + error.Message);
                    CompleteNativeTaskExit(error.Message);
                }
            });
        }
        private static void CompleteNativeTaskExit(string error)
        {
            // Task continuations use managed pool threads, not Java's callback
            // thread. Attach explicitly before touching JNI on that thread.
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (var bridge = new AndroidJavaClass("local.bnsm.bridge.SaveBridge"))
                    if (error == null) bridge.CallStatic("finishTaskExit");
                    else bridge.CallStatic("taskExitFailed", error);
            }
            finally { AndroidJNI.DetachCurrentThread(); }
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (!Enabled || instance != null) return;
        var owner = new GameObject("BnsmEmbeddedHost"); DontDestroyOnLoad(owner);
        instance = owner.AddComponent<LocalEmbeddedServer>();
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var files = activity.Call<AndroidJavaObject>("getFilesDir"))
        using (var cacheDir = activity.Call<AndroidJavaObject>("getCacheDir"))
        {
            instance.workspace = Path.Combine(files.Call<string>("getAbsolutePath"), "EmbeddedServer");
            instance.cache = cacheDir.Call<string>("getAbsolutePath");
            taskRemovalCallback = new TaskRemovalCallback();
            using (var bridge = new AndroidJavaClass("local.bnsm.bridge.SaveBridge")) bridge.CallStatic("registerGameCallbacks", activity, taskRemovalCallback);
        }
        instance.evidence = Path.Combine(Application.persistentDataPath, "Diagnostics/embedded-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(instance.evidence));
    }
    public static IEnumerator Prepare()
    {
        if (instance == null) Initialize();
        if (!instance.preparing) instance.StartCoroutine(instance.PrepareCore());
        while (!Ready) yield return null;
    }
    private IEnumerator PrepareCore()
    {
        preparing = true;
        bool leased = false;
        while (!leased)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("local.bnsm.bridge.SaveBridge"))
                leased = bridge.CallStatic<bool>("acquireGame", activity);
            if (!leased) { Status = "存档管家正在处理文件，完成后将继续启动"; yield return new WaitForSecondsRealtime(.3f); }
        }
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var bridge = new AndroidJavaClass("local.bnsm.bridge.EmbeddedService"))
            bridge.CallStatic("start", activity);
        using (var request = UnityWebRequest.Get(Application.streamingAssetsPath + "/embedded-server.zip"))
        {
            yield return request.SendWebRequest();
            if (!string.IsNullOrEmpty(request.error)) { Status = "内置服务数据读取失败：" + request.error; yield break; }
            archive = request.downloadHandler.data;
        }
        selected = Bnsm.UnityHost.Host.HasWorkspace(Path.Combine(workspace, "Saves"));
        if (!selected)
        {
            Status = "首次使用内置服，请选择新建存档或导入旧服务端导出的存档。";
            using (var bridge = new AndroidJavaClass("local.bnsm.bridge.LocalSupport")) bridge.CallStatic("chooseProfile");
        }
        while (!selected) yield return null;
        if (operation == null) StartHost();
    }
    private void StartHost()
    {
        if (archive == null) { Status = "正在准备内置服数据，请稍候"; return; }
        if (!selected)
        {
            using (var bridge = new AndroidJavaClass("local.bnsm.bridge.LocalSupport")) bridge.CallStatic("chooseProfile");
            return;
        }
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var bridge = new AndroidJavaClass("local.bnsm.bridge.EmbeddedService")) bridge.CallStatic("start", activity);
        Begin(async () => { await Bnsm.UnityHost.Host.Start(archive, workspace, evidence); }, "内置服务端已就绪");
    }
    private void Begin(Func<Task> action, string message)
    {
        if (operation != null) return;
        completed = message;
        operation = Task.Run(action);
    }
    [UnityEngine.Scripting.Preserve]
    public void Command(string command)
    {
        if (operation != null) { Toast("正在处理，请稍候"); return; }
        if (command == "new") { selected = true; if (archive != null) StartHost(); }
        else if (command == "start") StartHost();
        else if (command == "checkpoint")
        {
            if (!Ready) { Toast("内置服尚未准备完成，请稍后保存"); return; }
            Begin(Bnsm.UnityHost.Host.Checkpoint, "存档已保存");
        }
        else if (command == "stop") Begin(Bnsm.UnityHost.Host.Stop, "已保存并停止服务端；重新启动后请返回标题登录");
        else if (command == "export")
        {
            string snapshot = Path.Combine(cache, "embedded-save-" + Guid.NewGuid().ToString("N") + ".bnsm-save.zip");
            exporting = true;
            Begin(async () => {
                try
                {
                    string omission = await Bnsm.UnityHost.Host.ExportForSupport(snapshot);
                    // Continue on the worker even if a native window pauses
                    // Unity frames. Never wait for Update to start packaging.
                    AndroidJNI.AttachCurrentThread();
                    try
                    {
                        using (var bridge = new AndroidJavaClass("local.bnsm.bridge.LocalSupport"))
                            bridge.CallStatic("bundleExport", string.IsNullOrEmpty(omission) ? snapshot : "", omission ?? "");
                    }
                    finally { AndroidJNI.DetachCurrentThread(); }
                }
                catch (Exception error)
                {
                    AndroidJNI.AttachCurrentThread();
                    try { Toast("内置服操作失败：" + error.Message); }
                    finally { AndroidJNI.DetachCurrentThread(); }
                    throw;
                }
            }, "正在后台校验存档并整理日志");
        }
        else if (command.StartsWith("import:", StringComparison.Ordinal))
        {
            string path = Path.GetFullPath(command.Substring(7));
            if (path != Path.Combine(cache, "embedded-import.zip")) { Toast("导入路径无效"); return; }
            Begin(async () => { await Bnsm.UnityHost.Host.Import(workspace, path); selected = true; await Bnsm.UnityHost.Host.Start(archive, workspace, evidence); }, "旧存档已作为独立存档导入；请返回标题重新登录");
        }
    }
    [UnityEngine.Scripting.Preserve]
    public void OpenDebug(string unused) { LocalDevPanel.Open(); }
    private static void Toast(string text)
    {
        using (var bridge = new AndroidJavaClass("local.bnsm.bridge.LocalSupport")) bridge.CallStatic("notice", text);
    }
    private void Update()
    {
        Ready = Bnsm.UnityHost.Host.Ready;
        if (operation != null && operation.IsCompleted)
        {
            bool ok = !operation.IsFaulted && !operation.IsCanceled;
            Status = ok ? completed : "内置服操作失败：" + operation.Exception.GetBaseException().Message;
            if (!ok) Debug.LogException(operation.Exception.GetBaseException());
            operation = null; if (!exporting) Toast(Status);
            if (!Ready)
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass("local.bnsm.bridge.EmbeddedService")) bridge.CallStatic("stop", activity);
            exporting = false;
        }
        else if (operation != null) Status = Bnsm.UnityHost.Host.Status;
    }
    private void OnApplicationPause(bool value)
    {
        LocalAndroidDiagnostics.Trace("embedded-paused=" + value);
        // Host.Checkpoint serializes with start/export/stop itself. Queue this
        // even if another console operation is running when Home is pressed.
        if (value && (pauseCheckpoint == null || pauseCheckpoint.IsCompleted))
            pauseCheckpoint = Task.Run(async () => {
                try
                {
                    await Bnsm.UnityHost.Host.Checkpoint();
                    if (Bnsm.UnityHost.Host.Ready) LocalAndroidDiagnostics.Trace("pause-checkpoint-complete");
                }
                catch (Exception error) { LocalAndroidDiagnostics.Trace("pause-checkpoint-failed " + error.Message); }
            });
    }
    private void OnApplicationQuit()
    {
        try
        {
            if (Task.Run(Bnsm.UnityHost.Host.Stop).Wait(2500))
                using (var bridge = new AndroidJavaClass("local.bnsm.bridge.SaveBridge")) bridge.CallStatic("releaseGame");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var service = new AndroidJavaClass("local.bnsm.bridge.EmbeddedService")) service.CallStatic("stop", activity);
        }
        catch (Exception error) { Debug.LogWarning(error.Message); }
    }
#elif UNITY_IOS && !UNITY_EDITOR
    public static IEnumerator Prepare() { return LocalIosHost.Prepare(); }
    internal static void PublishIosState(bool ready, string status) { Ready = ready; Status = status; }
#else
    public static IEnumerator Prepare() { yield break; }
#endif
}
