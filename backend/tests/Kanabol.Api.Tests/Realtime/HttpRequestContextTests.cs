using Kanabol.Api.Realtime;
using Microsoft.AspNetCore.Http;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// X-Connection-Id, which the browser sends on each request so that its own board can ignore the events the
/// request causes (SPEC section 9). It goes out to everyone on the project, so only an id SignalR could
/// have made passes.
/// </summary>
public sealed class HttpRequestContextTests
{
    private static string? ConnectionIdOf(params string[] headerValues)
    {
        var http = new DefaultHttpContext();
        if (headerValues.Length > 0)
        {
            http.Request.Headers[HttpRequestContext.ConnectionIdHeader] = headerValues;
        }

        return new HttpRequestContext(new HttpContextAccessor { HttpContext = http }).ConnectionId;
    }

    [Fact]
    public void ConnectionId_OfAnIdAsSignalRMakesThem_IsThatId()
    {
        ConnectionIdOf("sJ0KHkCgN1vD-9_qW2xZtA").Should().Be("sJ0KHkCgN1vD-9_qW2xZtA");
    }

    [Fact]
    public void ConnectionId_WithoutTheHeader_IsNull()
    {
        ConnectionIdOf().Should().BeNull();
    }

    [Fact]
    public void ConnectionId_OutsideARequest_IsNull()
    {
        new HttpRequestContext(new HttpContextAccessor()).ConnectionId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("ăîș")]
    public void ConnectionId_OfAnythingElse_IsNull(string value)
    {
        ConnectionIdOf(value).Should().BeNull();
    }

    [Fact]
    public void ConnectionId_LongerThan64Characters_IsNull()
    {
        ConnectionIdOf(new string('a', 65)).Should().BeNull();
        ConnectionIdOf(new string('a', 64)).Should().HaveLength(64);
    }

    [Fact]
    public void ConnectionId_SentTwice_IsNull()
    {
        ConnectionIdOf("first", "second").Should().BeNull();
    }
}
