namespace NovaFleet.Application.Abstractions;

/// <summary>هش و بررسی رمز محلی؛ رمز خام هرگز ذخیره یا لاگ نمی‌شود.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
