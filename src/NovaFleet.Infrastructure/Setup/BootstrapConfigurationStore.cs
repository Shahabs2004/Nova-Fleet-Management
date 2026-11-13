using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaFleet.Infrastructure.Setup;

/// <summary>
/// فایل بوت‌استرپ کنار مخزن و در پروفایل کاربر سرویس. تنها جایی است که انتخاب دیتابیس
/// پیش از برقراری اولین اتصال ذخیره می‌شود؛ اولویت خواندن: متغیر محیطی ← این فایل ← appsettings.
/// </summary>
public interface IBootstrapConfigurationStore
{
    string FilePath { get; }

    string? ReadConnectionString();

    void WriteConnectionString(string connectionString);
}

public sealed class JsonBootstrapConfigurationStore : IBootstrapConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".novafleet",
        "bootstrap.json");

    public string? ReadConnectionString()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        using var stream = File.OpenRead(FilePath);
        var document = JsonSerializer.Deserialize<BootstrapFile>(stream, SerializerOptions);
        return string.IsNullOrWhiteSpace(document?.ConnectionString) ? null : document.ConnectionString.Trim();
    }

    public void WriteConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("مسیر فایل بوت‌استرپ قابل‌استخراج نیست.");
        System.IO.Directory.CreateDirectory(directory);

        var payload = JsonSerializer.Serialize(new BootstrapFile(connectionString.Trim()), SerializerOptions);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, FilePath, overwrite: true);
    }

    private sealed record BootstrapFile(string ConnectionString);
}
