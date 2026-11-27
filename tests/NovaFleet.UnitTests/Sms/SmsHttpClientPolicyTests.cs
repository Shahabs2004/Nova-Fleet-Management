using System.Net;
using NovaFleet.Infrastructure.Sms;

namespace NovaFleet.UnitTests.Sms;

public sealed class SmsHttpClientPolicyTests
{
    [Fact]
    public void CreateHandler_DisablesRedirectsSoGatewayCannotBePivotedToAnotherHost()
    {
        using var handler = SmsHttpClientPolicy.CreateHandler();

        Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(((SocketsHttpHandler)handler).AllowAutoRedirect);
    }
}
