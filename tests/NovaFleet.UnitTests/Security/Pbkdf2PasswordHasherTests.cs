using System.Security.Cryptography;
using NovaFleet.Infrastructure.Security;

namespace NovaFleet.UnitTests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    private const string Password = "Strong#Password1";

    [Fact]
    public void Hash_UsesCurrentWorkFactorAndVerifies()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var hash = hasher.Hash(Password);

        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("210000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.True(hasher.Verify(hash, Password));
        Assert.False(hasher.Verify(hash, "wrong"));
    }

    [Fact]
    public void Verify_AcceptsStrongerFutureWorkFactor()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 210_001, HashAlgorithmName.SHA256, 32);
        var hash = $"pbkdf2-sha256$210001${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

        Assert.True(new Pbkdf2PasswordHasher().Verify(hash, Password));
    }

    [Fact]
    public void Verify_RejectsHashBelowMinimumWorkFactor()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 1, HashAlgorithmName.SHA256, 32);
        var hash = $"pbkdf2-sha256$1${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

        Assert.False(new Pbkdf2PasswordHasher().Verify(hash, Password));
    }

    [Theory]
    [InlineData("pbkdf2-sha256$210000$not-base64$also-not-base64")]
    [InlineData("pbkdf2-sha256$0$AA==$AA==")]
    [InlineData("pbkdf2-sha256$210000$AA==$AA==$extra")]
    [InlineData("pbkdf2-sha256$210000$$AA==")]
    [InlineData("pbkdf2-sha256$210000$AA==$")]
    public void Verify_RejectsMalformedOrUnsupportedHash(string hash) =>
        Assert.False(new Pbkdf2PasswordHasher().Verify(hash, Password));
}
