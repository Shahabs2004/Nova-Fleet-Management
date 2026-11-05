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
}
