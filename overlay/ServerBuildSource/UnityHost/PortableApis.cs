global using Bnsm.UnityCompat;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bnsm.UnityCompat
{
    internal static class Api
    {
        public static readonly Random Random=new LockedRandom();
        public static byte[] Sha256(byte[] bytes){using var hash=SHA256.Create();return hash.ComputeHash(bytes);}
        public static string ToHexString(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","");
        public static string GetHexString(int length){var bytes=new byte[(length+1)/2];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return ToHexString(bytes).Substring(0,length);}
        public static T[] EnumValues<T>() where T:struct,System.Enum=>(T[])System.Enum.GetValues(typeof(T));
        public static bool EnumDefined<T>(T value) where T:struct,System.Enum=>System.Enum.IsDefined(typeof(T),value);
        public static bool EnumDefined(Type type,object value)=>System.Enum.IsDefined(type,value);
        public static void Move(string source,string target)=>File.Move(source,target);
        public static void Move(string source,string target,bool overwrite)
        {
            if(overwrite&&File.Exists(target))File.Replace(source,target,null);
            else File.Move(source,target);
        }
    }
    internal sealed class LockedRandom:Random
    {
        private readonly object gate=new();private readonly Random inner=new();
        public override int Next(){lock(gate)return inner.Next();}
        public override int Next(int max){lock(gate)return inner.Next(max);}
        public override int Next(int min,int max){lock(gate)return inner.Next(min,max);}
        public override double NextDouble(){lock(gate)return inner.NextDouble();}
        public override void NextBytes(byte[] bytes){lock(gate)inner.NextBytes(bytes);}
    }
    internal static class Extensions
    {
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source)=>source.OrderBy(x=>x);
        public static IOrderedEnumerable<T> OrderDescending<T>(this IEnumerable<T> source)=>source.OrderByDescending(x=>x);
        public static IEnumerable<T> DistinctBy<T,TKey>(this IEnumerable<T> source,Func<T,TKey> key){var seen=new HashSet<TKey>();foreach(var value in source)if(seen.Add(key(value)))yield return value;}
        public static IEnumerable<T[]> Chunk<T>(this IEnumerable<T> source,int size)
        {
            if(size<=0)throw new ArgumentOutOfRangeException(nameof(size));
            var batch=new List<T>(size);foreach(var item in source){batch.Add(item);if(batch.Count==size){yield return batch.ToArray();batch.Clear();}}if(batch.Count>0)yield return batch.ToArray();
        }
        public static Dictionary<TKey,TValue> ToDictionary<TKey,TValue>(this IEnumerable<KeyValuePair<TKey,TValue>> source) where TKey:notnull=>source.ToDictionary(x=>x.Key,x=>x.Value);
        public static long NextInt64(this Random random,long max)
        {
            if(max<=0)throw new ArgumentOutOfRangeException(nameof(max));
            ulong bound=(ulong)max,limit=ulong.MaxValue-ulong.MaxValue%bound,value;var bytes=new byte[8];
            do{random.NextBytes(bytes);value=BitConverter.ToUInt64(bytes,0);}while(value>=limit);return (long)(value%bound);
        }
        public static async Task ReadExactlyAsync(this Stream stream,Memory<byte> bytes,CancellationToken token)
        {
            while(bytes.Length>0){int count=await stream.ReadAsync(bytes,token);if(count==0)throw new EndOfStreamException();bytes=bytes.Slice(count);}
        }
        public static async Task<TcpClient> AcceptTcpClientAsync(this TcpListener listener,CancellationToken token)
        {
            using var registration=token.Register(listener.Stop);
            try{return await listener.AcceptTcpClientAsync();}
            catch(Exception) when(token.IsCancellationRequested){throw new OperationCanceledException(token);}
        }
    }
    // The original replay is zlib-wrapped DEFLATE. Keep header and Adler-32
    // validation rather than treating a raw DEFLATE decode as a valid replay.
    internal sealed class CheckedZLibStream:Stream
    {
        private readonly Stream input;private readonly bool leaveOpen;private readonly DeflateStream decoded;private readonly uint expected;
        private uint a=1,b;private bool checkedEnd;
        public CheckedZLibStream(Stream source,CompressionMode mode,bool leaveOpen)
        {
            if(mode!=CompressionMode.Decompress)throw new NotSupportedException("Research adapter only decodes validated replay uploads");
            this.input=source;this.leaveOpen=leaveOpen;
            if(!source.CanSeek||source.Length-source.Position>32*1024*1024)throw new InvalidDataException("Replay compressed length invalid");
            using var all=new MemoryStream();source.CopyTo(all);byte[] bytes=all.ToArray();int n=bytes.Length;
            if(n<6||(bytes[0]&15)!=8||(bytes[0]>>4)>7||((bytes[0]<<8)+bytes[1])%31!=0||(bytes[1]&32)!=0)throw new InvalidDataException("Invalid zlib header");
            expected=((uint)bytes[n-4]<<24)|((uint)bytes[n-3]<<16)|((uint)bytes[n-2]<<8)|bytes[n-1];
            decoded=new DeflateStream(new MemoryStream(bytes,2,n-6,false),CompressionMode.Decompress);
        }
        public override int Read(byte[] buffer,int offset,int count)
        {
            int read=decoded.Read(buffer,offset,count);
            for(int i=offset;i<offset+read;i++){a=(a+buffer[i])%65521;b=(b+a)%65521;}
            if(count>0&&read==0&&!checkedEnd){checkedEnd=true;if(((b<<16)|a)!=expected)throw new InvalidDataException("Replay zlib Adler-32 mismatch");}return read;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing){decoded.Dispose();if(!leaveOpen)input.Dispose();}base.Dispose(disposing);}
    }
}
