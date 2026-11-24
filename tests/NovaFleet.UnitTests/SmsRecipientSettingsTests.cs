using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Notifications;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Notifications;
using NovaFleet.Infrastructure.Sms;
using NovaFleet.UnitTests.Missions;

namespace NovaFleet.UnitTests;

/// <summary>خالی یعنی پیش‌فرض، none یعنی بدون گیرنده، و توکن ناشناخته هرگز نباید بی‌صدا حذف شود.</summary>
public sealed class SmsRecipientSettingsTests : MissionTestBase
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseRecipients_EmptyValueUsesEventDefaults(string? value)
    {
        Assert.Equal(["requester", "driver"], SmsDefaults.ParseRecipients(value, SmsEventType.MissionApproved));
        Assert.Equal(["requester"], SmsDefaults.ParseRecipients(value, SmsEventType.MissionRejected));
    }

    [Fact]
    public void ParseRecipients_NoneMeansNoRecipients()
    {
        Assert.Empty(SmsDefaults.ParseRecipients("none", SmsEventType.MissionApproved));
        Assert.Empty(SmsDefaults.ParseRecipients(" none ", SmsEventType.MissionRejected));
    }

    [Fact]
    public void ParseRecipients_KnownCombinationsAreTrimmedAndDeduplicated()
    {
        Assert.Equal(["requester", "driver"], SmsDefaults.ParseRecipients("requester,driver", SmsEventType.MissionApproved));
        Assert.Equal(["driver", "requester"], SmsDefaults.ParseRecipients(" driver, requester,driver ", SmsEventType.MissionApproved));
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("requester,manager")]
    [InlineData("none,requester")]
    [InlineData("requester,,driver")]
    public void ParseRecipients_UnknownOrConflictingTokensAreValidationErrors(string value)
    {
        Assert.Throws<ValidationAppException>(() => SmsDefaults.ParseRecipients(value, SmsEventType.MissionApproved));
    }

    [Fact]
    public async Task UpdateTemplateAsync_EmptySelectionStoresNoneAndReturnsNoRecipients()
    {
        var service = ServiceFor();
        var saves = 0;
        Db.SavingChanges += (_, _) => saves++;

        var updated = await service.UpdateTemplateAsync(
            SmsEventType.MissionApproved,
            new UpdateSmsTemplateRequest("متن جدید", true, []));

        Assert.Empty(updated.Recipients);
        Assert.Equal("none", await Db.SystemSettings.AsNoTracking()
            .Where(item => item.Key == RecipientsKey(SmsEventType.MissionApproved))
            .Select(item => item.Value)
            .SingleAsync());
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task UpdateTemplateAsync_KnownSelectionStoresCanonicalCommaSeparatedTokens()
    {
        var updated = await ServiceFor().UpdateTemplateAsync(
            SmsEventType.MissionChanged,
            new UpdateSmsTemplateRequest("متن", true, ["driver", "requester", "driver"]));

        Assert.Equal(["driver", "requester"], updated.Recipients);
        Assert.Equal("driver,requester", await Db.SystemSettings.AsNoTracking()
            .Where(item => item.Key == RecipientsKey(SmsEventType.MissionChanged))
            .Select(item => item.Value)
            .SingleAsync());
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("requester,manager")]
    public async Task UpdateTemplateAsync_UnknownSelectionIsRejectedWithoutChangingTemplateOrSettings(string unknown)
    {
        var service = ServiceFor();
        Db.SmsTemplates.Add(new SmsTemplate(SmsEventType.MissionApproved, "قبلی", true));
        await Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateTemplateAsync(
            SmsEventType.MissionApproved,
            new UpdateSmsTemplateRequest("جدید", false, ["requester", unknown])));

        var template = await Db.SmsTemplates.AsNoTracking().SingleAsync();
        Assert.Equal("قبلی", template.Body);
        Assert.True(template.IsActive);
        Assert.False(await Db.SystemSettings.AnyAsync(item => item.Key == RecipientsKey(SmsEventType.MissionApproved)));
    }

    [Fact]
    public async Task ListTemplatesAsync_StoredNoneRemainsEmptyInsteadOfRestoringDefaults()
    {
        Settings.Values[RecipientsKey(SmsEventType.MissionApproved)] = "none";

        var templates = await ServiceFor().ListTemplatesAsync();

        Assert.Empty(templates.Single(item => item.EventType == SmsEventType.MissionApproved).Recipients);
    }

    [Fact]
    public async Task ListTemplatesAsync_StoredUnknownTokenIsRejected()
    {
        Settings.Values[RecipientsKey(SmsEventType.MissionApproved)] = "requester,manager";

        await Assert.ThrowsAsync<ValidationAppException>(() => ServiceFor().ListTemplatesAsync());
    }

    private SmsService ServiceFor() => new(
        Db,
        Settings,
        User(RequesterUsername,
            PermissionCatalog.Notifications.SmsView,
            PermissionCatalog.Notifications.SmsTemplatesManage),
        Audit,
        Clock);

    private static string RecipientsKey(SmsEventType type) =>
        $"{SettingKeys.SmsRecipientsPrefix}{SmsEventNames.Of(type)}";
}
