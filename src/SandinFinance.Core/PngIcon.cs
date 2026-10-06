using System.Buffers.Binary;

namespace SandinFinance.Core;

public static class PngIcon
{
    public const int MaximumBytes = 1_048_576;
    public static void Validate(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        ReadOnlySpan<byte> end = [0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130];
        if (bytes.Length is < 45 or > MaximumBytes || !bytes.AsSpan(0, 8).SequenceEqual(signature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || !bytes.AsSpan(bytes.Length - 12).SequenceEqual(end))
            throw new ArgumentException("Выберите PNG-файл размером до 1 МБ.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width == 0 || height == 0 || width > 2048 || height > 2048)
            throw new ArgumentException("PNG-иконка должна быть не больше 2048 × 2048 пикселей.");
    }
}
