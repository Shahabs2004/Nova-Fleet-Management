using NovaFleet.Domain.Identity;

namespace NovaFleet.Application.Identity;

/// <summary>درخواست ورود فرم؛ روش ورود در سمت سرور تعیین می‌شود و از بدنه درخواست اعتماد نمی‌شود.</summary>
public sealed record LoginRequest(string Username, string Password);

/// <summary>اطلاعات نشست برای ثبت سابقه ورود.</summary>
public sealed record SessionContext(string SessionKey, string? ClientIpAddress);

public sealed record CurrentUserDto(
    string Username,
    string DisplayName,
    string? OrganizationalUnit,
    bool IsLocalAdmin,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string? AvatarDataUrl = null);

/// <summary>ورود، خروج، تغییر رمز و بازیابی هویت جاری.</summary>
public interface IAuthService
{
    Task<CurrentUserDto> LoginLocalAsync(LoginRequest request, SessionContext session, CancellationToken cancellationToken = default);

    Task<CurrentUserDto> LoginLdapAsync(LoginRequest request, SessionContext session, CancellationToken cancellationToken = default);

    /// <summary>ورود یکپارچه ویندوز پس از احراز هویت IIS؛ در صورت نبود کاربر دایرکتوری، از نگاشت گروه ساخته می‌شود.</summary>
    Task<CurrentUserDto> LoginWindowsAsync(string username, SessionContext session, CancellationToken cancellationToken = default);

    Task LogoutAsync(string sessionKey, CancellationToken cancellationToken = default);

    Task<CurrentUserDto?> GetCurrentAsync(string username, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

public sealed record UserListItemDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? OrganizationalUnit,
    bool IsDisabled,
    bool IsDirectoryUser,
    bool IsLocalAdmin,
    IReadOnlyList<string> Roles,
    string? AvatarDataUrl = null);

public sealed record PermissionAssignmentDto(string Permission, PermissionEffect Effect);

public sealed record UserDetailsDto(
    Guid Id,
    string Username,
    string FirstName,
    string LastName,
    string? MobilePhone,
    string? Email,
    string? OrganizationalUnit,
    bool IsDisabled,
    bool IsDirectoryUser,
    bool IsLocalAdmin,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastDirectorySyncAtUtc,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> Roles,
    IReadOnlyList<PermissionAssignmentDto> DirectPermissions,
    string? AvatarDataUrl = null);

public sealed record CreateLocalUserRequest(
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string? MobilePhone,
    string? Email,
    string? OrganizationalUnit,
    bool IsLocalAdmin,
    IReadOnlyList<Guid> RoleIds,
    string? AvatarDataUrl = null);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? MobilePhone,
    string? Email,
    string? OrganizationalUnit,
    IReadOnlyList<Guid> RoleIds,
    string? AvatarDataUrl = null);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record SetPermissionsRequest(IReadOnlyList<PermissionAssignmentDto> Permissions);

public sealed record ProfileDto(
    string Username,
    string FirstName,
    string LastName,
    string? MobilePhone,
    string? Email,
    string? OrganizationalUnit,
    string? AvatarDataUrl = null);

// Reject over-posted identity, role and organizational-unit fields, rather than silently ignoring them.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProfilePhotoRequest(string? AvatarDataUrl = null);

public interface IUserService
{
    Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);

    Task<ProfileDto> UpdateProfilePhotoAsync(UpdateProfilePhotoRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserListItemDto>> ListAsync(string? search, bool includeDisabled, CancellationToken cancellationToken = default);

    Task<UserDetailsDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserDetailsDto> CreateLocalAsync(CreateLocalUserRequest request, CancellationToken cancellationToken = default);

    Task<UserDetailsDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<UserDetailsDto> SetDisabledAsync(Guid id, bool disabled, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken cancellationToken = default);
}

public sealed record RoleDto(
    Guid Id,
    string Name,
    string Title,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int UserCount,
    IReadOnlyList<PermissionAssignmentDto> Permissions);

public sealed record SaveRoleRequest(string Name, string Title, string? Description);

public sealed record DirectoryMappingDto(Guid Id, string GroupName, Guid RoleId, string RoleTitle, bool IsActive);

public sealed record SaveDirectoryMappingRequest(string GroupName, Guid RoleId);

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);

    Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken = default);

    Task<RoleDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);

    Task SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectoryMappingDto>> ListDirectoryMappingsAsync(CancellationToken cancellationToken = default);

    Task<DirectoryMappingDto> SaveDirectoryMappingAsync(SaveDirectoryMappingRequest request, CancellationToken cancellationToken = default);

    Task RemoveDirectoryMappingAsync(Guid id, CancellationToken cancellationToken = default);
}
