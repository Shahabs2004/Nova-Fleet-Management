using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace NovaFleet.UnitTests.Security;

public static class PngFixture
{
    public const string Prefix = "data:image/png;base64,";

    public static string Create(int size = 256, uint? headerWidth = null, uint? headerHeight = null,
        bool metadata = false, int extraRaw = 0, byte filter = 0, bool truncateDeflate = false, bool animated = false,
        bool duplicateAdler = false)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, headerWidth ?? (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), headerHeight ?? (uint)size);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR", header);
        if (metadata)
        {
            Chunk(png, "tEXt", "comment\0private-metadata"u8.ToArray());
        }

        if (animated)
        {
            Chunk(png, "acTL", new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 });
        }

        var raw = new byte[size * (1 + size * 4) + extraRaw];
        raw[0] = filter;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var idat = compressed.ToArray();
        if (duplicateAdler)
            idat = [.. idat, .. idat[^4..]];
        Chunk(png, "IDAT", truncateDeflate ? idat[..^4] : idat);
        Chunk(png, "IEND", []);
        return Prefix + Convert.ToBase64String(png.ToArray());
    }

    public static string Invalid(string kind) => kind switch
    {
        "empty" => "",
        "url" => "https://example.test/logo.png",
        "svg" => "data:image/svg+xml;base64," + Convert.ToBase64String("<svg/>"u8),
        "jpeg" => "data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 255, 216, 255, 217 }),
        "nonimage" => Prefix + Convert.ToBase64String("not an image"u8),
        "base64" => Prefix + "not%%%base64",
        "whitespace" => Create() + "\n",
        "header" => Create().Replace("image/png", "image/PNG"),
        "encodedOversize" => Prefix + new string('A', 350000),
        "decodedOversize" => Prefix + Convert.ToBase64String(new byte[256 * 1024 + 1]),
        "nonsquare" => Create(headerHeight: 32),
        "dimensions" => Create(headerWidth: 257, headerHeight: 257),
        "dimensionBomb" => Create(headerWidth: uint.MaxValue, headerHeight: uint.MaxValue),
        "truncated" => Truncate(Create(), 12),
        "crc" => CorruptCrc(),
        "inflationBomb" => Create(extraRaw: 1000000),
        "filter" => Create(filter: 5),
        "truncatedDeflate" => Create(truncateDeflate: true),
        "animated" => Create(animated: true),
        "idatTrailingData" => Create(duplicateAdler: true),
        "trailingData" => Prefix + Convert.ToBase64String([.. Convert.FromBase64String(Create()[Prefix.Length..]), 1]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Truncate(string value, int count)
    {
        var bytes = Convert.FromBase64String(value[Prefix.Length..]);
        return Prefix + Convert.ToBase64String(bytes[..^count]);
    }

    private static string CorruptCrc()
    {
        var bytes = Convert.FromBase64String(Create()[Prefix.Length..]);
        bytes[29] ^= 1;
        return Prefix + Convert.ToBase64String(bytes);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        stream.Write(number);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(body);
        uint crc = uint.MaxValue;
        foreach (var value in body)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }
}
