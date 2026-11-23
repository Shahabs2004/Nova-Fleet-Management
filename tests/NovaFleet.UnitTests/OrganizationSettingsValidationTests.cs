using System.Text;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;
using NovaFleet.Domain.Configuration;
using NovaFleet.Infrastructure.Settings;
using NovaFleet.UnitTests.Missions;
using NovaFleet.UnitTests.Security;
using QuestPDF.Infrastructure;

namespace NovaFleet.UnitTests;

public sealed class OrganizationSettingsValidationTests : MissionTestBase
{
    public OrganizationSettingsValidationTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public async Task UpdateOrganization_NormalizesLogoBeforeReturningAndPersisting()
    {
        var original = PngFixture.Create(metadata: true);
        var service = Service();

        var updated = await service.UpdateOrganizationAsync(new("  سازمان آزمون  ", original));

        Assert.Equal("سازمان آزمون", updated.Name);
        Assert.NotNull(updated.LogoDataUrl);
        Assert.NotEqual(original, updated.LogoDataUrl);
        Assert.StartsWith(PngFixture.Prefix, updated.LogoDataUrl);
        Assert.DoesNotContain("private-metadata", Decode(updated.LogoDataUrl));
        Assert.Equal(updated.Name, await Runtime.GetStringAsync(SettingKeys.OrganizationName));
        Assert.Equal(updated.LogoDataUrl, await Runtime.GetStringAsync(SettingKeys.OrganizationLogo));
        Assert.DoesNotContain("data:image", System.Text.Json.JsonSerializer.Serialize(Audit.Records));
    }

    [Fact]
    public async Task UpdateOrganization_NullLogoClearsStoredValue()
    {
        var service = Service();
        await service.UpdateOrganizationAsync(new("سازمان آزمون", PngFixture.Create()));

        var cleared = await service.UpdateOrganizationAsync(new("سازمان آزمون", null));

        Assert.Null(cleared.LogoDataUrl);
        Assert.Equal(string.Empty, await Runtime.GetStringAsync(SettingKeys.OrganizationLogo));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("url")]
    [InlineData("svg")]
    [InlineData("jpeg")]
    [InlineData("base64")]
    [InlineData("encodedOversize")]
    [InlineData("decodedOversize")]
    [InlineData("nonsquare")]
    [InlineData("dimensions")]
    [InlineData("dimensionBomb")]
    [InlineData("truncated")]
    [InlineData("crc")]
    [InlineData("inflationBomb")]
    [InlineData("filter")]
    [InlineData("truncatedDeflate")]
    [InlineData("animated")]
    [InlineData("idatTrailingData")]
    [InlineData("trailingData")]
    public async Task UpdateOrganization_InvalidLogoLeavesNameLogoAndAuditUntouched(string kind)
    {
        var service = Service();
        var original = PngFixture.Create(metadata: true);
        await service.UpdateOrganizationAsync(new("سازمان قبلی", original));
        var storedLogo = await Runtime.GetStringAsync(SettingKeys.OrganizationLogo);
        var audits = Audit.Records.Count;

        var error = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.UpdateOrganizationAsync(new("سازمان تازه", PngFixture.Invalid(kind))));

        Assert.Contains("لوگو", error.Message);
        Assert.Equal("سازمان قبلی", await Runtime.GetStringAsync(SettingKeys.OrganizationName));
        Assert.Equal(storedLogo, await Runtime.GetStringAsync(SettingKeys.OrganizationLogo));
        Assert.Equal(audits, Audit.Records.Count);
        Assert.False(Db.ChangeTracker.HasChanges());
    }

    private SettingsService Service() => new(
        Db,
        User(RequesterUsername, PermissionCatalog.Administration.SystemManage),
        Audit,
        Runtime);

    private static string Decode(string dataUrl) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[PngFixture.Prefix.Length..]));
}
