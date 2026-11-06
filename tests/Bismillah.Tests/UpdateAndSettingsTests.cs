using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Org.BouncyCastle.Security;
using Xunit;

namespace Bismillah.Tests;

public class UpdateFeedTests
{
    private static string Release(string tag, bool draft = false, bool prerelease = false, bool assets = true)
    {
        string zip = $"Bismillah-{tag.TrimStart('v')}-win-x64.zip";
        string list = assets
            ? $$"""[{"name":"{{zip}}","browser_download_url":"https://example.com/{{zip}}"},{"name":"{{zip}}.sig","browser_download_url":"https://example.com/{{zip}}.sig"}]"""
            : "[]";
        return $$"""{"tag_name":"{{tag}}","html_url":"https://example.com/release","draft":{{draft.ToString().ToLower()}},"prerelease":{{prerelease.ToString().ToLower()}},"assets":{{list}}}""";
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("v1.2", "1.2.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    public void VersionsCompareNumerically(string candidate, string current, bool newer) =>
        Assert.Equal(newer, UpdateFeed.IsNewer(candidate, current));

    [Fact]
    public void ANewerReleaseIsFoundWithItsFiles()
    {
        var release = UpdateFeed.Parse(Release("v1.1.0"), "1.0.0");
        Assert.NotNull(release);
        Assert.Equal("1.1.0", release!.Version);
        Assert.EndsWith("Bismillah-1.1.0-win-x64.zip", release.Zip.ToString());
        Assert.EndsWith(".zip.sig", release.Signature.ToString());
    }

    [Fact]
    public void AnOlderOrEqualReleaseIsIgnored()
    {
        Assert.Null(UpdateFeed.Parse(Release("v1.0.0"), "1.0.0"));
        Assert.Null(UpdateFeed.Parse(Release("v0.9.0"), "1.0.0"));
    }

    [Fact]
    public void DraftsAndPrereleasesAreIgnored()
    {
        Assert.Null(UpdateFeed.Parse(Release("v2.0.0", draft: true), "1.0.0"));
        Assert.Null(UpdateFeed.Parse(Release("v2.0.0", prerelease: true), "1.0.0"));
    }

    [Fact]
    public void AReleaseMissingItsFilesIsAnError()
    {
        var error = Assert.Throws<UpdateException>(() => UpdateFeed.Parse(Release("v2.0.0", assets: false), "1.0.0"));
        Assert.Contains("missing", error.Message);
    }

    [Fact]
    public void ASignatureMadeWithTheKeyVerifiesAndATamperedFileDoesNot()
    {
        var random = new SecureRandom();
        var privateKey = new byte[Ed25519.SecretKeySize];
        random.NextBytes(privateKey);
        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        byte[] data = Encoding.UTF8.GetBytes("pretend this is the release zip");
        var signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(privateKey, 0, data, 0, data.Length, signature, 0);

        string key = Convert.ToBase64String(publicKey), sig = Convert.ToBase64String(signature);
        Assert.True(UpdateFeed.VerifySignature(data, sig, key));
        Assert.True(UpdateFeed.VerifySignature(data, sig + "\n", key + "\n"));      // trailing newlines from files
        byte[] tampered = (byte[])data.Clone();
        tampered[0] ^= 1;
        Assert.False(UpdateFeed.VerifySignature(tampered, sig, key));
        Assert.False(UpdateFeed.VerifySignature(data, sig, Convert.ToBase64String(new byte[32])));      // another key
        Assert.False(UpdateFeed.VerifySignature(data, "not base64!", key));
        Assert.False(UpdateFeed.VerifySignature(data, Convert.ToBase64String(new byte[10]), key));
    }

    [Fact]
    public void TheBuiltInPublicKeyIsAValidEd25519Key()
    {
        Assert.Equal(32, Convert.FromBase64String(UpdateFeed.PublicKey).Length);
    }
}

public class SettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "bismillah-tests-" + Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void DefaultsMatchTheMacApp()
    {
        var s = Settings.Load(TempPath());
        Assert.Equal(0, s.DhikrPerDay);
        Assert.Equal(8, s.DhikrStartHour);
        Assert.Equal(22, s.DhikrEndHour);
        Assert.False(s.ShowOnAllScreens);
        Assert.True(s.UpdateAutomatically);
        Assert.Null(s.LastUpdateCheck);
    }

    [Fact]
    public void ChoicesSurviveARestart()
    {
        string path = TempPath();
        var s = Settings.Load(path);
        s.DhikrPerDay = 10;
        s.ShowOnAllScreens = true;
        s.UpdateAutomatically = false;
        s.LastUpdateCheck = new DateTime(2026, 11, 6, 9, 30, 0);
        s.DhikrPending = [new DateTime(2026, 11, 6, 12, 0, 0)];
        s.Save();
        var again = Settings.Load(path);
        Assert.Equal(10, again.DhikrPerDay);
        Assert.True(again.ShowOnAllScreens);
        Assert.False(again.UpdateAutomatically);
        Assert.Equal(s.LastUpdateCheck, again.LastUpdateCheck);
        Assert.Equal(s.DhikrPending, again.DhikrPending);
    }

    [Fact]
    public void ADamagedFileMeansDefaultsNotACrash()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");
        var s = Settings.Load(path);
        Assert.Equal(0, s.DhikrPerDay);
        s.DhikrPerDay = 3;
        s.Save();                                  // and it can be saved over
        Assert.Equal(3, Settings.Load(path).DhikrPerDay);
    }

    [Fact]
    public void SavingToAnUnwritablePlaceDoesNotThrow()
    {
        var s = Settings.Load(Path.Combine("/proc/definitely/not/writable", "settings.json"));
        s.DhikrPerDay = 1;
        s.Save();
    }
}

public class UpdateSwapTests
{
    private static string Folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bismillah-swap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void TheNewFileReplacesTheOldAndNothingIsLeftBehind()
    {
        string dir = Folder();
        string target = Path.Combine(dir, "Bismillah.exe"), fresh = Path.Combine(dir, "new.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(fresh, "new");
        UpdateSwap.Apply(fresh, target);
        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".old"));
    }

    [Fact]
    public void AMissingNewFileLeavesTheOldOneAlone()
    {
        string dir = Folder();
        string target = Path.Combine(dir, "Bismillah.exe");
        File.WriteAllText(target, "old");
        Assert.Throws<FileNotFoundException>(() => UpdateSwap.Apply(Path.Combine(dir, "nope.exe"), target));
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void ACopyThatFailsPutsTheOldFileBack()
    {
        string dir = Folder();
        string target = Path.Combine(dir, "Bismillah.exe");
        File.WriteAllText(target, "old");
        string fresh = Path.Combine(dir, "new.exe");
        File.WriteAllText(fresh, "new");
        // make the copy fail: the target path is also a directory name that can't be replaced by a file
        string badTarget = Path.Combine(dir, "sub", "Bismillah.exe");
        Assert.ThrowsAny<Exception>(() => UpdateSwap.Apply(fresh, badTarget));
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void WritableFolderDetection()
    {
        Assert.True(UpdateSwap.CanWriteTo(Folder()));
        Assert.False(UpdateSwap.CanWriteTo(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"))));
    }
}
