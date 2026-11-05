using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Organization;

/// <summary>واحد سازمانی ساده و بدون سلسله‌مراتب.</summary>
public sealed class OrganizationUnit : Entity
{
    private OrganizationUnit()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public OrganizationUnit(string name)
    {
        Rename(name);
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        var normalized = name.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "نام واحد سازمانی حداکثر ۲۰۰ نویسه است.");
        }

        Name = normalized;
    }

    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;
}
