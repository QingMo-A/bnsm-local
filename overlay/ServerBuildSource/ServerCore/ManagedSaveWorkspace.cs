using System.Text.Json;
using System.IO.Compression;

namespace Bnsm.Server;

/// <summary>Native-host facade over the exact desktop profile/backup format.</summary>
public sealed class ManagedSaveWorkspace(string root,string? legacyDirectory=null)
{
    private readonly SaveWorkspace workspace=new(root);
    public string Root=>workspace.Root;
    public void ExportSnapshot(ManagedSaveSession session,string destination,IReadOnlyDictionary<string,byte[]> snapshot,CompressionLevel compression=CompressionLevel.Optimal)
    {
        if(session.Directory!=workspace.Saves(session.Id))throw new InvalidDataException("Snapshot belongs to another workspace");
        workspace.ExportSnapshot(session.Id,destination,snapshot,compression);
    }
    public JsonElement Execute(string command,string? id=null,string? name=null,string? file=null)=>
        JsonSerializer.SerializeToElement(workspace.Command(command,id,name,file,legacyDirectory));
    public ManagedSaveSession Open(bool recoverAndroidBotNumbers=false)
    {
        var value=workspace.Open(legacyDirectory,recoverAndroidBotNumbers);
        return new(value.Id,value.Name,value.Directory,value.Lease);
    }
    public IReadOnlyList<ManagedBackup> Backups(string id)
    {
        _=workspace.Saves(id); // Validates the identifier before forming a path.
        string folder=Path.Combine(Root,"backups",id);
        return Directory.Exists(folder)?Directory.GetFiles(folder,"*.bnsm-save.zip").OrderDescending().Select(p=>new ManagedBackup(Path.GetFileName(p),p,new FileInfo(p).Length)).ToArray():[];
    }
    public IReadOnlyList<ManagedBackup> AllBackups()
    {
        string folder=Path.Combine(Root,"backups");
        return Directory.Exists(folder)?Directory.GetDirectories(folder)
            .Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),"^[a-f0-9]{32}$"))
            .SelectMany(p=>Directory.GetFiles(p,"*.bnsm-save.zip")).OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(p=>new ManagedBackup(Path.GetFileName(p),p,new FileInfo(p).Length)).ToArray():[];
    }
}
public sealed record ManagedBackup(string Name,string Path,long Bytes);
public sealed class ManagedSaveSession(string id,string name,string directory,FileStream lease):IDisposable
{
    public string Id {get;}=id;
    public string Name {get;}=name;
    public string Directory {get;}=directory;
    public void Dispose()=>lease.Dispose();
}
