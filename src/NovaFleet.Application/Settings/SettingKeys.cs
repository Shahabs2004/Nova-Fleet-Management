namespace NovaFleet.Application.Settings;

/// <summary>کلیدهای پایدار تنظیمات و مقادیر پیش‌فرض مصوب SPEC.</summary>
public static class SettingKeys
{
    public const string OrganizationName = "organization.name";
    public const string OrganizationLogo = "organization.logo";

    public const string MinimumGapMinutes = "missions.minimum-gap-minutes";
    public const string FutureRequestDays = "missions.future-request-days";
    public const string WithdrawalAutoCloseHours = "missions.withdrawal-auto-close-hours";

    public const string SessionAbsoluteMinutes = "session.absolute-minutes";
    public const string SessionIdleMinutes = "session.idle-minutes";

    public const string SmsReminderMinutes = "sms.reminder-minutes";
    public const string SmsMaxAttempts = "sms.max-attempts";
    public const string SmsRetryMinutes = "sms.retry-minutes";
    public const string SmsPollingMinutes = "sms.polling-minutes";
    public const string SmsMaxPollingAttempts = "sms.max-polling-attempts";
    public const string SmsRecipientsPrefix = "sms.recipients.";

    public const string MissionOrderHeader = "missions.order.header";
    public const string MissionOrderFooter = "missions.order.footer";
    public const string MissionOrderShowLogo = "missions.order.show-logo";

    public const string DirectoryEnabled = "directory.enabled";
    public const string DirectoryHost = "directory.host";
    public const string DirectoryPort = "directory.port";
    public const string DirectoryUseSsl = "directory.use-ssl";
    public const string DirectoryDomain = "directory.domain";
    public const string DirectoryBaseDn = "directory.base-dn";
    public const string DirectoryBindDn = "directory.bind-dn";
    /// <summary>رمز حساب Bind؛ فقط رمزنگاری‌شده ذخیره و هرگز به UI برگردانده نمی‌شود.</summary>
    public const string DirectoryBindPassword = "directory.bind-password";
    public const string DirectoryTimeoutSeconds = "directory.timeout-seconds";
    public const string DirectorySearchFilter = "directory.search-filter";

    /// <summary>کلید سراسری فعال/غیرفعال بودن ارسال پیامک؛ پیش‌فرض خاموش است تا هیچ پیامک پولی بدون تصمیم مدیر ارسال نشود.</summary>
    public const string SmsEnabled = "sms.enabled";

    public const string SmsMode = "sms.mode";
    public const string SmsEndpoint = "sms.endpoint";
    public const string SmsOrganization = "sms.organization";
    public const string SmsUsername = "sms.username";
    /// <summary>رمز درگاه پیامک؛ فقط رمزنگاری‌شده ذخیره و هرگز به UI برگردانده نمی‌شود.</summary>
    public const string SmsPassword = "sms.password";
    public const string SmsSenderNumber = "sms.sender-number";
    public const string SmsTimeoutSeconds = "sms.timeout-seconds";

    public const string BrandingPdfFontPath = "branding.pdf-font-path";
    public const string BrandingReportPaperSize = "branding.report-paper-size";

    public const string OperationsLogLevel = "operations.log-level";

    public const string SetupCompleted = "setup.completed";

    public const int DefaultMinimumGapMinutes = 30;
    public const int DefaultFutureRequestDays = 30;
    public const int DefaultWithdrawalAutoCloseHours = 24;
    public const int DefaultSessionAbsoluteMinutes = 480;
    public const int DefaultSessionIdleMinutes = 30;
    public const int DefaultSmsReminderMinutes = 60;
    public const int DefaultSmsMaxAttempts = 5;
    public const int DefaultSmsRetryMinutes = 5;
    public const int DefaultSmsPollingMinutes = 10;
    public const int DefaultSmsMaxPollingAttempts = 12;

    public const string DefaultSmsMode = "Simulation";
    public const int DefaultSmsTimeoutSeconds = 20;
    public const int DefaultDirectoryPort = 636;
    public const int DefaultDirectoryTimeoutSeconds = 15;
    public const string DefaultLogLevel = "Information";
    public const string DefaultReportPaperSize = "A4";
    public const string DefaultDirectorySearchFilter = "(&(objectClass=user)(sAMAccountName={0}))";

    /// <summary>حالت‌های مجاز درگاه پیامک؛ Simulation تنها حالتی است که به شبکه نیاز ندارد.</summary>
    public static IReadOnlyList<string> SmsModes { get; } = ["Simulation", "Wsdl", "Api"];

    public static IReadOnlyList<string> LogLevels { get; } =
        ["Critical", "Error", "Warning", "Information", "Debug", "Trace"];

    public static IReadOnlyList<string> ReportPaperSizes { get; } = ["A4", "A5", "Letter"];
}
