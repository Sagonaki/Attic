using System.Buffers.Binary;

namespace Attic.Domain.Services;

public static class KeysetCursor
{
    public static string Encode(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static bool TryDecode(string? cursor, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(cursor)) return false;

        var padded = cursor.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        try
        {
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != 8) return false;
            value = BinaryPrimitives.ReadInt64BigEndian(bytes);
            if (value < 0) return false;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
