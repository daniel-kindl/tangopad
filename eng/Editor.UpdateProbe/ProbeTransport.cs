using System.Net;

namespace Editor.UpdateProbe;

internal sealed class ProbeTransport(string configurationPath) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProbeConfiguration configuration = ProbeConfiguration.Parse(await File.ReadAllTextAsync(configurationPath, cancellationToken).ConfigureAwait(false));
        Uri uri = request.RequestUri ?? throw new InvalidDataException("The probe request has no URI.");
        string filename = Path.GetFileName(uri.AbsolutePath);
        string path = Path.Combine(configuration.FeedDirectory, filename);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (filename.EndsWith(".nupkg", StringComparison.Ordinal) && configuration.Fault == "truncate")
        {
            long count = stream.Length / 2;
            await stream.DisposeAsync().ConfigureAwait(false);
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            stream = new MemoryStream(bytes[..(int)count]);
        }
        HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        return response;
    }
}
