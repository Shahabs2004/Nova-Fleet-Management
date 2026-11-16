using NovaFleet.Infrastructure.Directory;

namespace NovaFleet.UnitTests.Directory;

/// <summary>
/// دامنه bind ساده با نام خالی را Error 49 رد می‌کند؛ حساب Bind باید خودکار به UPN تبدیل شود
/// و هیچ فرمت از پیش معتبری (UPN، down-level، DN) دست‌نخورده نماند، نه اینکه خراب شود.
/// </summary>
public class BindDnNormalizationTests
{
    [Theory]
    [InlineData("sh-sadeghi", "Frs.Local", "sh-sadeghi@Frs.Local")]
    [InlineData("sh-sadeghi", "FRS.LOCAL", "sh-sadeghi@FRS.LOCAL")]
    [InlineData("sh-sadeghi@Frs.Local", "Frs.Local", "sh-sadeghi@Frs.Local")]
    [InlineData("FRS\\sh-sadeghi", "Frs.Local", "FRS\\sh-sadeghi")]
    [InlineData("CN=Shahab Sadeghi,OU=Users,DC=Frs,DC=Local", "Frs.Local", "CN=Shahab Sadeghi,OU=Users,DC=Frs,DC=Local")]
    [InlineData("sh-sadeghi", "", "sh-sadeghi")]
    [InlineData("", "Frs.Local", "")]
    public void SimpleNamesBecomeUpnAndOtherFormatsArePreserved(string bindDn, string domain, string expected) =>
        Assert.Equal(expected, LdapDirectoryAuthenticator.NormalizeBindDn(bindDn, domain));
}
