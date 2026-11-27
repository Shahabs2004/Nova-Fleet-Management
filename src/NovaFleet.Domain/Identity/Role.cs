using NovaFleet.Domain.Common;

namespace NovaFleet.Domain.Identity;

/// <summary>نقش قابل تخصیص به کاربران؛ نقش سفارشی نیز پشتیبانی می‌شود.</summary>
public sealed class Role : Entity
{
    private Role()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; } = true;

    public Role(string name, string title, string? description = null, bool isSystem = false)
    {
        Name = TextRules.Required(name, 64, nameof(name));
        Title = TextRules.Required(title, 200, nameof(title));
        Description = description?.Trim();
        IsSystem = isSystem;
    }

    public void Update(string title, string? description)
    {
        Title = TextRules.Required(title, 200, nameof(title));
        Description = description?.Trim();
    }

    public void Disable() => IsActive = false;

    public void Enable() => IsActive = true;
}
