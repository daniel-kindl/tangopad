using System.Net;
using System.Security.Cryptography;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Editor.Infrastructure.Updates;

internal sealed class SignedReleaseSource(string publicKey, string releaseChannel, CancellationToken checkToken, HttpClient? transport = null) : IUpdateSource
{
    private const string Repository = "https://github.com/daniel-kindl/tangopad/releases/";
    private const int MaximumFeedBytes = 65536;
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true }) { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
    {
        string name = $"releases.{releaseChannel}.json";
        byte[] bytes = await ReadBoundedAsync(new Uri(Repository + "latest/download/" + name), MaximumFeedBytes, checkToken).ConfigureAwait(false);
        byte[] signature = await ReadBoundedAsync(new Uri(Repository + "latest/download/" + name + ".sig"), 256, checkToken).ConfigureAwait(false);
        return VerifyFeed(bytes, signature, publicKey, releaseChannel);
    }

    internal static VelopackAssetFeed VerifyFeed(byte[] bytes, byte[] signature, string publicKey, string channel)
    {
        if (bytes.Length > MaximumFeedBytes || signature.Length > 256) { throw new InvalidDataException("The update metadata exceeds its size limit."); }
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(publicKey);
        if (key.KeySize != 256 || !key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
        {
            throw new CryptographicException("The update metadata signature is invalid.");
        }
        VelopackAssetFeed feed = VelopackAssetFeed.FromJson(System.Text.Encoding.UTF8.GetString(bytes));
        if (feed is null || feed.Assets is null || feed.Assets.Length != 1) { throw new InvalidDataException("The update feed must contain one full stable release."); }
        VelopackAsset? asset = feed.Assets[0];
        return asset is null || asset.PackageId != "TangopadDesktop" || asset.Type != VelopackAssetType.Full || asset.Version is null || asset.Version.IsPrerelease || asset.Version.HasMetadata
            || asset.Size is <= 0 or > 2147483648 || asset.SHA256 is null || asset.SHA256.Length != 64 || !asset.SHA256.All(char.IsAsciiHexDigit)
            || asset.FileName != $"TangopadDesktop-{asset.Version}-{channel}-full.nupkg" || asset.NotesMarkdown?.Length > 16384
            ? throw new InvalidDataException("The update metadata does not match the stable package policy.")
            : feed;
    }

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
    {
        Uri address = new(Repository + $"download/v{releaseEntry.Version}/" + releaseEntry.FileName);
        using HttpResponseMessage response = await GetAsync(address, cancelToken).ConfigureAwait(false);
        using Stream input = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        using FileStream output = new(localFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        byte[] buffer = new byte[65536];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancelToken).ConfigureAwait(false)) != 0)
        {
            received += count;
            if (received > releaseEntry.Size) { throw new InvalidDataException("The update package exceeds its authenticated size."); }
            await output.WriteAsync(buffer.AsMemory(0, count), cancelToken).ConfigureAwait(false);
            progress((int)(received * 100 / releaseEntry.Size));
        }
        if (received != releaseEntry.Size) { throw new InvalidDataException("The update package download is incomplete."); }
    }

    private async Task<byte[]> ReadBoundedAsync(Uri address, int maximum, CancellationToken token)
    {
        using HttpResponseMessage response = await GetAsync(address, token).ConfigureAwait(false);
        using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer = new byte[maximum + 1];
        int count = await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, token).ConfigureAwait(false);
        return count > maximum ? throw new InvalidDataException("The update metadata exceeds its size limit.") : buffer[..count];
    }

    private async Task<HttpResponseMessage> GetAsync(Uri address, CancellationToken token)
    {
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (address.Scheme != "https" || !address.IsDefaultPort || address.UserInfo.Length != 0
                || address.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            {
                throw new InvalidDataException("The update download origin is not trusted.");
            }
            using HttpRequestMessage request = new(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd("Tangopad-Updater/1.0");
            HttpResponseMessage response = await (transport ?? DefaultClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                address = location is not null ? new Uri(address, location) : throw new InvalidDataException("The update redirect has no destination.");
                continue;
            }
            try { _ = response.EnsureSuccessStatusCode(); }
            catch { response.Dispose(); throw; }
            return response;
        }
        throw new InvalidDataException("The update download has too many redirects.");
    }
}
