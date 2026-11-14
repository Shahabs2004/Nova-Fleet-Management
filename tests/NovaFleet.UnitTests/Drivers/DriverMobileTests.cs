using NovaFleet.Domain.Drivers;
using Xunit;

namespace NovaFleet.UnitTests.Drivers;

/// <summary>موبایل راننده باید ارقام فارسی/عربی را بپذیرد و همیشه لاتین ذخیره کند.</summary>
public sealed class DriverMobileTests
{
    [Theory]
    [InlineData("09123456789", "09123456789")]
    [InlineData("۰۹۱۲۳۴۵۶۷۸۹", "09123456789")]
    [InlineData("٠٩١٢٣٤٥٦٧٨٩", "09123456789")]
    [InlineData("۰۹۱۲-۳۴۵-۶۷۸۹", "09123456789")]
    [InlineData("00989123456789", "09123456789")]
    [InlineData("+98 912 345 6789", "09123456789")]
    [InlineData("989123456789", "09123456789")]
    public void Mobile_is_stored_as_ascii_eleven_digits(string input, string expected)
    {
        var driver = new Driver("علی", "اکبری", input);
        Assert.Equal(expected, driver.MobilePhone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("9123456789")]
    [InlineData("091234567890")]
    [InlineData("۰۹۱۲")]
    [InlineData("19123456789")]
    public void Invalid_mobile_is_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() => new Driver("علی", "اکبری", input));
    }

    [Fact]
    public void Update_profile_normalizes_persian_digits_too()
    {
        var driver = new Driver("علی", "اکبری", "09123456789");
        driver.UpdateProfile("علی", "اکبری", "۰۹۳۵۱۱۱۲۲۳۳");
        Assert.Equal("09351112233", driver.MobilePhone);
    }
}