namespace NovaFleet.Domain.Identity;

/// <summary>نوع اثر یک مجوز؛ منع صریح بر اعطا اولویت دارد.</summary>
public enum PermissionEffect
{
    Grant = 1,
    Deny = 2
}
