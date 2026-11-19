using System.Buffers.Binary;
using System.Text;

namespace LifeVerse.Wire;

/// <summary>Escreve campos em little-endian. Valores nulos viram vazios; nunca lança por conteúdo.</summary>
public sealed class WireWriter
{
    private static readonly UTF8Encoding Utf8 = new(false, false);
    private byte[] _buffer;
    private int _length;

    public WireWriter(int capacity = 128) { _buffer = new byte[Math.Max(16, capacity)]; }

    public int Length => _length;

    public byte[] ToArray() { var result = new byte[_length]; Buffer.BlockCopy(_buffer, 0, result, 0, _length); return result; }

    private Span<byte> Reserve(int count)
    {
        var needed = _length + count;
        if (needed > _buffer.Length) Array.Resize(ref _buffer, Math.Max(needed, _buffer.Length * 2));
        var span = _buffer.AsSpan(_length, count); _length += count; return span;
    }

    public void Byte(byte value) => Reserve(1)[0] = value;
    public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);
    public void Int(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
    public void UInt(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
    public void Long(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
    public void Float(float value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), BitConverter.SingleToInt32Bits(value));

    public void VarUInt(uint value)
    {
        while (value >= 0x80) { Byte((byte)(value | 0x80)); value >>= 7; }
        Byte((byte)value);
    }

    public void String(string? value) { var bytes = Utf8.GetBytes(value ?? string.Empty); VarUInt((uint)bytes.Length); bytes.CopyTo(Reserve(bytes.Length)); }

    public void Bytes(byte[]? value) { value ??= Array.Empty<byte>(); VarUInt((uint)value.Length); value.CopyTo(Reserve(value.Length)); }

    public void Floats(float[]? values) { values ??= Array.Empty<float>(); VarUInt((uint)values.Length); foreach (var value in values) Float(value); }
}

/// <summary>
/// Lê campos em little-endian sem nunca lançar: ao primeiro problema marca <see cref="Failed"/> e devolve valores vazios.
/// Recusa floats não finitos, booleanos diferentes de 0/1, UTF-8 inválido, tamanhos acima do limite e números variáveis longos demais.
/// </summary>
public ref struct WireReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public WireReader(ReadOnlySpan<byte> data) { _data = data; _position = 0; Failed = false; }

    public bool Failed { get; private set; }
    public int Remaining => _data.Length - _position;

    private bool Take(int count)
    {
        if (Failed || count < 0 || _data.Length - _position < count) { Failed = true; return false; }
        return true;
    }

    public byte Byte() => Take(1) ? _data[_position++] : (byte)0;

    public bool Bool() { var value = Byte(); if (value > 1) Failed = true; return value == 1; }

    public int Int() { if (!Take(4)) return 0; var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_position)); _position += 4; return value; }

    public uint UInt() { if (!Take(4)) return 0; var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position)); _position += 4; return value; }

    public long Long() { if (!Take(8)) return 0; var value = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_position)); _position += 8; return value; }

    public float Float()
    {
        var value = BitConverter.Int32BitsToSingle(Int());
        if (Failed) return 0;
        if (float.IsNaN(value) || float.IsInfinity(value)) { Failed = true; return 0; }
        return value;
    }

    /// <summary>Número de até 5 bytes (7 bits cada); o quinto byte só pode usar 4 bits, para caber em uint.</summary>
    public uint VarUInt()
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = Byte();
            if (Failed) return 0;
            if (shift == 28 && (b & 0xF0) != 0) { Failed = true; return 0; }
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        Failed = true; return 0;
    }

    private int Length(int max)
    {
        var length = VarUInt();
        if (Failed) return 0;
        if (length > (uint)max) { Failed = true; return 0; }
        return (int)length;
    }

    public string String(int maxBytes)
    {
        var length = Length(maxBytes);
        if (Failed || !Take(length)) return string.Empty;
        try { var text = StrictUtf8.GetString(_data.Slice(_position, length)); _position += length; return text; }
        catch (ArgumentException) { Failed = true; return string.Empty; }
    }

    public byte[] Bytes(int maxBytes)
    {
        var length = Length(maxBytes);
        if (Failed || !Take(length)) return Array.Empty<byte>();
        var result = _data.Slice(_position, length).ToArray(); _position += length; return result;
    }

    public float[] Floats(int maxCount)
    {
        var count = Length(maxCount);
        if (Failed || !Take(count * 4)) return Array.Empty<float>();
        var result = new float[count];
        for (var i = 0; i < count; i++) result[i] = Float();
        return Failed ? Array.Empty<float>() : result;
    }
}
