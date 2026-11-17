using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>Reject an Android build with stale generated server code or content.</summary>
public static class LocalEmbeddedBuildInputs
{
    [Serializable] private sealed class Source { public string source, sha256; }
    [Serializable] private sealed class Library { public string file, sha256; }
    [Serializable] private sealed class Inputs { public Source[] sourceManifest; public Library[] libraries; public string contentSha256; }
    private static void Check(string file,string expected)
    {
        if(!File.Exists(file))throw new InvalidOperationException("Missing embedded input: "+file+"; run prepare_embedded_android.py");
        using(var hash=SHA256.Create())
            if(!string.Equals(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(file))).Replace("-",""),expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Stale embedded input: "+file+"; run prepare_embedded_android.py");
    }
    public static void Verify()
    {
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        string manifest=Path.Combine(root,"Verification/android-embedded-inputs.json");
        if(!File.Exists(manifest))throw new InvalidOperationException("Run prepare_embedded_android.py before building the integrated Android game.");
        var inputs=JsonUtility.FromJson<Inputs>(File.ReadAllText(manifest));
        if(inputs.sourceManifest==null||inputs.sourceManifest.Length<80||inputs.libraries==null)throw new InvalidDataException("Embedded inputs manifest incomplete");
        foreach(var source in inputs.sourceManifest)Check(Path.Combine(root,source.source),source.sha256);
        foreach(var library in inputs.libraries)Check(Path.Combine(Application.dataPath,"Plugins/EmbeddedServer",library.file),library.sha256);
        Check(Path.Combine(Application.streamingAssetsPath,"embedded-server.zip"),inputs.contentSha256);
    }
}
