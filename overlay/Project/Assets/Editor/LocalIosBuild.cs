using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.Collections.Generic;
#endif

/// <summary>Real device Xcode export; compilation and signing are performed on macOS.</summary>
public static class LocalIosBuild
{
    [MenuItem("Bnsm Local/Modern/Export iOS integrated trial")]
    public static void Export()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
            throw new InvalidOperationException("Install Unity 6000.3 iOS Build Support first.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            throw new InvalidOperationException("Launch Unity with -buildTarget iOS before this method.");
        LocalEmbeddedBuildInputs.Verify();
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.iOS, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);
        PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "local.bnsm.iosintegrated");
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.iOS.buildNumber = "1";
        PlayerSettings.iOS.appleEnableAutomaticSigning = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.allowUnsafeCode = true;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        BnsmLocalAssetsBuild.Generate();
        BnsmModernBuild.MobileContent();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string output = Path.Combine(root, "Builds/iOS-Integrated/Xcode");
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("-bnsm-build-output=", StringComparison.Ordinal)) output = Path.GetFullPath(arg.Substring("-bnsm-build-output=".Length));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/BSC/Scenes/Startup.unity", "Assets/BSC/Scenes/PreLoad.unity", "Assets/BSC/Scenes/Game.unity" },
            locationPathName = output, target = BuildTarget.iOS,
            options = BuildOptions.CompressWithLz4HC,
            extraScriptingDefines = new[] { "BNSM_LOCAL_IOS" }
        });
        string result = "{\"target\":\"iOS\",\"result\":\"" + report.summary.result + "\",\"errors\":" + report.summary.totalErrors +
            ",\"nativeCompiled\":false,\"signed\":false,\"deviceVerified\":false}";
        File.WriteAllText(Path.Combine(root, "Verification/ios-build-result.json"), result);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("iOS Xcode export failed: " + report.summary.result);
        Debug.Log("BNSM_IOS_XCODE_EXPORT_SUCCESS " + output);
    }
#if UNITY_IOS
    [PostProcessBuild(1000)]
    public static void CompleteProject(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string pbx = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject(); project.ReadFromFile(pbx);
        string app = project.GetUnityMainTargetGuid(), framework = project.GetUnityFrameworkTargetGuid();
        project.AddFrameworkToProject(framework, "UniformTypeIdentifiers.framework", false);
        string plugin = project.FindFileGuidByProjectPath("Libraries/Plugins/iOS/BnsmIosSupport.mm");
        if (string.IsNullOrEmpty(plugin)) throw new InvalidDataException("iOS support bridge missing from exported project");
        project.SetCompileFlagsForFile(framework, plugin, new List<string> { "-fobjc-arc" });
        string iconSource = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AndroidSaveManager/app/src/main/res/drawable/cat.png"));
        File.Copy(iconSource, Path.Combine(path, "BnsmConsoleIcon.png"), true);
        string resource = project.AddFile("BnsmConsoleIcon.png", "BnsmConsoleIcon.png"); project.AddFileToBuild(app, resource);
        project.SetBuildProperty(app, "CODE_SIGN_STYLE", "Manual");
        project.SetBuildProperty(app, "DEVELOPMENT_TEAM", "");
        project.WriteToFile(pbx);
        string info = Path.Combine(path, "Info.plist"); var plist = new PlistDocument(); plist.ReadFromFile(info);
        plist.root.SetString("CFBundleDisplayName", "战斗吧剑灵 · 内置服体验");
        plist.root.SetString("CFBundleShortVersionString", "1.97.0");
        // The copy picker and explicit share sheet work without exposing live saves in Documents.
        plist.root.SetBoolean("UIFileSharingEnabled", false);
        plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", false);
        // Only localhost HTTP is needed. No broad arbitrary-load or fabricated background-mode entitlement.
        PlistElementDict ats;
        if (plist.root.values.ContainsKey("NSAppTransportSecurity")) ats = plist.root["NSAppTransportSecurity"].AsDict();
        else ats = plist.root.CreateDict("NSAppTransportSecurity");
        ats.values.Remove("NSAllowsArbitraryLoads"); ats.SetBoolean("NSAllowsLocalNetworking", true);
        plist.root.values.Remove("UIBackgroundModes");
        plist.WriteToFile(info);
        ApplyOriginalIcon(path);
    }
    private static void ApplyOriginalIcon(string export)
    {
        string original = Path.GetFullPath(Path.Combine(Application.dataPath, "../../AndroidGameIcon/res/mipmap-xxxhdpi/app_icon.png"));
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(File.ReadAllBytes(original))) throw new InvalidDataException("Original launcher icon unreadable");
        source.wrapMode = TextureWrapMode.Clamp;
        string folder = Path.Combine(export, "Unity-iPhone/Images.xcassets/AppIcon.appiconset");
        var proofs = new List<IconFile>();
        foreach (string path in Directory.GetFiles(folder, "*.png"))
        {
            var dimensions = new Texture2D(2, 2);
            if (!dimensions.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException("iOS icon unreadable");
            int width = dimensions.width, height = dimensions.height; UnityEngine.Object.DestroyImmediate(dimensions);
            var icon = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                pixels[y * width + x] = source.GetPixelBilinear((x + .5f) / width, (y + .5f) / height);
            icon.SetPixels(pixels); icon.Apply(); File.WriteAllBytes(path, icon.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(icon);
            proofs.Add(new IconFile { file = Path.GetFileName(path), sha256 = Hash(path), width = width, height = height });
        }
        UnityEngine.Object.DestroyImmediate(source);
        if (proofs.Count == 0) throw new InvalidDataException("iOS launcher icon set missing");
        string report = JsonUtility.ToJson(new IconProof { sourceSha256 = Hash(original), icons = proofs.ToArray() }, true);
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Verification/ios-launcher-icon.json")), report);
    }
    private static string Hash(string path)
    {
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    [Serializable] private sealed class IconFile { public string file, sha256; public int width, height; }
    [Serializable] private sealed class IconProof { public string sourceSha256; public IconFile[] icons; }
#endif
}
