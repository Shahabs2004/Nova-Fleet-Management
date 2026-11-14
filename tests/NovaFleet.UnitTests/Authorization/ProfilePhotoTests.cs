using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Drivers;
using NovaFleet.Application.Identity;
using NovaFleet.Domain.Identity;
using NovaFleet.Infrastructure.Drivers;
using NovaFleet.Infrastructure.Identity;
using NovaFleet.Infrastructure.Security;
using NovaFleet.UnitTests.Missions;
using QuestPDF.Infrastructure;

namespace NovaFleet.UnitTests.Authorization;

public sealed class ProfilePhotoTests : MissionTestBase
{
    private const string Prefix = "data:image/png;base64,";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProfilePhotoTests() => QuestPDF.Settings.License = LicenseType.Community;

    private UserService Users(ICurrentUser? caller = null) => new(
        Db, new TestHasher(), caller ?? User(RequesterUsername, PermissionCatalog.Administration.UsersManage), Audit);

    private DriverService Drivers(ICurrentUser? caller = null) => new(
        Db, caller ?? User(RequesterUsername, PermissionCatalog.Drivers.Manage, PermissionCatalog.Drivers.View), Audit);

    private AuthService Auth() => new(Db, new TestHasher(), new TestDirectory(), new PermissionResolver(Db), Clock);

    private static CreateLocalUserRequest CreateUser(string? avatar) =>
        new("photo.user", "password123", "مریم", "رضایی", "09121112233", "user@example.test", "واحد اول", false, [], avatar);

    private static UpdateUserRequest UpdateUser(string? avatar) =>
        new("ویرایش", "کاربر", "09121112233", "updated@example.test", "واحد دوم", [], avatar);

    [Fact]
    public async Task User_CreateUpdateReadAndRemove_SaveProfileAndPhotoTogether()
    {
        var saves = 0;
        Db.SavingChanges += (_, _) => saves++;
        var service = Users();
        var created = await service.CreateLocalAsync(CreateUser(Png()));
        Assert.Equal(1, saves);
        Assert.NotNull(created.AvatarDataUrl);
        Assert.Equal(created.AvatarDataUrl, (await service.ListAsync(null, false)).Single(x => x.Id == created.Id).AvatarDataUrl);
        Assert.Equal(created.AvatarDataUrl, (await service.GetAsync(created.Id)).AvatarDataUrl);
        Assert.Equal(created.AvatarDataUrl, (await Auth().LoginLocalAsync(
            new LoginRequest("photo.user", "password123"), new SessionContext(Guid.NewGuid().ToString(), null))).AvatarDataUrl);

        saves = 0;
        var updated = await service.UpdateAsync(created.Id, UpdateUser(Png(size: 32)));
        Assert.Equal(1, saves);
        Assert.Equal("ویرایش", updated.FirstName);
        Assert.NotEqual(created.AvatarDataUrl, updated.AvatarDataUrl);
        Db.ChangeTracker.Clear();
        var stored = await Db.Users.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(updated.AvatarDataUrl, stored.AvatarDataUrl);
        Assert.Equal(updated.FirstName, stored.FirstName);
        Assert.Equal(updated.AvatarDataUrl, (await Auth().GetCurrentAsync("photo.user"))!.AvatarDataUrl);

        var cleared = await service.UpdateAsync(created.Id, UpdateUser(null));
        Assert.Null(cleared.AvatarDataUrl);
        Assert.Null((await service.ListAsync(null, false)).Single(x => x.Id == created.Id).AvatarDataUrl);
        Assert.Null((await Auth().GetCurrentAsync("photo.user"))!.AvatarDataUrl);
        AssertNoPhotoInAudit();
    }

    [Fact]
    public async Task Driver_CreateUpdateReadOptionsAndRemove_SaveTogether()
    {
        var saves = 0;
        Db.SavingChanges += (_, _) => saves++;
        var service = Drivers();
        var created = await service.CreateAsync(new DriverCreateRequest("رضا", "محمدی", "09121112233", Png()));
        Assert.Equal(1, saves);
        Assert.NotNull(created.AvatarDataUrl);
        Assert.Equal(created.AvatarDataUrl, (await service.ListOptionsAsync(false)).Single().AvatarDataUrl);
        Assert.Equal(created.AvatarDataUrl, (await service.SearchAsync(null, false, 1, 20)).Items.Single().AvatarDataUrl);
        Assert.Equal(created.AvatarDataUrl, (await service.GetAsync(created.Id)).AvatarDataUrl);

        saves = 0;
        var updated = await service.UpdateAsync(created.Id,
            new DriverUpdateRequest("ویرایش", "راننده", "09121112233", created.RowVersion, Png(size: 32)));
        Assert.Equal(1, saves);
        Assert.NotEqual(created.AvatarDataUrl, updated.AvatarDataUrl);
        Db.ChangeTracker.Clear();
        var stored = await Db.Drivers.AsNoTracking().SingleAsync();
        Assert.Equal(updated.AvatarDataUrl, stored.AvatarDataUrl);
        Assert.Equal(updated.FirstName, stored.FirstName);
        var cleared = await service.UpdateAsync(created.Id,
            new DriverUpdateRequest(updated.FirstName, updated.LastName, updated.MobilePhone, updated.RowVersion, null));
        Assert.Null(cleared.AvatarDataUrl);
        Assert.Null((await service.ListOptionsAsync(false)).Single().AvatarDataUrl);
        AssertNoPhotoInAudit();
    }

    [Fact]
    public async Task SelfPhoto_RequiresNoAdminPermission_ChangesOnlyOwnPhoto()
    {
        var own = await Db.Users.SingleAsync(x => x.Username == RequesterUsername);
        var role = new Role("PhotoRole", "نقش آزمون");
        Db.Roles.Add(role);
        Db.UserRoles.Add(new UserRole(own.Id, role.Id));
        Db.UserPermissions.Add(new UserPermission(own.Id, PermissionCatalog.Drivers.View, PermissionEffect.Grant));
        await Db.SaveChangesAsync();
        var service = Users(User(RequesterUsername));
        var before = await service.GetProfileAsync();
        var saved = await service.UpdateProfilePhotoAsync(new(Png()));
        Assert.Equal(before, saved with { AvatarDataUrl = null });
        Assert.NotNull(saved.AvatarDataUrl);
        Assert.Equal(saved, await service.GetProfileAsync());
        Assert.Equal(saved.AvatarDataUrl, (await new PermissionResolver(Db).ResolveAsync(RequesterUsername))!.AvatarDataUrl);
        Assert.Equal(saved.AvatarDataUrl, (await Auth().GetCurrentAsync(RequesterUsername))!.AvatarDataUrl);
        Assert.Equal(role.Id, (await Db.UserRoles.SingleAsync(x => x.UserId == own.Id)).RoleId);
        Assert.Equal(PermissionCatalog.Drivers.View, (await Db.UserPermissions.SingleAsync(x => x.UserId == own.Id)).Permission);
        Assert.False(own.IsLocalAdmin);
        Assert.Null((await Db.Users.AsNoTracking().SingleAsync(x => x.Username == OtherUsername)).AvatarDataUrl);
        Assert.Null((await service.UpdateProfilePhotoAsync(new(null))).AvatarDataUrl);
        AssertNoPhotoInAudit();
    }

    [Theory]
    [InlineData("username")]
    [InlineData("id")]
    [InlineData("firstName")]
    [InlineData("organizationalUnit")]
    [InlineData("roleIds")]
    [InlineData("isLocalAdmin")]
    public void SelfPhotoRequest_RejectsOverposting(string property)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateProfilePhotoRequest>(
            "{\"avatarDataUrl\":null,\"" + property + "\":null}", JsonOptions));
        Assert.Single(typeof(UpdateProfilePhotoRequest).GetProperties());
    }

    [Fact]
    public async Task Profile_RejectsAnonymousMissingAndDisabledAccounts()
    {
        foreach (var caller in new ICurrentUser[] { AnonymousCurrentUser.Instance, User("missing.user") })
        {
            await Assert.ThrowsAsync<UnauthorizedAppException>(() => Users(caller).GetProfileAsync());
            await Assert.ThrowsAsync<UnauthorizedAppException>(() => Users(caller).UpdateProfilePhotoAsync(new(Png())));
        }
        var own = await Db.Users.SingleAsync(x => x.Username == RequesterUsername);
        own.Disable();
        await Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAppException>(() => Users(User(RequesterUsername)).GetProfileAsync());
        await Assert.ThrowsAsync<UnauthorizedAppException>(() => Users(User(RequesterUsername)).UpdateProfilePhotoAsync(new(Png())));
        Assert.Null(own.AvatarDataUrl);
    }

    [Fact]
    public async Task AdminAndDriverWrites_KeepExistingPermissions_AndReadsKeepTheirGuards()
    {
        var user = await Db.Users.SingleAsync(x => x.Username == OtherUsername);
        var driver = NewDriver();
        await Db.SaveChangesAsync();
        var operatorUser = User(RequesterUsername, PermissionCatalog.Drivers.View);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Users(operatorUser).CreateLocalAsync(CreateUser(Png())));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Users(operatorUser).UpdateAsync(user.Id, UpdateUser(Png())));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Users(operatorUser).GetAsync(user.Id));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Users(operatorUser).ListAsync(null, false));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Drivers(operatorUser).CreateAsync(new("رضا", "محمدی", "09121112233", Png())));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Drivers(operatorUser).UpdateAsync(driver.Id, new("رضا", "محمدی", "09121112233", null, Png())));
        Assert.Equal(driver.Id, (await Drivers(operatorUser).GetAsync(driver.Id)).Id);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Drivers(User(RequesterUsername)).ListOptionsAsync(false));
        Assert.Null(user.AvatarDataUrl);
        Assert.Null(driver.AvatarDataUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectorySync_PreservesPhoto_AndLoginReturnsIt(bool windows)
    {
        var user = await Db.Users.SingleAsync(x => x.Username == RequesterUsername);
        user.SyncDirectory("قدیم", "کاربر", user.OrganizationalUnit, "[]", "CN=Photo User");
        Db.UserPermissions.Add(new UserPermission(user.Id, PermissionCatalog.Drivers.View, PermissionEffect.Grant));
        await Db.SaveChangesAsync();
        var saved = await Users(User(RequesterUsername)).UpdateProfilePhotoAsync(new(Png()));
        var session = new SessionContext(Guid.NewGuid().ToString(), null);
        var login = windows
            ? await Auth().LoginWindowsAsync("DOMAIN\\" + RequesterUsername, session)
            : await Auth().LoginLdapAsync(new(RequesterUsername, "password"), session);
        Assert.Equal(saved.AvatarDataUrl, login.AvatarDataUrl);
        Assert.Equal(saved.AvatarDataUrl, (await Db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).AvatarDataUrl);
        Assert.Equal("Directory User", login.DisplayName);
        Assert.Equal(saved.OrganizationalUnit, login.OrganizationalUnit);
        AssertNoPhotoInAudit();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("url")]
    [InlineData("svg")]
    [InlineData("jpeg")]
    [InlineData("nonimage")]
    [InlineData("base64")]
    [InlineData("whitespace")]
    [InlineData("header")]
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
    [InlineData("trailingData")]
    public async Task InvalidPhoto_RejectsAllWritePaths_WithoutChangingExistingRecords(string kind)
    {
        var avatar = Invalid(kind);
        var users = Users();
        var drivers = Drivers();
        var user = await users.CreateLocalAsync(CreateUser(Png()));
        var driver = await drivers.CreateAsync(new("رضا", "محمدی", "09121112233", Png()));
        var audits = Audit.Records.Count;
        await Assert.ThrowsAsync<ValidationAppException>(() => users.UpdateAsync(user.Id, UpdateUser(avatar)));
        await Assert.ThrowsAsync<ValidationAppException>(() => drivers.UpdateAsync(driver.Id,
            new("تغییر نام", "راننده", "09124445566", driver.RowVersion, avatar)));
        await Assert.ThrowsAsync<ValidationAppException>(() => Users(User(user.Username)).UpdateProfilePhotoAsync(new(avatar)));
        await Assert.ThrowsAsync<ValidationAppException>(() => users.CreateLocalAsync(CreateUser(avatar) with { Username = "invalid.user" }));
        await Assert.ThrowsAsync<ValidationAppException>(() => drivers.CreateAsync(new("تغییر", "راننده", "09124445566", avatar)));
        // Even another SaveChanges on the same context must not persist partially mutated profiles.
        Assert.False(Db.ChangeTracker.HasChanges());
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var storedUser = await Db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        var storedDriver = await Db.Drivers.AsNoTracking().SingleAsync();
        Assert.Equal(user.FirstName, storedUser.FirstName);
        Assert.Equal(user.OrganizationalUnit, storedUser.OrganizationalUnit);
        Assert.Equal(user.AvatarDataUrl, storedUser.AvatarDataUrl);
        Assert.Equal(driver.FirstName, storedDriver.FirstName);
        Assert.Equal(driver.MobilePhone, storedDriver.MobilePhone);
        Assert.Equal(driver.AvatarDataUrl, storedDriver.AvatarDataUrl);
        Assert.False(await Db.Users.AnyAsync(x => x.Username == "invalid.user"));
        Assert.Equal(audits, Audit.Records.Count);
    }

    [Fact]
    public void ValidPng_IsReencoded_StripsMetadata_PreservesSizeAndTransparency_AndIsReusable()
    {
        var original = Png(metadata: true);
        var normalized = AvatarImageValidator.ValidateAndNormalize(original)!;
        Assert.NotEqual(original, normalized);
        Assert.DoesNotContain("private-metadata", Encoding.ASCII.GetString(Convert.FromBase64String(normalized[Prefix.Length..])));
        using var image = Image.FromBinaryData(Convert.FromBase64String(normalized[Prefix.Length..]));
        Assert.Equal(256, image.Size.Width);
        Assert.Equal(256, image.Size.Height);
        // The fixture is transparent RGBA. PNG output retains an alpha channel.
        Assert.Equal(6, Convert.FromBase64String(normalized[Prefix.Length..])[25]);
        Assert.Equal(normalized, AvatarImageValidator.ValidateAndNormalize(normalized));
        Assert.Null(AvatarImageValidator.ValidateAndNormalize(null));
    }

    private void AssertNoPhotoInAudit()
    {
        var serialized = JsonSerializer.Serialize(Audit.Records) + JsonSerializer.Serialize(Db.AuditEntries.AsNoTracking().ToList());
        Assert.DoesNotContain("data:image", serialized);
        Assert.DoesNotContain("AvatarDataUrl", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static string Invalid(string kind) => kind switch
    {
        "empty" => "",
        "url" => "https://example.test/photo.png",
        "svg" => "data:image/svg+xml;base64," + Convert.ToBase64String("<svg/>"u8),
        "jpeg" => "data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 255, 216, 255, 217 }),
        "nonimage" => Prefix + Convert.ToBase64String("not an image"u8),
        "base64" => Prefix + "not%%%base64",
        "whitespace" => Png() + "\n",
        "header" => Png().Replace("image/png", "image/PNG"),
        "encodedOversize" => Prefix + new string('A', 350000),
        "decodedOversize" => Prefix + Convert.ToBase64String(new byte[AvatarImageValidator.MaxDecodedBytes + 1]),
        "nonsquare" => Png(headerHeight: 32),
        "dimensions" => Png(headerWidth: 257, headerHeight: 257),
        "dimensionBomb" => Png(headerWidth: uint.MaxValue, headerHeight: uint.MaxValue),
        "truncated" => Prefix + Convert.ToBase64String(Convert.FromBase64String(Png()[Prefix.Length..])[..^12]),
        "crc" => CorruptCrc(),
        "inflationBomb" => Png(extraRaw: 1000000),
        "filter" => Png(filter: 5),
        "truncatedDeflate" => Png(truncateDeflate: true),
        "animated" => Png(animated: true),
        "trailingData" => Prefix + Convert.ToBase64String([.. Convert.FromBase64String(Png()[Prefix.Length..]), 1]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string CorruptCrc()
    {
        var bytes = Convert.FromBase64String(Png()[Prefix.Length..]);
        bytes[29] ^= 1;
        return Prefix + Convert.ToBase64String(bytes);
    }

    // Independent PNG fixture encoder: zero-filter RGBA rows, zlib IDAT and standard CRC32.
    private static string Png(int size = 256, uint? headerWidth = null, uint? headerHeight = null,
        bool metadata = false, int extraRaw = 0, byte filter = 0, bool truncateDeflate = false, bool animated = false)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, headerWidth ?? (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), headerHeight ?? (uint)size);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR", header);
        if (metadata)
            Chunk(png, "tEXt", "comment\0private-metadata"u8.ToArray());
        if (animated)
            Chunk(png, "acTL", new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 });
        var raw = new byte[size * (1 + size * 4) + extraRaw];
        raw[0] = filter;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        var idat = compressed.ToArray();
        Chunk(png, "IDAT", truncateDeflate ? idat[..^4] : idat);
        Chunk(png, "IEND", []);
        return Prefix + Convert.ToBase64String(png.ToArray());
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        stream.Write(number);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(body);
        uint crc = uint.MaxValue;
        foreach (var value in body)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }

    private sealed class TestHasher : IPasswordHasher
    {
        public string Hash(string password) => "test:" + password;
        public bool Verify(string passwordHash, string password) => passwordHash == Hash(password);
    }

    private sealed class TestDirectory : IDirectoryAuthenticator
    {
        private static DirectoryUserInfo Info => new(RequesterUsername, "Directory User", "directory@example.test", "09121112233", [], "CN=Directory User");
        public Task<DirectoryAuthResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(DirectoryAuthResult.Succeeded(Info));
        public Task<DirectoryUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult<DirectoryUserInfo?>(Info);
        public Task<DirectoryProbeResult> ProbeAsync(string? sampleUsername, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
