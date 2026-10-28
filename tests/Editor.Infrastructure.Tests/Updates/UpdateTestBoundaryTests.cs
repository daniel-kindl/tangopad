using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Editor.Infrastructure.Updates;
using Velopack.Logging;
using Xunit;

namespace Editor.Infrastructure.Tests.Updates;

/// <summary>Proves that a default build keeps the update-test path out of production update code.</summary>
public sealed class UpdateTestBoundaryTests
{
    /// <summary>A default build gives internals only to its tests and the update probe, never to the editor.</summary>
    [Fact]
    public void DefaultBuildGivesNoInternalsToTheEditor()
    {
        string[] friends = [.. typeof(SignedReleaseSource).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName).Order(StringComparer.Ordinal)];
        Assert.Equal(["Editor.Infrastructure.Tests", "Tangopad.UpdateProbe"], friends);
        Assert.DoesNotContain("Editor.App", friends);
    }

    /// <summary>The production source rejects a local host and a local feed, also after a redirect.</summary>
    [Theory]
    [InlineData("http://127.0.0.1:8080/releases.linux-x64.json")]
    [InlineData("https://127.0.0.1/releases.linux-x64.json")]
    [InlineData("https://localhost/releases.linux-x64.json")]
    [InlineData("https://[::1]/releases.linux-x64.json")]
    [InlineData("http://localhost:5000/releases.linux-x64.json")]
    [InlineData("file:///tmp/releases.linux-x64.json")]
    [InlineData("https://github.com.attacker.example/releases.linux-x64.json")]
    [InlineData("https://api.github.com/releases.linux-x64.json")]
    public async Task LocalOrUnknownFeedFails(string destination)
    {
        List<Uri> requests = [];
        using Handler handler = new(request =>
        {
            requests.Add(request.RequestUri!);
            HttpResponseMessage response = new(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(destination);
            return response;
        });
        using HttpClient client = new(handler, disposeHandler: false);
        SignedReleaseSource source = new("unused", "linux-x64", TestContext.Current.CancellationToken, client);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => source.GetReleaseFeed(new Logger(), "TangopadDesktop", "linux-x64", null, null)).ConfigureAwait(true);
        Assert.Contains("not trusted", error.Message, StringComparison.Ordinal);
        Uri only = Assert.Single(requests);
        Assert.Equal("https://github.com/daniel-kindl/tangopad/releases/latest/download/releases.linux-x64.json", only.AbsoluteUri);
    }

    /// <summary>The allow-list contains exactly the three GitHub release hosts.</summary>
    [Theory]
    [InlineData("github.com")]
    [InlineData("release-assets.githubusercontent.com")]
    [InlineData("objects.githubusercontent.com")]
    public async Task AllowedReleaseHostIsRequested(string host)
    {
        List<Uri> requests = [];
        using Handler handler = new(request =>
        {
            requests.Add(request.RequestUri!);
            if (requests.Count > 1) { return new HttpResponseMessage(HttpStatusCode.NotFound); }
            HttpResponseMessage response = new(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri($"https://{host}/feed");
            return response;
        });
        using HttpClient client = new(handler, disposeHandler: false);
        SignedReleaseSource source = new("unused", "linux-x64", TestContext.Current.CancellationToken, client);
        _ = await Record.ExceptionAsync(() => source.GetReleaseFeed(new Logger(), "TangopadDesktop", "linux-x64", null, null)).ConfigureAwait(true);
        Assert.Equal(2, requests.Count);
        Assert.Equal(host, requests[1].Host);
    }

    private sealed class Logger : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) { }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
