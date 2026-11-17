using System;
using System.IO;
using System.IO.Compression;
using System.Text;

/// <summary>Immutable support bundle. Save bytes must come from Host.Export, never a live file copy.</summary>
public static class LocalMobileSupportArchive
{
    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using (var output = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) output.Write(bytes, 0, bytes.Length);
    }
    private static byte[] ReadShared(string path, int maximum)
    {
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            int length = (int)Math.Min(source.Length, maximum);
            source.Position = Math.Max(0, source.Length - length);
            byte[] bytes = new byte[length]; int offset = 0, count;
            while (offset < length && (count = source.Read(bytes, offset, length - offset)) > 0) offset += count;
            if (offset != length) Array.Resize(ref bytes, offset);
            return bytes;
        }
    }
    public static string Create(string outputDirectory, string clientLogs, string serverLogs, string evidence,
        string saveSnapshot, string diagnosticsJson)
    {
        Directory.CreateDirectory(outputDirectory);
        string destination = Path.Combine(outputDirectory, "bnsm-" + (saveSnapshot == null ? "logs-" : "save-and-logs-") +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
        string partial = destination + ".partial";
        try
        {
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                if (saveSnapshot != null)
                {
                    if (new FileInfo(saveSnapshot).Length > 128 * 1024 * 1024) throw new IOException("存档快照超过导出限制。");
                    Add(zip, "save.bnsm-save.zip", File.ReadAllBytes(saveSnapshot));
                }
                string[] roots = { clientLogs, serverLogs };
                string[] groups = { "client", "server" };
                for (int group = 0; group < roots.Length; group++)
                    for (int index = 0; index < 4; index++)
                    {
                        string name = groups[group] + (index == 0 ? "" : "." + index) + ".log";
                        string path = Path.Combine(roots[group], name);
                        try { if (File.Exists(path)) Add(zip, "logs/" + groups[group] + "/" + name, ReadShared(path, 3 * 1024 * 1024)); }
                        catch (FileNotFoundException) { /* Rotated while exporting. */ }
                    }
                if (File.Exists(evidence)) Add(zip, "host-state.json", ReadShared(evidence, 256 * 1024));
                Add(zip, "diagnostics.json", Encoding.UTF8.GetBytes(diagnosticsJson));
                Add(zip, "说明.txt", Encoding.UTF8.GetBytes("生成时间（UTC）：" + DateTime.UtcNow.ToString("O") + "\n" +
                    (saveSnapshot == null ? "服务端尚未就绪；仅含本机日志，不含存档。" :
                    "含已提交的存档与本机日志。恢复请导入 save.bnsm-save.zip；不支持恢复未结算的战斗。") + "\n移植与维护：QingMo"));
            }
            File.Move(partial, destination);
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
