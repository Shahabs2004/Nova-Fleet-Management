using System.Net;

namespace NovaFleet.Infrastructure.Sms;

public static class SmsHttpClientPolicy
{
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        // تغییر مسیر می‌تواند مقصدِ تأییدشده را به loopback/link-local تغییر دهد؛ درگاه باید مسیر ثابت داشته باشد.
        AllowAutoRedirect = false
    };
}
