using System.Text;

namespace Nexer.Rendering;

/// <summary>Reusable growable byte buffer with VT-friendly appenders. Not thread-safe.</summary>
internal sealed class OutputBuffer
{
    private byte[] buffer = new byte[4096];

    public int Length { get; private set; }

    public ReadOnlyMemory<byte> Slice(int start) => buffer.AsMemory(start, Length - start);

    public void Reset(int reserve)
    {
        Ensure(reserve);
        Length = reserve;
    }

    public void WriteAt(int offset, ReadOnlySpan<byte> bytes) => bytes.CopyTo(buffer.AsSpan(offset));

    public void Append(byte value)
    {
        Ensure(Length + 1);
        buffer[Length++] = value;
    }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        Ensure(Length + bytes.Length);
        bytes.CopyTo(buffer.AsSpan(Length));
        Length += bytes.Length;
    }

    public void AppendNumber(int value)
    {
        Ensure(Length + 11);
        value.TryFormat(buffer.AsSpan(Length), out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
        Length += written;
    }

    public void AppendRune(Rune rune)
    {
        Ensure(Length + 4);
        Length += rune.EncodeToUtf8(buffer.AsSpan(Length));
    }

    public void AppendText(string text)
    {
        Ensure(Length + Encoding.UTF8.GetMaxByteCount(text.Length));
        Length += Encoding.UTF8.GetBytes(text, buffer.AsSpan(Length));
    }

    /// <summary>CSI n final; n omitted when it equals 1.</summary>
    public void AppendCsi(int n, byte final)
    {
        Append("\e["u8);
        if (n != 1) AppendNumber(n);
        Append(final);
    }

    private void Ensure(int capacity)
    {
        if (capacity <= buffer.Length) return;
        Array.Resize(ref buffer, Math.Max(capacity, buffer.Length * 2));
    }

    public static int DigitCount(int value) => value < 10 ? 1 : value < 100 ? 2 : value < 1000 ? 3 : value < 10000 ? 4 : 10;
}
