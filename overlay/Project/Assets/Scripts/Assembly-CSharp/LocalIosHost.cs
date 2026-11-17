#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>iOS owns the same isolated C# host; native APIs only select/share files and finish saves.</summary>
public sealed class LocalIosHost : MonoBehaviour
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BackgroundSave(int token);
    [DllImport("__Internal")] private static extern void BnsmIosInstall(string receiver, string importTarget, BackgroundSave callback);
    [DllImport("__Internal")] private static extern void BnsmIosChooseProfile();
    [DllImport("__Internal")] private static extern void BnsmIosNotice(string text);
    [DllImport("__Internal")] private static extern void BnsmIosState(string text, [MarshalAs(UnmanagedType.I1)] bool ready, [MarshalAs(UnmanagedType.I1)] bool busy);
    [DllImport("__Internal")] private static extern void BnsmIosShare(string path);
    [DllImport("__Internal")] private static extern void BnsmIosFinishBackground(int token);
    private static readonly BackgroundSave backgroundSave = SaveInBackground;
    private static LocalIosHost instance;
    private string root, evidence, importPath, exports, cache;
    private string message = "准备 iOS 内置服务端", shareResult;
    private Task operation;
    private byte[] archive;
    private bool preparing;
    private string lastNativeStatus;
    private bool lastReady, lastBusy;
    [Serializable] private sealed class ExportInfo
    {
        public string product = "BnsmLocalGame", platform = "iOS", exportedUtc, unity, app, buildGuid;
        public bool saveFilesIncluded, deviceVerified = false;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (!LocalMode.IosLocal || instance != null) return;
        var owner = new GameObject("BnsmIosHost"); DontDestroyOnLoad(owner);
        instance = owner.AddComponent<LocalIosHost>();
        instance.root = Path.Combine(Application.persistentDataPath, "EmbeddedServer");
        instance.cache = Application.temporaryCachePath;
        instance.evidence = Path.Combine(Application.persistentDataPath, "Diagnostics/embedded-state.json");
        instance.importPath = Path.Combine(instance.cache, "BnsmImport.bnsm-save.zip");
        instance.exports = Path.Combine(Application.persistentDataPath, "SupportExports");
        Directory.CreateDirectory(Path.GetDirectoryName(instance.evidence));
        BnsmIosInstall(owner.name, instance.importPath, backgroundSave);
        LocalAndroidDiagnostics.Trace("ios-host-installed; build=" + Application.buildGUID);
    }
    public static IEnumerator Prepare()
    {
        Initialize();
        if (!instance.preparing) instance.StartCoroutine(instance.PrepareCore());
        while (!Bnsm.UnityHost.Host.Ready) yield return null;
        LocalEmbeddedServer.PublishIosState(true, Bnsm.UnityHost.Host.Status);
    }
    private IEnumerator PrepareCore()
    {
        preparing = true;
        using (var request = UnityWebRequest.Get(LocalMode.StreamingUri("embedded-server.zip")))
        {
            yield return request.SendWebRequest();
            if (!string.IsNullOrEmpty(request.error))
            {
                message = "内置数据读取失败：" + request.error;
                BnsmIosNotice(message); preparing = false; yield break;
            }
            archive = request.downloadHandler.data;
        }
        if (Bnsm.UnityHost.Host.HasWorkspace(Path.Combine(root, "Saves"))) StartHost();
        else { message = "首次使用请选择新建存档，或导入 .bnsm-save.zip"; BnsmIosChooseProfile(); }
    }
    private void Begin(Func<Task> action, string success)
    {
        if (operation != null) { BnsmIosNotice("正在处理，请稍候"); return; }
        message = success;
        operation = Task.Run(action);
    }
    private void StartHost()
    {
        if (archive == null) { BnsmIosNotice("正在准备内置数据，请稍候"); return; }
        Begin(() => Bnsm.UnityHost.Host.Start(archive, root, evidence), "内置服务端已就绪");
    }
    [UnityEngine.Scripting.Preserve]
    public void Command(string command)
    {
        if (operation != null) { BnsmIosNotice("正在处理，请稍候"); return; }
        if (command == "new" || command == "retry")
        {
            if (archive == null)
            {
                if (!preparing) StartCoroutine(PrepareCore()); else BnsmIosNotice("正在读取内置数据，请稍候");
            }
            else if (command == "retry" && !Bnsm.UnityHost.Host.HasWorkspace(Path.Combine(root, "Saves"))) BnsmIosChooseProfile();
            else StartHost();
        }
        else if (command == "import")
        {
            if (Bnsm.UnityHost.Host.Ready) { BnsmIosNotice("请在启动游戏前导入存档。"); return; }
            Begin(async () => {
                await Bnsm.UnityHost.Host.Import(root, importPath);
                await Bnsm.UnityHost.Host.Start(archive, root, evidence);
                if (File.Exists(importPath)) File.Delete(importPath);
            }, "存档已导入为独立进度，内置服务端已就绪");
        }
        else if (command == "checkpoint")
        {
            if (!Bnsm.UnityHost.Host.Ready) { BnsmIosNotice("内置服尚未就绪，可以先导出日志。"); return; }
            Begin(Bnsm.UnityHost.Host.Checkpoint, "存档已保存");
        }
        else if (command == "debug") LocalDevPanel.Open();
        else if (command == "export")
        {
            bool includeSave = Bnsm.UnityHost.Host.Ready;
            string snapshot = includeSave ? Path.Combine(cache, Guid.NewGuid().ToString("N") + ".bnsm-save.zip") : null;
            string clientLogs = Path.GetDirectoryName(evidence), serverLogs = Path.Combine(root, "Diagnostics");
            string metadata = JsonUtility.ToJson(new ExportInfo { exportedUtc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion, app = Application.version, buildGuid = Application.buildGUID, saveFilesIncluded = includeSave }, true);
            Begin(async () => {
                try
                {
                    if (snapshot != null) await Bnsm.UnityHost.Host.Export(snapshot);
                    shareResult = LocalMobileSupportArchive.Create(exports, clientLogs, serverLogs, evidence, snapshot, metadata);
                }
                finally { if (snapshot != null && File.Exists(snapshot)) File.Delete(snapshot); }
            }, includeSave ? "存档与日志已导出；不包含未结算的战斗" : "启动日志已导出，不包含存档");
        }
    }
    private void Update()
    {
        if (operation != null && operation.IsCompleted)
        {
            bool ok = !operation.IsFaulted && !operation.IsCanceled;
            if (!ok)
            {
                Exception error = operation.IsCanceled ? new OperationCanceledException("操作已取消，请重试") : operation.Exception.GetBaseException();
                message = "操作失败：" + error.Message;
                Debug.LogException(error);
            }
            operation = null; BnsmIosNotice(message);
            if (ok && shareResult != null) BnsmIosShare(shareResult);
            shareResult = null;
        }
        string status = operation != null ? Bnsm.UnityHost.Host.Status : message;
        LocalEmbeddedServer.PublishIosState(Bnsm.UnityHost.Host.Ready, status);
        bool ready = Bnsm.UnityHost.Host.Ready, busy = operation != null;
        if (status != lastNativeStatus || ready != lastReady || busy != lastBusy)
        {
            BnsmIosState(status, ready, busy);
            lastNativeStatus = status; lastReady = ready; lastBusy = busy;
        }
    }
    [AOT.MonoPInvokeCallback(typeof(BackgroundSave))]
    private static void SaveInBackground(int token)
    {
        // Native callback starts before suspension; it cannot depend on another Unity frame.
        _ = Task.Run(async () => {
            try { await Bnsm.UnityHost.Host.Checkpoint(); LocalAndroidDiagnostics.Trace("ios-background-checkpoint-complete"); }
            catch (Exception error) { LocalAndroidDiagnostics.Trace("ios-background-checkpoint-failed " + error); }
            finally { BnsmIosFinishBackground(token); }
        });
    }
    private void OnApplicationQuit()
    {
        // iOS need not deliver this after force-quit. Mutations and background checkpoints are the durable path.
        try { Task.Run(Bnsm.UnityHost.Host.Stop).Wait(2000); }
        catch (Exception error) { LocalAndroidDiagnostics.Trace("ios-quit " + error.Message); }
    }
}
#endif
