using System.Security.Cryptography;
using System.Text.Json;
using Bnsm.Server;

/// <summary>Repair only the known Android Double representation of the NPC tutorial sentinel.</summary>
internal static class AndroidBotSaveRecovery
{
    internal static int Recover(string directory)
    {
        string path=Path.Combine(directory,"bot-world.json");if(!File.Exists(path))return 0;
        byte[] original=File.ReadAllBytes(path);using var document=JsonDocument.Parse(original);
        var root=document.RootElement;
        if(!root.TryGetProperty("Bots",out var bots)||bots.ValueKind!=JsonValueKind.Array)return 0;
        var damaged=new HashSet<int>();int index=0;
        foreach(var bot in bots.EnumerateArray()){
            if(bot.TryGetProperty("Uid",out var id)&&id.TryGetUInt32(out uint uid)&&uid>=900000&&
                bot.TryGetProperty("Profile",out var profile)&&profile.TryGetProperty("Uid",out var profileId)&&profileId.TryGetUInt32(out uint puid)&&puid==uid&&
                profile.TryGetProperty("Account",out var account)&&account.GetString()=="world-bot:"+uid&&
                profile.TryGetProperty("TutorialStatus",out var tutorial)&&tutorial.ValueKind==JsonValueKind.Number&&
                (tutorial.GetRawText().Equals("1.8446744073709552E19",StringComparison.OrdinalIgnoreCase)||tutorial.GetRawText().Equals("1.8446744073709552E+19",StringComparison.OrdinalIgnoreCase)))damaged.Add(index);
            index++;
        }
        if(damaged.Count==0)return 0;
        using var output=new MemoryStream();
        using(var writer=new Utf8JsonWriter(output)){
            writer.WriteStartObject();
            foreach(var field in root.EnumerateObject()){
                if(field.Name!="Bots"){field.WriteTo(writer);continue;}
                writer.WritePropertyName(field.Name);writer.WriteStartArray();index=0;
                foreach(var bot in bots.EnumerateArray()){
                    if(!damaged.Contains(index++)){bot.WriteTo(writer);continue;}
                    writer.WriteStartObject();
                    foreach(var property in bot.EnumerateObject()){
                        if(property.Name!="Profile"){property.WriteTo(writer);continue;}
                        writer.WritePropertyName("Profile");writer.WriteStartObject();
                        foreach(var value in property.Value.EnumerateObject())if(value.Name=="TutorialStatus")writer.WriteNumber("TutorialStatus",ulong.MaxValue);else value.WriteTo(writer);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        byte[] recovered=output.ToArray();
        // Do not repair around unrelated damage. Validate the complete candidate
        // before committing anything; human saves are never rewritten here.
        SaveWorkspace.ValidateData(directory,recovered);
        string backupDirectory=Path.Combine(Path.GetDirectoryName(directory)!,"recovery");Directory.CreateDirectory(backupDirectory);
        string backup=Path.Combine(backupDirectory,"bot-world-"+Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant()+".json");
        if(File.Exists(backup)){
            if(!File.ReadAllBytes(backup).SequenceEqual(original))throw new IOException("人机恢复备份不匹配，原文件未修改");
        }else{
            string pending=backup+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var file=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(original);file.Flush(true);}File.Move(pending,backup);}
            finally{if(File.Exists(pending))File.Delete(pending);}
        }
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(recovered);file.Flush(true);}File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        ServerDiagnostics.Info("Recovered Android NPC tutorial sentinel; residents="+damaged.Count+"; original retained at "+backup);
        return damaged.Count;
    }
}
