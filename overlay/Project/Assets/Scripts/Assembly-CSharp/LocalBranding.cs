using UnityEngine;

/// <summary>One passive, viewport-anchored credit across startup, loading and local gameplay.</summary>
public sealed class LocalBranding : MonoBehaviour
{
    public const string Watermark = "Local Edition · By QingMo";
    public const string Credit = "移植与维护：QingMo";
    public static string DebugDescription { get; private set; } = "正在准备客户端诊断信息";
    private static LocalBranding instance;
    private GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (!LocalMode.Enabled || instance != null) return;
        var owner = new GameObject("BnsmLocalBranding");
        DontDestroyOnLoad(owner);
        instance = owner.AddComponent<LocalBranding>();
    }

    public static void SetDiagnostics(string network, string originalBuild, string version, string fps)
    {
        DebugDescription = "客户端：" + version + "\n原版构建：" + (originalBuild ?? "").Trim() +
            "\n累计通信：" + network + (string.IsNullOrEmpty(fps) ? "" : "\n" + fps);
    }

    private void OnGUI()
    {
        // Labels only paint; they never consume pointer/touch events or create an input blocker.
        if (Event.current.type != EventType.Repaint) return;
        Matrix4x4 previousMatrix = GUI.matrix;
        int previousDepth = GUI.depth;
        GUI.matrix = Matrix4x4.identity;
        GUI.depth = 10000;
        try
        {
            if (style == null) style = new GUIStyle(GUI.skin.label);
            float scale = Mathf.Clamp(Screen.height / 720f, 1f, 2f);
            style.fontSize = Mathf.RoundToInt(14f * scale);
            style.fontStyle = FontStyle.Normal;
            style.alignment = TextAnchor.LowerRight;
            style.wordWrap = false;
            Vector2 size = style.CalcSize(new GUIContent(Watermark));
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            float inset = 8f * scale;
            Rect bounds = new Rect(safe.xMax - size.x - inset,
                Screen.height - safe.yMin - size.y - inset, size.x, size.y);
            style.normal.textColor = new Color(0f, 0f, 0f, .32f);
            GUI.Label(new Rect(bounds.x + 1, bounds.y + 1, bounds.width, bounds.height), Watermark, style);
            style.normal.textColor = new Color(.78f, .81f, .83f, .8f);
            GUI.Label(bounds, Watermark, style);
        }
        finally { GUI.matrix = previousMatrix; GUI.depth = previousDepth; }
    }
}
