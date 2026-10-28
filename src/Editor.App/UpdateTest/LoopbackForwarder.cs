namespace Editor.App.UpdateTest;

/// <summary>
/// Test-only transport for update-test builds. It runs after the unchanged origin check in
/// the signed release source, and it sends only the three expected update files to a local feed.
/// </summary>
internal sealed class LoopbackForwarder : DelegatingHandler
{
    private readonly int port;
    private readonly HashSet<string> names;

    /// <summary>Creates a forwarder for one local port and a fixed set of file names.</summary>
    internal LoopbackForwarder(int port, IEnumerable<string> fileNames, HttpMessageHandler inner) : base(inner)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        this.port = port;
        names = new HashSet<string>(fileNames, StringComparer.Ordinal);
    }

    /// <summary>Returns the feed, its signature and the one full package for a channel and version.</summary>
    internal static IReadOnlyList<string> ExpectedNames(string channel, string version)
    {
        return [$"releases.{channel}.json", $"releases.{channel}.json.sig", $"TangopadDesktop-{version}-{channel}-full.nupkg"];
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri address = request.RequestUri ?? throw new HttpRequestException("The update test transport needs an absolute address.");
        string name = address.IsAbsoluteUri ? address.Segments[^1] : string.Empty;
        if (address.Query.Length != 0 || address.Fragment.Length != 0 || !names.Contains(name))
        {
            throw new HttpRequestException("The update test transport refuses an unexpected file: " + address.AbsolutePath);
        }
        request.RequestUri = new Uri($"http://127.0.0.1:{port}/{name}");
        return base.SendAsync(request, cancellationToken);
    }
}
