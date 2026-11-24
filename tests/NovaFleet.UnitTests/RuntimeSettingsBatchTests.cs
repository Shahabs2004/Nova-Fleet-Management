using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Domain.Configuration;
using NovaFleet.Infrastructure.Persistence;
using NovaFleet.Infrastructure.Security;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests;

/// <summary>نوشتن دسته‌ای تنظیمات باید یک ذخیره اتمیک داشته باشد و اسرار پیش از آن محافظت شوند.</summary>
public sealed class RuntimeSettingsBatchTests : MissionTestBase
{
    [Fact]
    public async Task SetBatchAsync_CommitsEveryKeyOnceAndProtectsSecretsBeforeSaving()
    {
        var protector = new RecordingSecretProtector();
        var settings = new RuntimeSettings(Db, Clock, protector);
        var saves = 0;
        string? protectedAtSave = null;
        string? valueAtSave = null;
        var protectionsAtSave = Array.Empty<string>();
        Db.SavingChanges += (_, _) =>
        {
            saves++;
            var secret = Db.ChangeTracker.Entries<SystemSetting>()
                .Single(entry => entry.Entity.Key == "batch.secret");
            protectedAtSave = secret.Entity.ProtectedValue;
            valueAtSave = secret.Entity.Value;
            protectionsAtSave = protector.ProtectedPlaintexts.ToArray();
        };

        await settings.SetBatchAsync(
        [
            new SettingWrite("batch.first", "one", false),
            new SettingWrite("batch.second", "two", false),
            new SettingWrite("batch.secret", "  batch-secret  ", true),
        ]);

        Assert.Equal(1, saves);
        Assert.Equal(["batch-secret"], protectionsAtSave);
        Assert.Equal("YmF0Y2gtc2VjcmV0", protectedAtSave);
        Assert.Equal(string.Empty, valueAtSave);
        Assert.Equal("one", await settings.GetStringAsync("batch.first"));
        Assert.Equal("two", await settings.GetStringAsync("batch.second"));
        Assert.Equal("batch-secret", await settings.GetSecretAsync("batch.secret"));
    }

    [Fact]
    public async Task SetBatchAsync_NullKeepsSecretsAndStoresEmptyForPlainKeys()
    {
        var settings = new RuntimeSettings(Db, Clock, new RecordingSecretProtector());
        await settings.SetAsync("batch.plain", "before");
        await settings.SetSecretAsync("batch.secret", "before-secret");

        await settings.SetBatchAsync(
        [
            new SettingWrite("batch.plain", null, false),
            new SettingWrite("batch.secret", null, true),
            new SettingWrite("batch.new-plain", null, false),
            new SettingWrite("batch.new-secret", null, true),
        ]);

        Assert.Equal(string.Empty, await settings.GetStringAsync("batch.plain"));
        Assert.Equal("before-secret", await settings.GetSecretAsync("batch.secret"));
        Assert.Equal(string.Empty, await settings.GetStringAsync("batch.new-plain"));
        Assert.False(await Db.SystemSettings.AnyAsync(item => item.Key == "batch.new-secret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task SetBatchAsync_BlankSecretStoresCanonicalNonSecretClear(string value)
    {
        var settings = new RuntimeSettings(Db, Clock, new RecordingSecretProtector());
        await settings.SetSecretAsync("batch.secret", "before-secret");

        await settings.SetBatchAsync([new SettingWrite("batch.secret", value, true)]);

        var stored = await Db.SystemSettings.AsNoTracking().SingleAsync(item => item.Key == "batch.secret");
        Assert.False(stored.IsSecret);
        Assert.Equal(string.Empty, stored.Value);
        Assert.Null(stored.ProtectedValue);
        Assert.False(await settings.HasSecretAsync("batch.secret"));
        Assert.Null(await settings.GetSecretAsync("batch.secret"));
        Assert.Equal(string.Empty, await settings.GetStringAsync("batch.secret"));
        Assert.Equal(string.Empty, (await settings.ReadAllAsync())["batch.secret"]);
    }

    [Fact]
    public async Task SetBatchAsync_ForcedSaveFailurePreservesEveryPriorValue()
    {
        var protector = new RecordingSecretProtector();
        var settings = new RuntimeSettings(Db, Clock, protector);
        await settings.SetAsync("batch.first", "old-first");
        await settings.SetAsync("batch.second", "old-second");
        await settings.SetSecretAsync("batch.secret", "old-secret");
        Db.SavingChanges += (_, _) => throw new InvalidOperationException("forced save failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.SetBatchAsync(
        [
            new SettingWrite("batch.first", "new-first", false),
            new SettingWrite("batch.second", "new-second", false),
            new SettingWrite("batch.secret", "new-secret", true),
            new SettingWrite("batch.new", "new-value", false),
        ]));

        await using var verification = new NovaFleetDbContext(Options);
        var stored = await verification.SystemSettings.AsNoTracking().ToDictionaryAsync(item => item.Key);
        Assert.Equal("old-first", stored["batch.first"].Value);
        Assert.Equal("old-second", stored["batch.second"].Value);
        Assert.Equal("old-secret", await new RuntimeSettings(verification, Clock, protector).GetSecretAsync("batch.secret"));
        Assert.False(stored.ContainsKey("batch.new"));
    }

    [Fact]
    public async Task SetBatchAsync_OrdinaryReadsNeverReturnSecretPlaintext()
    {
        var settings = new RuntimeSettings(Db, Clock, new RecordingSecretProtector());

        await settings.SetBatchAsync([new SettingWrite("batch.secret", "never-return-this", true)]);

        var all = await settings.ReadAllAsync();
        Assert.False(all.ContainsKey("batch.secret"));
        Assert.Null(await settings.GetStringAsync("batch.secret"));
        Assert.Equal(17, await settings.GetIntAsync("batch.secret", 17));
        Assert.True(await settings.GetBoolAsync("batch.secret", true));
        var stored = await Db.SystemSettings.AsNoTracking().SingleAsync(item => item.Key == "batch.secret");
        Assert.True(stored.IsSecret);
        Assert.Equal(string.Empty, stored.Value);
        Assert.DoesNotContain("never-return-this", stored.ProtectedValue);
    }

    private sealed class RecordingSecretProtector : ISecretProtector
    {
        private readonly FakeSecretProtector inner = new();
        public List<string> ProtectedPlaintexts { get; } = [];

        public string Protect(string plaintext)
        {
            ProtectedPlaintexts.Add(plaintext);
            return inner.Protect(plaintext);
        }

        public string? Unprotect(string? protectedValue) => inner.Unprotect(protectedValue);
    }
}
