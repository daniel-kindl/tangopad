using System.Security.Cryptography;
using System.Text;
using Editor.Infrastructure.Updates;
using Xunit;

namespace Editor.Infrastructure.Tests.Updates;

/// <summary>Checks the trust boundary before update metadata can reach the installer.</summary>
public sealed class SignedReleaseFeedTests
{
    private const string Feed = /*lang=json,strict*/ """
        {"Assets":[{"PackageId":"TangopadDesktop","Version":"0.2.0","Type":"Full","FileName":"TangopadDesktop-0.2.0-linux-x64-full.nupkg","SHA256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Size":123}]}
        """;

    /// <summary>A pinned key authenticates the exact feed bytes.</summary>
    [Fact]
    public void ValidSignatureAcceptsRelease()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] bytes = Encoding.UTF8.GetBytes(Feed);
        byte[] signature = key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal("0.2.0", SignedReleaseSource.VerifyFeed(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "linux-x64").Assets.Single().Version.ToString());
    }

    /// <summary>Null release entries are invalid even when their feed is signed.</summary>
    [Fact]
    public void NullReleaseFails()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] bytes = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"Assets":[null]}""");
        byte[] signature = key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        _ = Assert.Throws<InvalidDataException>(() => SignedReleaseSource.VerifyFeed(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "linux-x64"));
    }

    /// <summary>Changes to signed bytes and signatures from another key fail authentication.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TamperingFails(bool changeBytes)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] bytes = Encoding.UTF8.GetBytes(Feed);
        byte[] signature = (changeBytes ? key : other).SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (changeBytes) { bytes[0] = (byte)' '; }
        _ = Assert.Throws<CryptographicException>(() => SignedReleaseSource.VerifyFeed(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "linux-x64"));
    }

    /// <summary>Authenticated metadata must still obey package identity, platform, path, and channel policy.</summary>
    [Theory]
    [InlineData("TangopadDesktop-0.2.0-linux-x64-full.nupkg", "../payload.nupkg")]
    [InlineData("linux-x64", "win-x64")]
    [InlineData("TangopadDesktop", "OtherApp")]
    [InlineData("0.2.0", "0.2.0-beta.1")]
    [InlineData("123", "0")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "ABCD")]
    public void InvalidMetadataFails(string before, string after)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] bytes = Encoding.UTF8.GetBytes(Feed.Replace(before, after, StringComparison.Ordinal));
        byte[] signature = key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        _ = Assert.Throws<InvalidDataException>(() => SignedReleaseSource.VerifyFeed(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "linux-x64"));
    }
}
