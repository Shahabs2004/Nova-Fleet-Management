using Microsoft.EntityFrameworkCore;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Authorization;
using NovaFleet.Application.Common;
using NovaFleet.Application.Locations;
using NovaFleet.Domain.Locations;
using NovaFleet.Infrastructure.Persistence;

namespace NovaFleet.Infrastructure.Locations;

/// <summary>مقاصد سفر؛ پیشنهاد درخواست‌کننده نیازمند تأیید مدیر است و نام تکراری پذیرفته نمی‌شود.</summary>
public sealed class LocationService(
    NovaFleetDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit) : ILocationService
{
    public async Task<IReadOnlyList<LocationDto>> ListAsync(
        LocationStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Missions.View);
        var source = db.Locations.AsNoTracking();
        if (status.HasValue)
        {
            source = source.Where(item => item.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            source = source.Where(item => item.Name.Contains(term));
        }

        return await source
            .OrderBy(item => item.Name)
            .Select(item => new LocationDto(
                item.Id,
                item.Name,
                item.Status,
                item.ProposedBy,
                item.ReviewedBy,
                item.ReviewReason,
                item.ReviewedAtUtc,
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListApprovedNamesAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(PermissionCatalog.Missions.View, PermissionCatalog.Missions.ViewOwn, PermissionCatalog.Missions.Create);
        return await db.Locations.AsNoTracking()
            .Where(item => item.Status == LocationStatus.Approved)
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<LocationDto> ProposeAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        var normalized = name.Trim();
        if (await db.Locations.AnyAsync(item => item.Name == normalized, cancellationToken))
        {
            throw new ConflictAppException("این مکان قبلاً ثبت شده است.");
        }

        var location = new Location(normalized, currentUser.Username);
        db.Locations.Add(location);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ProposeLocation", "Location", location.Id.ToString(), "Success", After: new { location.Name }), cancellationToken);
        return await GetDtoAsync(location.Id, cancellationToken);
    }

    public async Task<LocationDto> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.LocationsManage);
        var location = await LoadAsync(id, cancellationToken);
        location.Approve(currentUser.Username);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ApproveLocation", "Location", id.ToString(), "Success"), cancellationToken);
        return await GetDtoAsync(id, cancellationToken);
    }

    public async Task<LocationDto> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.LocationsManage);
        var location = await LoadAsync(id, cancellationToken);
        location.Reject(currentUser.Username, reason);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("RejectLocation", "Location", id.ToString(), "Success", Reason: reason), cancellationToken);
        return await GetDtoAsync(id, cancellationToken);
    }

    public async Task<LocationDto> DisableAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.LocationsManage);
        var location = await LoadAsync(id, cancellationToken);
        location.Disable(currentUser.Username, reason);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("DisableLocation", "Location", id.ToString(), "Success", Reason: reason), cancellationToken);
        return await GetDtoAsync(id, cancellationToken);
    }

    public async Task<LocationDto> ReopenAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.LocationsManage);
        var location = await LoadAsync(id, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        if (await db.Locations.AnyAsync(item => item.Name == name.Trim() && item.Id != id, cancellationToken))
        {
            throw new ConflictAppException("مکانی با این نام وجود دارد.");
        }

        location.Reopen(name);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("ReopenLocation", "Location", id.ToString(), "Success", After: new { location.Name }), cancellationToken);
        return await GetDtoAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        currentUser.Require(PermissionCatalog.Administration.LocationsManage);
        var location = await LoadAsync(id, cancellationToken);
        if (location.Status != LocationStatus.Rejected && location.Status != LocationStatus.Disabled)
        {
            throw new ValidationAppException("فقط مکان ردشده یا غیرفعال قابل حذف است.");
        }

        db.Locations.Remove(location);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditRecord("DeleteLocation", "Location", id.ToString(), "Success"), cancellationToken);
    }

    private async Task<Location> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Locations.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
        ?? throw new NotFoundAppException("مکان پیدا نشد.");

    private async Task<LocationDto> GetDtoAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Locations.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new LocationDto(
                item.Id,
                item.Name,
                item.Status,
                item.ProposedBy,
                item.ReviewedBy,
                item.ReviewReason,
                item.ReviewedAtUtc,
                item.CreatedAtUtc))
            .FirstAsync(cancellationToken);
}
