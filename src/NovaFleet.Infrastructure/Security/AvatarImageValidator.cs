using System.Buffers.Binary;
using System.IO.Compression;
using NovaFleet.Application.Common;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace NovaFleet.Infrastructure.Security;

/// <summary>Bounded PNG validation and raster normalization shared by user and driver writes.</summary>
public static class AvatarImageValidator
{
    public const int MaxDecodedBytes = 256 * 1024;
    private const string Prefix = "data:image/png;base64,";
    private static readonly int MaxInputLength = Prefix.Length + ((MaxDecodedBytes + 2) / 3 * 4);
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static string? ValidateAndNormalize(string? value)
    {
        if (value is null)
            return null;

        if (value.Length > MaxInputLength)
            throw TooLarge();
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw InvalidImage();

        try
        {
            var payload = value[Prefix.Length..];
            var bytes = Convert.FromBase64String(payload);
            if (bytes.Length > MaxDecodedBytes)
                throw TooLarge();
            // Convert accepts whitespace and noncanonical padding; the wire format does not.
            if (Convert.ToBase64String(bytes) != payload)
                throw InvalidImage();

            var (png, size) = ValidatePng(bytes);
            using var image = Image.FromBinaryData(png);
            if (image.Size.Width != size || image.Size.Height != size)
                throw InvalidImage();

            // The public Image API has no encoder. Rasterize a pixel-sized, transparent page
            // using the existing QuestPDF renderer instead of reflecting into its internal Skia API.
            var normalized = Document.Create(document => document.Page(page =>
            {
                page.Size(size, size);
                page.Content().Image(image).UseOriginalImage();
            })).GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                RasterDpi = 72,
                UseTransparentBackground = true
            }).Single();

            if (normalized.Length > MaxDecodedBytes)
                throw TooLarge();
            return Prefix + Convert.ToBase64String(normalized);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException
                                   or DocumentComposeException or DocumentDrawingException)
        {
            // Do not expose or log decoder errors or image data.
            throw InvalidImage();
        }
    }

    private static (byte[] Png, int Size) ValidatePng(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length < 45 || !data[..8].SequenceEqual(Signature)
            || ReadUInt(data[8..]) != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw InvalidImage();

        // Check dimensions before invoking any decoder or inflating any data.
        var width = ReadUInt(data[16..]);
        var height = ReadUInt(data[20..]);
        if (width is 0 or > 256 || height != width)
            throw new ValidationAppException("تصویر پروفایل باید مربع و حداکثر ۲۵۶ × ۲۵۶ پیکسل باشد.");

        int depth = data[24], color = data[25];
        var validDepth = color switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => false
        };
        if (!validDepth || data[26] != 0 || data[27] != 0 || data[28] > 1)
            throw InvalidImage();

        using var sanitized = new MemoryStream();
        using var compressed = new MemoryStream();
        sanitized.Write(Signature);
        var paletteEntries = 0;
        var seenTransparency = false;
        var seenData = false;
        var dataEnded = false;
        var ended = false;
        for (var offset = 8; offset < data.Length;)
        {
            if (data.Length - offset < 12)
                throw InvalidImage();
            var length = ReadUInt(data[offset..]);
            if (length > data.Length - offset - 12)
                throw InvalidImage();
            var count = (int)length;
            var chunk = data.Slice(offset, count + 12);
            var type = chunk.Slice(4, 4);
            foreach (var letter in type)
                if (!(letter is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                    throw InvalidImage();
            if ((type[2] & 32) != 0 || Crc32(chunk.Slice(4, count + 4)) != ReadUInt(chunk[(count + 8)..]))
                throw InvalidImage();

            var keep = true;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (offset != 8)
                    throw InvalidImage();
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (paletteEntries != 0 || seenData || seenTransparency || color is 0 or 4
                    || count == 0 || count % 3 != 0 || count > 768
                    || (color == 3 && count / 3 > (1 << depth)))
                    throw InvalidImage();
                paletteEntries = count / 3;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (seenTransparency || seenData || !(color switch
                    {
                        0 => count == 2,
                        2 => count == 6,
                        3 => count > 0 && count <= paletteEntries,
                        _ => false
                    }))
                    throw InvalidImage();
                seenTransparency = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded || (color == 3 && paletteEntries == 0))
                    throw InvalidImage();
                seenData = true;
                compressed.Write(chunk.Slice(8, count));
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!seenData || count != 0 || offset + 12 != data.Length)
                    throw InvalidImage();
                ended = true;
            }
            else
            {
                // Reject unknown critical chunks and animation. Strip all other metadata before
                // native decoding, including compressed ICC/text profiles (another bomb vector).
                if ((type[0] & 32) == 0 || type.SequenceEqual("acTL"u8)
                    || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                    throw InvalidImage();
                keep = false;
            }

            if (seenData && !type.SequenceEqual("IDAT"u8))
                dataEnded = true;
            if (keep)
                sanitized.Write(chunk);
            offset += count + 12;
        }

        if (!ended)
            throw InvalidImage();
        ValidateScanlines(compressed.ToArray(), (int)width, depth, color, data[28] == 1);
        return (sanitized.ToArray(), (int)width);
    }

    private static void ValidateScanlines(byte[] compressed, int size, int depth, int color, bool interlaced)
    {
        var channels = color switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        // Adam7 pass geometry; each pass is bounded by the already checked IHDR dimensions.
        (int X, int Y, int Dx, int Dy)[] passes = interlaced
            ? [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]
            : [(0, 0, 1, 1)];
        using var source = new MemoryStream(compressed);
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        var row = new byte[1 + size * channels * depth / 8 + 1];
        uint a = 1, b = 0;
        foreach (var (x, y, dx, dy) in passes)
        {
            var width = Math.Max(0, (size - x + dx - 1) / dx);
            var height = Math.Max(0, (size - y + dy - 1) / dy);
            if (width == 0 || height == 0)
                continue;
            var rowLength = 1 + (width * channels * depth + 7) / 8;
            for (var i = 0; i < height; i++)
            {
                var read = zlib.ReadAtLeast(row.AsSpan(0, rowLength), rowLength, throwOnEndOfStream: false);
                if (read != rowLength || row[0] > 4)
                    throw InvalidImage();
                foreach (var value in row.AsSpan(0, rowLength))
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }
            }
        }

        // Reject excess expansion, incomplete streams and corrupt checksums, rather than relying
        // on the native raster decoder's potentially lenient truncated-image behavior.
        if (zlib.ReadByte() != -1 || compressed.Length < 6
            || ReadUInt(compressed.AsSpan(compressed.Length - 4)) != ((b << 16) | a))
            throw InvalidImage();
    }

    private static uint ReadUInt(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32BigEndian(data);

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
        }
        return ~crc;
    }

    private static ValidationAppException InvalidImage() =>
        new("تصویر پروفایل معتبر نیست؛ لطفاً یک تصویر PNG معتبر انتخاب و دوباره برش دهید.");

    private static ValidationAppException TooLarge() =>
        new("حجم تصویر پروفایل نباید بیشتر از ۲۵۶ کیلوبایت باشد.");
}
