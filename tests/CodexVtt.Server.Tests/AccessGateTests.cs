using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodexVtt.Server.Tests;

public class AccessGateTests
{
    private static async Task<(int Status, bool Reached, HttpContext Context)> Send(
        string? token, string remote, string path = "/", string? query = null, string? cookie = null)
    {
        var reached = false;
        var gate = new AccessGate(_ => { reached = true; return Task.CompletedTask; },
            Options.Create(new ServerOptions { AccessToken = token }), NullLogger<AccessGate>.Instance);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Path = path;
        context.Request.Host = new HostString("codexvtt.lan:5185");
        if (query is not null)
            context.Request.QueryString = new QueryString(query);
        if (cookie is not null)
            context.Request.Headers.Cookie = $"{AccessGate.CookieName}={cookie}";
        context.Response.Body = new MemoryStream();
        await gate.InvokeAsync(context);
        return (context.Response.StatusCode, reached, context);
    }

    [Fact]
    public async Task Without_a_token_only_the_same_pc_gets_in()
    {
        Assert.True((await Send(null, "127.0.0.1")).Reached);
        Assert.True((await Send(null, "::1")).Reached);

        var remote = await Send(null, "192.168.1.20");
        Assert.False(remote.Reached);
        Assert.Equal(StatusCodes.Status403Forbidden, remote.Status);
    }

    [Fact]
    public async Task With_a_token_requests_need_the_cookie()
    {
        Assert.Equal(StatusCodes.Status401Unauthorized, (await Send("segreto", "192.168.1.20")).Status);
        Assert.Equal(StatusCodes.Status401Unauthorized, (await Send("segreto", "127.0.0.1", cookie: "sbagliato")).Status);
        Assert.True((await Send("segreto", "192.168.1.20", cookie: "segreto")).Reached);
    }

    [Fact]
    public async Task Opening_the_link_with_the_token_sets_the_cookie_and_hides_the_token()
    {
        var result = await Send("segreto", "192.168.1.20", query: "?token=segreto");

        Assert.False(result.Reached);
        Assert.Equal(StatusCodes.Status302Found, result.Status);
        Assert.Equal("/", result.Context.Response.Headers.Location.ToString());
        var cookie = result.Context.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusCodes.Status401Unauthorized, (await Send("segreto", "192.168.1.20", query: "?token=altro")).Status);
    }

    [Theory]
    [InlineData("http://codexvtt.lan:5185", true)]
    [InlineData("http://evil.example", false)]
    [InlineData("http://codexvtt.lan:9999", false)]
    [InlineData("", true)]
    public void Websocket_origin_must_match_the_host(string origin, bool expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("codexvtt.lan:5185");
        if (origin.Length > 0)
            request.Headers.Origin = origin;

        Assert.Equal(expected, AccessGate.SameOrigin(request));
    }

    [Theory]
    [InlineData("riunione.mp3", "riunione.mp3")]
    [InlineData(@"..\..\Windows\evil.wav", "evil.wav")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("ab*c?.wav", "ab_c_.wav")]
    [InlineData("..", "audio")]
    [InlineData("  ", "audio")]
    public void Uploaded_names_cannot_escape_the_upload_folder(string name, string expected) =>
        Assert.Equal(expected, BridgeEndpoints.SafeFileName(name));
}
