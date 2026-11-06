using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Bismillah;

/// <summary>A release on GitHub that is newer than this app and carries the update files.</summary>
public sealed record ReleaseInfo(string Version, Uri Zip, Uri Signature, Uri Page);

public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Finding and trusting updates. A release carries `Bismillah-&lt;version&gt;-win-x64.zip` and `.zip.sig`. The signature is an
/// Ed25519 signature of the zip, made with a private key that never leaves the maintainer's machine; the matching
/// public key is built into the app. Nothing is installed unless the signature checks out, so a hijacked release page
/// alone can't ship code to users.
/// </summary>
public static class UpdateFeed
{
    public const string Repository = "hafizuddin-a/bismillah-startup";
    public static Uri DefaultFeed => new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>The public half of the key that signs releases (base64).</summary>
    public const string PublicKey = "oyisxX7OzEXU/Jr/AOKEphZ0dGZe5nhYGj1t/pVwFys=";

    public static string ZipName(string version) => $"Bismillah-{version}-win-x64.zip";

    private sealed class ApiRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<ApiAsset> Assets { get; set; } = [];
    }

    private sealed class ApiAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
    }

    /// <summary>Reads GitHub's "latest release" JSON. Returns the release if it is newer than `current`, otherwise null.</summary>
    public static ReleaseInfo? Parse(string json, string current)
    {
        var api = JsonSerializer.Deserialize<ApiRelease>(json) ?? throw new UpdateException("The release information was empty.");
        if (api.Draft || api.Prerelease) return null;
        string version = api.TagName.Trim().TrimStart('v', 'V');
        if (!IsNewer(version, current)) return null;
        var zip = api.Assets.FirstOrDefault(a => a.Name == ZipName(version));
        var sig = api.Assets.FirstOrDefault(a => a.Name == ZipName(version) + ".sig");
        if (zip is null || sig is null) throw new UpdateException($"Version {version} is published but its update files are missing.");
        return new ReleaseInfo(version, new Uri(zip.Url), new Uri(sig.Url), new Uri(api.HtmlUrl));
    }

    public static bool IsNewer(string candidate, string current)
    {
        static int[] Parts(string v) => v.Trim().TrimStart('v', 'V').Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToArray();
        var a = Parts(candidate);
        var b = Parts(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>True when `signatureBase64` is a valid Ed25519 signature of `data` under `publicKeyBase64`.</summary>
    public static bool VerifySignature(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            var key = Convert.FromBase64String(publicKeyBase64.Trim());
            var signature = Convert.FromBase64String(signatureBase64.Trim());
            if (key.Length != Ed25519.PublicKeySize || signature.Length != Ed25519.SignatureSize) return false;
            return Ed25519.Verify(signature, 0, key, 0, data, 0, data.Length);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
