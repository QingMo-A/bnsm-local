using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Bnsm.Server;

/// <summary>Bounded asynchronous local logs. Packet threads never wait on disk writes.</summary>
public sealed class ServerDiagnostics : IAsyncDisposable
{
    private const int MaximumFileBytes=2*1024*1024,RetainedFiles=4,MaximumLineChars=4096;
    private readonly object fileGate=new();
    private readonly ConcurrentQueue<string> recent=new();
    private readonly Channel<string> queue;
    private readonly Task worker;
    private readonly TextWriter originalOut,originalError;
    private readonly CaptureWriter capturedOut,capturedError;
    private readonly DateTime started=DateTime.UtcNow;
    private StreamWriter? writer;
    private long bytes,dropped;
    private bool diskFailed,disposed;
    public string DirectoryPath {get;}
    public static ServerDiagnostics? Current {get;private set;}
    public Func<object>? ContextProvider {get;set;}
    public static ServerDiagnostics Begin(string directory)
    {
        if(Current!=null)throw new InvalidOperationException("Diagnostics already initialized");
        return Current=new ServerDiagnostics(directory);
    }
    private ServerDiagnostics(string directory)
    {
        DirectoryPath=Path.GetFullPath(directory);
        try {
            Directory.CreateDirectory(DirectoryPath);
            string previous=Path.Combine(DirectoryPath,"server.log");
            if(File.Exists(previous))foreach(string line in File.ReadLines(previous).TakeLast(100))recent.Enqueue(line);
        } catch {diskFailed=true;}
        queue=Channel.CreateBounded<string>(new BoundedChannelOptions(1024){SingleReader=true,FullMode=BoundedChannelFullMode.DropOldest},_=>Interlocked.Increment(ref dropped));
        originalOut=Console.Out;originalError=Console.Error;
        capturedOut=new(this,originalOut,"INFO");capturedError=new(this,originalError,"ERROR");
        Console.SetOut(capturedOut);Console.SetError(capturedError);
        worker=Task.Run(WriteLoop);
        Write("INFO","Service process started; bounded log retention 4 x 2 MiB");
    }
    public static void Info(string message)=>Current?.Write("INFO",message);
    public static void Error(string message)=>Current?.Write("ERROR",message);
    public void Write(string level,string message)
    {
        if(disposed)return;
        foreach(string raw in (message??"").Replace("\r","").Split('\n').Take(32))
        {
            string text=raw.Length>MaximumLineChars?raw[..MaximumLineChars]+" [truncated]":raw;
            try {text=Regex.Replace(text,"(loginSessionKey|sessionKey|password|userPW|token)([\\\"'\\s:=]+)([^\\s,;\\\"']+)","$1$2<redacted>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(50));}
            catch(RegexMatchTimeoutException){text="[log line omitted: redaction timeout]";}
            string line=DateTime.UtcNow.ToString("O")+" ["+level+"] "+text;
            recent.Enqueue(line);while(recent.Count>500)recent.TryDequeue(out _);
            queue.Writer.TryWrite(line);
        }
    }
    private async Task WriteLoop()
    {
        await foreach(string line in queue.Reader.ReadAllAsync())
        {
            if(diskFailed)continue;
            try {lock(fileGate){
                int size=Encoding.UTF8.GetByteCount(line)+1;
                if(writer==null)Open();
                if(bytes+size>MaximumFileBytes)Rotate();
                writer!.WriteLine(line);writer.Flush();bytes+=size;
            }} catch {diskFailed=true;lock(fileGate){writer?.Dispose();writer=null;}}
        }
        lock(fileGate){writer?.Dispose();writer=null;}
    }
    private void Open()
    {
        string path=Path.Combine(DirectoryPath,"server.log");
        bytes=File.Exists(path)?new FileInfo(path).Length:0;
        writer=new StreamWriter(new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read|FileShare.Delete),new UTF8Encoding(false));
    }
    private void Rotate()
    {
        writer?.Dispose();writer=null;
        for(int i=RetainedFiles-1;i>=1;i--){
            string target=Path.Combine(DirectoryPath,"server."+i+".log");
            string source=Path.Combine(DirectoryPath,i==1?"server.log":"server."+(i-1)+".log");
            if(File.Exists(target))File.Delete(target);
            if(File.Exists(source))File.Move(source,target);
        }
        Open();
    }
    public string Tail(int lines=300)
    {
        string text=string.Join('\n',recent.ToArray().TakeLast(Math.Clamp(lines,1,500)));
        return text.Length>100000?text[^100000..]:text;
    }
    public byte[] Export()
    {
        using var output=new MemoryStream();
        using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            lock(fileGate){
                writer?.Flush();
                foreach(string name in Enumerable.Range(0,RetainedFiles).Select(i=>i==0?"server.log":"server."+i+".log")) {
                    string path=Path.Combine(DirectoryPath,name);
                    if(File.Exists(path)) {
                        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                        using var entry=archive.CreateEntry(name,CompressionLevel.Fastest).Open();input.CopyTo(entry);
                    }
                }
            }
            using(var text=new StreamWriter(archive.CreateEntry("recent-tail.log").Open(),new UTF8Encoding(false)))text.Write(Tail(500));
            if(ContextProvider!=null) {
                try {using var context=archive.CreateEntry("host-context.json").Open();JsonSerializer.Serialize(context,ContextProvider());}
                catch(Exception error){using var context=new StreamWriter(archive.CreateEntry("host-context-error.txt").Open());context.Write(error.GetType().Name);}
            }
            using(var info=archive.CreateEntry("diagnostics.json").Open())JsonSerializer.Serialize(info,new{product="BnsmLocalGame",exportedUtc=DateTime.UtcNow,startedUtc=started,processId=Environment.ProcessId,runtime=Environment.Version.ToString(),os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),diskLoggingAvailable=!diskFailed,droppedQueuedLines=Interlocked.Read(ref dropped),saveFilesIncluded=false});
        }
        return output.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;
        Write("INFO","Service process log closing");disposed=true;
        Console.SetOut(originalOut);Console.SetError(originalError);
        queue.Writer.TryComplete();await worker;
        if(ReferenceEquals(Current,this))Current=null;
    }
    private sealed class CaptureWriter(ServerDiagnostics owner,TextWriter original,string level):TextWriter
    {
        private readonly ThreadLocal<StringBuilder> partial=new(()=>new());
        public override Encoding Encoding=>Encoding.UTF8;
        public override void WriteLine(string? value){original.WriteLine(value);owner.Write(level,value??"");}
        public override void Write(string? value){if(value!=null)foreach(char c in value)Write(c);}
        public override void Write(char value){var text=partial.Value!;if(value=='\n'){WriteLine(text.ToString().TrimEnd('\r'));text.Clear();}else if(text.Length<MaximumLineChars)text.Append(value);}
        public override void Flush()=>original.Flush();
    }
}
