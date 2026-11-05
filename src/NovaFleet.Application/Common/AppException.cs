namespace NovaFleet.Application.Common;

/// <summary>خطای قابل نمایش و قابل نگاشت به پاسخ HTTP؛ پیام آن برای کاربر نهایی امن است.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class ValidationAppException(string message) : AppException(message);

public sealed class UnauthorizedAppException(string message) : AppException(message);

public sealed class ForbiddenAppException(string message) : AppException(message);

public sealed class NotFoundAppException(string message) : AppException(message);

public sealed class ConflictAppException(string message) : AppException(message);
