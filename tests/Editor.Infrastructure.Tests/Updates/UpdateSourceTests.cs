using System.Net;
using System.Security.Cryptography;
using System.Text;
using Editor.Infrastructure.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Xunit;

namespace Editor.Infrastructure.Tests.Updates;

/// <summary>Exercises signed feeds through the real update manager and a local HTTP transport.</summary>
public sealed class UpdateSourceTests
{
    /// <summary>Semantic version ordering prevents equal versions and downgrades.</summary>
    [Theory]
    [InlineData("0.2.0", "0.1.0", false)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.2.0", "0.10.0", true)]
    [InlineData("0.10.0", "1.0.0", true)]
    public async Task StableVersionPrecedence(string installed, string available, bool expected)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] feed = Encoding.UTF8.GetBytes($$"""
            {"Assets":[{"PackageId":"TangopadDesktop","Version":"{{available}}","Type":"Full","FileName":"TangopadDesktop-{{available}}-linux-x64-full.nupkg","SHA256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Size":123}]}
            """);
        byte[] signature = key.SignData(feed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        using Handler handler = new(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal) ? signature : feed)
        });
        using HttpClient client = new(handler, disposeHandler: false);
        SignedReleaseSource source = new(key.ExportSubjectPublicKeyInfoPem(), "linux-x64", TestContext.Current.CancellationToken, client);
        TestVelopackLocator locator = new("TangopadDesktop", installed, Path.GetTempPath());
        UpdateManager manager = new(source, new UpdateOptions { ExplicitChannel = "linux-x64", AllowVersionDowngrade = false }, locator);
        UpdateInfo? result = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
        Assert.Equal(expected, result is not null);
        if (expected) { Assert.Equal(available, result!.TargetFullRelease.Version.ToString()); }
    }

    /// <summary>Redirects cannot send requests outside the allowed HTTPS origins.</summary>
    [Theory]
    [InlineData("https://attacker.example/payload")]
    [InlineData("http://github.com/daniel-kindl/tangopad")]
    [InlineData("https://github.com:444/payload")]
    public async Task UntrustedRedirectFails(string destination)
    {
        int requests = 0;
        using Handler handler = new(_ =>
        {
            requests++;
            HttpResponseMessage response = new(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(destination);
            return response;
        });
        using HttpClient client = new(handler, disposeHandler: false);
        SignedReleaseSource source = new("unused", "linux-x64", TestContext.Current.CancellationToken, client);
        _ = await Assert.ThrowsAsync<InvalidDataException>(() => source.GetReleaseFeed(new Logger(), "TangopadDesktop", "linux-x64", null, null)).ConfigureAwait(true);
        Assert.Equal(1, requests);
    }

    /// <summary>Oversized metadata stops before parsing or signature verification.</summary>
    [Fact]
    public async Task OversizedFeedFails()
    {
        using Handler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[65537]) });
        using HttpClient client = new(handler, disposeHandler: false);
        SignedReleaseSource source = new("unused", "linux-x64", TestContext.Current.CancellationToken, client);
        _ = await Assert.ThrowsAsync<InvalidDataException>(() => source.GetReleaseFeed(new Logger(), "TangopadDesktop", "linux-x64", null, null)).ConfigureAwait(true);
    }

    /// <summary>Partial and oversized package responses cannot complete a download.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task PackageSizeMismatchFails(int received)
    {
        using Handler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[received]) });
        using HttpClient client = new(handler, disposeHandler: false);
        SignedReleaseSource source = new("unused", "linux-x64", TestContext.Current.CancellationToken, client);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            VelopackAsset asset = new() { Version = SemanticVersion.Parse("0.2.0"), FileName = "TangopadDesktop-0.2.0-linux-x64-full.nupkg", Size = 3 };
            _ = await Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadReleaseEntry(new Logger(), asset, path, _ => { }, TestContext.Current.CancellationToken)).ConfigureAwait(true);
        }
        finally { File.Delete(path); }
    }

    /// <summary>Cancellation stops a package request before a local file is created.</summary>
    [Fact]
    public async Task CancelledPackageRequestCreatesNoFile()
    {
        using Handler handler = new(_ => throw new InvalidOperationException("A cancelled request must not reach the handler."));
        using HttpClient client = new(handler, disposeHandler: false);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(true);
        SignedReleaseSource source = new("unused", "linux-x64", cancellation.Token, client);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        VelopackAsset asset = new() { Version = SemanticVersion.Parse("0.2.0"), FileName = "TangopadDesktop-0.2.0-linux-x64-full.nupkg", Size = 3 };
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.DownloadReleaseEntry(new Logger(), asset, path, _ => { }, cancellation.Token)).ConfigureAwait(true);
        Assert.False(File.Exists(path));
    }

    /// <summary>Timeouts and caller cancellation stop response bodies after headers arrive.</summary>
    [Theory]
    [InlineData("feed", false)]
    [InlineData("signature", false)]
    [InlineData("package", false)]
    [InlineData("feed", true)]
    [InlineData("signature", true)]
    [InlineData("package", true)]
    public async Task StalledResponseReadIsCancelled(string responsePart, bool callerCancellation)
    {
        using StalledStream body = new();
        using Handler handler = new(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = responsePart == "signature" && !request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? new ByteArrayContent("{}"u8.ToArray()) : new StreamContent(body)
        });
        using HttpClient client = new(handler, disposeHandler: false)
        {
            Timeout = callerCancellation ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(500)
        };
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        SignedReleaseSource source = new("unused", "linux-x64", cancellation.Token, client);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        VelopackAsset asset = new() { Version = SemanticVersion.Parse("0.2.0"), FileName = "TangopadDesktop-0.2.0-linux-x64-full.nupkg", Size = 3 };
        Task operation = responsePart == "package"
            ? source.DownloadReleaseEntry(new Logger(), asset, path, _ => { }, cancellation.Token)
            : source.GetReleaseFeed(new Logger(), "TangopadDesktop", "linux-x64", null, null);
        try
        {
            await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
            if (callerCancellation) { await cancellation.CancelAsync().ConfigureAwait(true); }
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ConfigureAwait(true);
            Assert.True(body.Disposed);
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
            try { await operation.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            File.Delete(path);
        }
    }

    private sealed class StalledStream : MemoryStream
    {
        internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _ = ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class Logger : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) { }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
