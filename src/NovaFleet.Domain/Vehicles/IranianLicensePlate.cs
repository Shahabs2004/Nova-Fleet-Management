using System.Globalization;

namespace NovaFleet.Domain.Vehicles;

/// <summary>
/// پلاک ملی ایران با چهار بخش؛ ذخیره ساختاریافته برای جست‌وجو و نمایش گرافیکی.
/// </summary>
public sealed record IranianLicensePlate(string FirstTwo, string Letter, string NextThree, string IranCode)
{
    private static readonly HashSet<char> ValidLetters =
    [
        'ب', 'ج', 'د', 'س', 'ص', 'ط', 'ع', 'ق', 'ل', 'م', 'ن', 'و', 'ه', 'ی'
    ];

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

        return new IranianLicensePlate(first, normalizedLetter.ToString(), next, iran);
    }

    public static IranianLicensePlate Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '_').ToArray());
        compact = NormalizeDigits(compact);
        if (compact.Length != 8)
        {
            throw new ArgumentException("پلاک باید شامل دو رقم، یک حرف، سه رقم و کد دو رقمی باشد.", nameof(value));
        }

        return Create(compact[..2], compact[2].ToString(), compact[3..6], compact[6..]);
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
            else if (character is >= '\u06F0' and <= '\u06F9')
            {
                builder.Append((char)('0' + (character - '\u06F0')));
            }
            else if (character is >= '\u0660' and <= '\u0669')
            {
                builder.Append((char)('0' + (character - '\u0660')));
            }
        }

        return builder.ToString();
    }

    private static char NormalizeLetter(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        var character = value.Trim()[0];
        return character switch
        {
            'ي' or 'ى' => 'ی',
            'ك' => 'ب',
            _ => character
        };
    }
}
