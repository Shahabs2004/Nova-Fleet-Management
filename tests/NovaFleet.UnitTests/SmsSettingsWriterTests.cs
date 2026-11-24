using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests;

/// <summary>اعتبارسنجی تنظیمات پیامک باید روی ترکیب فعلی/ارسالی انجام و همه کلیدها یک‌جا ذخیره شوند.</summary>
public sealed class SmsSettingsWriterTests : MissionTestBase
{
    [Theory]
    [InlineData("Wsdl", null, "organization", "svc.user", "1000999", "secret")]
    [InlineData("Wsdl", "https://sms.example/soap", null, "svc.user", "1000999", "secret")]
    [InlineData("Api", "https://sms.example/rest", "organization", null, "1000999", "secret")]
    [InlineData("Api", "https://sms.example/rest", "organization", "svc.user", null, "secret")]
    [InlineData("Wsdl", "https://sms.example/soap", "organization", "svc.user", "1000999", null)]
    public async Task WriteSmsAsync_EnabledRealGatewayRejectsMissingPartsWithoutWriting(
        string mode,
        string? endpoint,
        string? organization,
        string? username,
        string? senderNumber,
        string? password)
    {
        await Runtime.SetAsync(SettingKeys.SmsEnabled, bool.FalseString);
        var settings = new RecordingRuntimeSettings(Runtime);
        var request = new UpdateSmsConnectionSettingsDto(
            true, mode, endpoint, organization, username, senderNumber, 20, 5, 5, password);

        await Assert.ThrowsAsync<ValidationAppException>(() => SettingsWriters.WriteSmsAsync(settings, request));

        Assert.False(await Runtime.GetBoolAsync(SettingKeys.SmsEnabled, true));
        Assert.Empty(settings.Batches);
        Assert.Empty(settings.IndividualWrites);
    }

    [Fact]
    public async Task WriteSmsAsync_InvalidEndpointDoesNotEnableOrPartiallySave()
    {
        await Runtime.SetAsync(SettingKeys.SmsEnabled, bool.FalseString);
        await Runtime.SetAsync(SettingKeys.SmsMode, "Simulation");
        var settings = new RecordingRuntimeSettings(Runtime);

        await Assert.ThrowsAsync<ValidationAppException>(() => SettingsWriters.WriteSmsAsync(settings, Request(
            endpoint: "sms.example/soap")));

        Assert.False(await Runtime.GetBoolAsync(SettingKeys.SmsEnabled, true));
        Assert.Equal("Simulation", await Runtime.GetStringAsync(SettingKeys.SmsMode));
        Assert.Empty(settings.Batches);
        Assert.Empty(settings.IndividualWrites);
    }

    [Fact]
    public async Task WriteSmsAsync_OmittedPasswordUsesStoredSecretAndCommitsOneOrderedBatch()
    {
        await Runtime.SetAsync(SettingKeys.SmsEnabled, bool.FalseString);
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "stored-secret");
        var settings = new RecordingRuntimeSettings(Runtime);

        var mode = await SettingsWriters.WriteSmsAsync(settings, Request(password: null));

        Assert.Equal("Wsdl", mode);
        var batch = Assert.Single(settings.Batches);
        Assert.Equal(
            [
                SettingKeys.SmsMode,
                SettingKeys.SmsEndpoint,
                SettingKeys.SmsOrganization,
                SettingKeys.SmsUsername,
                SettingKeys.SmsSenderNumber,
                SettingKeys.SmsTimeoutSeconds,
                SettingKeys.SmsRetryMinutes,
                SettingKeys.SmsMaxAttempts,
                SettingKeys.SmsEnabled,
            ],
            batch.Select(write => write.Key));
        Assert.DoesNotContain(batch, write => write.Key == SettingKeys.SmsPassword);
        Assert.Empty(settings.IndividualWrites);
        Assert.True(await Runtime.GetBoolAsync(SettingKeys.SmsEnabled, false));
        Assert.Equal("https://sms.example/soap", await Runtime.GetStringAsync(SettingKeys.SmsEndpoint));
        Assert.Equal("stored-secret", await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
    }

    [Fact]
    public async Task WriteSmsAsync_NewPasswordIsProtectedBeforeEnabledInTheSameBatch()
    {
        await Runtime.SetAsync(SettingKeys.SmsEnabled, bool.FalseString);
        var settings = new RecordingRuntimeSettings(Runtime);

        await SettingsWriters.WriteSmsAsync(settings, Request(password: "  new-secret  "));

        var batch = Assert.Single(settings.Batches).ToList();
        var passwordIndex = batch.FindIndex(write => write.Key == SettingKeys.SmsPassword);
        var enabledIndex = batch.FindIndex(write => write.Key == SettingKeys.SmsEnabled);
        Assert.True(passwordIndex >= 0);
        Assert.True(enabledIndex > passwordIndex);
        Assert.True(batch[passwordIndex].IsSecret);
        Assert.Equal("new-secret", batch[passwordIndex].Value);
        Assert.Empty(settings.IndividualWrites);
        Assert.True(await Runtime.GetBoolAsync(SettingKeys.SmsEnabled, false));
        Assert.Equal("new-secret", await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
        Assert.Null(await Runtime.GetStringAsync(SettingKeys.SmsPassword));
    }

    [Fact]
    public async Task WriteSmsAsync_BlankPasswordWhileDisabledExplicitlyClearsStoredSecret()
    {
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "stored-secret");
        var settings = new RecordingRuntimeSettings(Runtime);

        await SettingsWriters.WriteSmsAsync(settings, Request(enabled: false, mode: "Simulation", password: "   "));

        var batch = Assert.Single(settings.Batches);
        var passwordWrite = batch.Single(write => write.Key == SettingKeys.SmsPassword);
        Assert.False(passwordWrite.IsSecret);
        Assert.Equal(string.Empty, passwordWrite.Value);
        Assert.Empty(settings.IndividualWrites);
        Assert.False(await Runtime.HasSecretAsync(SettingKeys.SmsPassword));
        Assert.Equal(string.Empty, await Runtime.GetStringAsync(SettingKeys.SmsPassword));
    }

    [Fact]
    public async Task WriteSmsAsync_BlankPasswordWhileEnablingFailsAndPreservesPriorState()
    {
        await Runtime.SetAsync(SettingKeys.SmsEnabled, bool.FalseString);
        await Runtime.SetSecretAsync(SettingKeys.SmsPassword, "stored-secret");
        var settings = new RecordingRuntimeSettings(Runtime);

        await Assert.ThrowsAsync<ValidationAppException>(() => SettingsWriters.WriteSmsAsync(
            settings, Request(password: "   ")));

        Assert.False(await Runtime.GetBoolAsync(SettingKeys.SmsEnabled, true));
        Assert.Equal("stored-secret", await Runtime.GetSecretAsync(SettingKeys.SmsPassword));
        Assert.Empty(settings.Batches);
        Assert.Empty(settings.IndividualWrites);
    }

    private static UpdateSmsConnectionSettingsDto Request(
        bool enabled = true,
        string mode = "Wsdl",
        string? endpoint = "https://sms.example/soap",
        string? organization = "organization",
        string? username = "svc.user",
        string? senderNumber = "1000999",
        string? password = "secret") =>
        new(enabled, mode, endpoint, organization, username, senderNumber, 20, 5, 5, password);

    private sealed class RecordingRuntimeSettings(IRuntimeSettings inner) : IRuntimeSettings
    {
        public List<IReadOnlyList<SettingWrite>> Batches { get; } = [];
        public List<string> IndividualWrites { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            inner.ReadAllAsync(cancellationToken);

        public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
            inner.GetStringAsync(key, cancellationToken);

        public Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default) =>
            inner.GetIntAsync(key, fallback, cancellationToken);

        public Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default) =>
            inner.GetBoolAsync(key, fallback, cancellationToken);

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            inner.GetSecretAsync(key, cancellationToken);

        public Task<bool> HasSecretAsync(string key, CancellationToken cancellationToken = default) =>
            inner.HasSecretAsync(key, cancellationToken);

        public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            IndividualWrites.Add(key);
            return inner.SetAsync(key, value, cancellationToken);
        }

        public Task SetBatchAsync(IReadOnlyList<SettingWrite> writes, CancellationToken cancellationToken = default)
        {
            Batches.Add(writes.ToList());
            return inner.SetBatchAsync(writes, cancellationToken);
        }

        public Task SetSecretAsync(string key, string? plaintext, CancellationToken cancellationToken = default)
        {
            IndividualWrites.Add(key);
            return inner.SetSecretAsync(key, plaintext, cancellationToken);
        }

        public Task ClearSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            IndividualWrites.Add(key);
            return inner.ClearSecretAsync(key, cancellationToken);
        }
    }
}
