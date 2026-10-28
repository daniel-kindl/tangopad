using System.Net;
using Editor.App.UpdateTest;
using Xunit;

namespace Editor.App.Tests.Updates;

/// <summary>Checks the test-only transport that sends the three expected update files to a local feed.</summary>
public sealed class LoopbackForwarderTests
{
    private const string Releases = "https://github.com/daniel-kindl/tangopad/releases/";

    /// <summary>The forwarder knows exactly the feed, its signature and the one package.</summary>
    [Fact]
    public void ExpectedNamesAreTheFeedSignatureAndPackage()
    {
        Assert.Equal(["releases.linux-x64.json", "releases.linux-x64.json.sig", "TangopadDesktop-1.0.1-linux-x64-full.nupkg"],
            LoopbackForwarder.ExpectedNames("linux-x64", "1.0.1"));
    }

    /// <summary>Each expected file goes to the loopback port with the same file name.</summary>
    [Theory]
    [InlineData("latest/download/releases.linux-x64.json")]
    [InlineData("latest/download/releases.linux-x64.json.sig")]
    [InlineData("download/v1.0.1/TangopadDesktop-1.0.1-linux-x64-full.nupkg")]
    public async Task ExpectedFileGoesToLoopback(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        List<Uri> received = [];
        using HttpResponseMessage response = await Send(Releases + path, received).ConfigureAwait(true);
        Uri target = Assert.Single(received);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"http://127.0.0.1:45678/{path[(path.LastIndexOf('/') + 1)..]}", target.AbsoluteUri);
    }

    /// <summary>Any other file name is refused, also when the host is on the update allow-list.</summary>
    [Theory]
    [InlineData(Releases + "latest/download/releases.osx-x64.json")]
    [InlineData(Releases + "latest/download/releases.linux-x64.json.bak")]
    [InlineData(Releases + "download/v1.0.2/TangopadDesktop-1.0.2-linux-x64-full.nupkg")]
    [InlineData(Releases + "download/v1.0.1/Tangopad.exe")]
    [InlineData(Releases + "latest/download/releases.linux-x64.json?name=other")]
    [InlineData(Releases + "latest/download/RELEASES.linux-x64.json")]
    [InlineData(Releases + "latest/download/")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/payload.bin")]
    [InlineData("https://objects.githubusercontent.com/releases.linux-x64.json%2F..%2Fsecret")]
    public async Task UnexpectedFileIsRefused(string address)
    {
        List<Uri> received = [];
        _ = await Assert.ThrowsAsync<HttpRequestException>(() => Send(address, received)).ConfigureAwait(true);
        Assert.Empty(received);
    }

    /// <summary>The forwarder needs a real TCP port.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidPortIsRefused(int port)
    {
        using Inner inner = new([]);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new LoopbackForwarder(port, [], inner));
    }

    private static async Task<HttpResponseMessage> Send(string address, List<Uri> received)
    {
        using Inner inner = new(received);
        using LoopbackForwarder forwarder = new(45678, LoopbackForwarder.ExpectedNames("linux-x64", "1.0.1"), inner);
        using HttpClient client = new(forwarder, disposeHandler: false);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(address));
        return await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private sealed class Inner(List<Uri> received) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            received.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
