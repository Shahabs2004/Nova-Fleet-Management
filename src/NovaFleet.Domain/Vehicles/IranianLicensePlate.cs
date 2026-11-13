namespace NovaFleet.Domain.Vehicles;

/// <summary>
/// پلاک ملی ایران با چهار بخش؛ ذخیره ساختاریافته برای جست‌وجو و نمایش گرافیکی.
/// حرف پلاک می‌تواند چندنویسه باشد («الف» دولتی)، بنابراین اعتبارسنجی بر پایه توکن است نه نویسه.
/// </summary>
public sealed record IranianLicensePlate(string FirstTwo, string Letter, string NextThree, string IranCode)
{
    private const char PersianZero = (char)0x06F0;
    private const char PersianNine = (char)0x06F9;
    private const char ArabicZero = (char)0x0660;
    private const char ArabicNine = (char)0x0669;
    private const char ZeroWidthNonJoiner = (char)0x200C;

    private static readonly HashSet<string> ValidLetters = new(StringComparer.Ordinal)
    {
        // سواری شخصی
        "ب", "ج", "د", "س", "ص", "ط", "ق", "ل", "م", "ن", "و", "ه", "ی",
        // ادارات و نهادهای دولتی
        "الف",
        // پلیس، تاکسی، سپاه، وزارت دفاع، معلولین، ارتش، عمومی، ستاد کل، کشاورزی، گذرموقت
        "پ", "ت", "ث", "ز", "ژ", "ش", "ع", "ف", "ک", "گ",
        // دیپلمات و سرویس سفارتخانه
        "D", "S"
    };

    public static IranianLicensePlate Create(string firstTwo, string letter, string nextThree, string iranCode)
    {
        var first = NormalizeDigits(firstTwo);
        var next = NormalizeDigits(nextThree);
        var iran = NormalizeDigits(iranCode);
        var normalizedLetter = NormalizeLetter(letter);

        if (first.Length != 2 || !first.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("دو رقم اول پلاک باید عدد و دو رقمی باشد.", nameof(firstTwo));
        }

        if (next.Length != 3 || !next.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("سه رقم پلاک باید عدد و سه رقمی باشد.", nameof(nextThree));
        }

        if (iran.Length != 2 || !iran.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("کد ایران باید عدد و دو رقمی باشد.", nameof(iranCode));
        }

        if (!ValidLetters.Contains(normalizedLetter))
        {
            throw new ArgumentException("حرف پلاک معتبر نیست.", nameof(letter));
        }

        return new IranianLicensePlate(first, normalizedLetter, next, iran);
    }

    public static IranianLicensePlate Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '_').ToArray());
        if (compact.Length < 8)
        {
            throw new ArgumentException("پلاک باید شامل دو رقم، یک حرف، سه رقم و کد دو رقمی باشد.", nameof(value));
        }

        // طول حرف متغیر است (تک‌نویسه یا «الف»)؛ ارقام از دو سر و حرف از میان برداشته می‌شود.
        var first = compact[..2];
        var iran = compact[^2..];
        var next = compact[^5..^2];
        var letter = compact[2..^5];

        return Create(first, letter, next, iran);
    }

    public string Format() => $"{FirstTwo} {Letter} {NextThree} - {IranCode}";

    public override string ToString() => Format();

    private static string NormalizeDigits(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character is >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else if (character is >= PersianZero and <= PersianNine)
            {
                builder.Append((char)('0' + (character - PersianZero)));
            }
            else if (character is >= ArabicZero and <= ArabicNine)
            {
                builder.Append((char)('0' + (character - ArabicZero)));
            }
        }

        return builder.ToString();
    }

    private static string NormalizeLetter(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var token = value.Trim().Replace(ZeroWidthNonJoiner.ToString(), string.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(token, nameof(value));
        return token switch
        {
            "ي" or "ى" => "ی",
            "ك" => "ک",
            "ا" => "الف",
            "d" => "D",
            "s" => "S",
            _ => token
        };
    }
}