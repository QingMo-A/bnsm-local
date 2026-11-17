using System.Buffers.Binary;
using ProtoBuf;

internal sealed class WireCodec
{
    public const int HeaderSize = 8;
    public const int MaxPacketSize = 8192;
    private readonly int[] keys;
    public WireCodec(int[] keys)
    {
        if (keys.Length == 0) throw new ArgumentException("Original packet keys are required");
        this.keys = keys;
    }
    private int Key(uint index)
    {
        unchecked
        {
            index ^= index >> 16; index *= 2246822507u;
            index ^= index >> 13; index *= 3266489909u;
            index ^= index >> 16;
            return keys[index % (uint)keys.Length];
        }
    }
    private void Xor(byte[] buffer, int start, uint index)
    {
        var key = BitConverter.GetBytes(Key(index));
        int offset = start;
        while (offset + 3 < buffer.Length)
        {
            for (int i = 0; i < 4; i++) buffer[offset++] ^= key[i];
        }
        // The original client restarts at key[0] for each trailing byte.
        while (offset < buffer.Length) buffer[offset++] ^= key[0];
    }
    public static uint Crc(ReadOnlySpan<byte> data)
    {
        uint value = uint.MaxValue;
        foreach (byte b in data)
        {
            value ^= b;
            for (int i = 0; i < 8; i++) value = (value & 1) == 0 ? value >> 1 : 0xEDB88320u ^ (value >> 1);
        }
        return ~value;
    }
    public static byte[] Serialize(object? message)
    {
        if (message == null) return [];
        using var stream = new MemoryStream();
        Serializer.NonGeneric.Serialize(stream, message);
        return stream.ToArray();
    }
    public static object? Deserialize(Type? type, byte[] payload)
    {
        if (type == null) return null;
        using var stream = new MemoryStream(payload);
        return Serializer.NonGeneric.Deserialize(type, stream);
    }
    public byte[] Encode(ushort command, byte[] payload, bool encrypted, uint? sequence = null)
    {
        if (payload.Length + HeaderSize > MaxPacketSize) throw new InvalidDataException("Packet exceeds original client size limit");
        var buffer = new byte[payload.Length + HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)buffer.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), command);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), sequence ?? Crc(payload));
        payload.CopyTo(buffer, HeaderSize);
        if (encrypted) { Xor(buffer, HeaderSize, command); Xor(buffer, 2, (uint)buffer.Length); }
        return buffer;
    }
    public (ushort Command, uint Sequence, byte[] Payload) Decode(byte[] buffer, bool encrypted)
    {
        if (buffer.Length < HeaderSize || buffer.Length > MaxPacketSize || BinaryPrimitives.ReadUInt16LittleEndian(buffer) != buffer.Length)
            throw new InvalidDataException("Invalid frame length");
        if (encrypted) Xor(buffer, 2, (uint)buffer.Length);
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(2));
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));
        if (encrypted) Xor(buffer, HeaderSize, command);
        return (command, sequence, buffer[HeaderSize..]);
    }
    public static async Task<byte[]?> ReadFrame(Stream stream, CancellationToken cancellation)
    {
        var length = new byte[2];
        int first = await stream.ReadAsync(length.AsMemory(0, 1), cancellation);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(length.AsMemory(1, 1), cancellation);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(length);
        if (size < HeaderSize || size > MaxPacketSize) throw new InvalidDataException($"Invalid frame length: {size}");
        var buffer = new byte[size]; length.CopyTo(buffer, 0);
        await stream.ReadExactlyAsync(buffer.AsMemory(2), cancellation);
        return buffer;
    }
}
