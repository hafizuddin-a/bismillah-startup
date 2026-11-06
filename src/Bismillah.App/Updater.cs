using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;

namespace Bismillah;

/// <summary>
/// Keeps Bismillah up to date from this project's GitHub releases (see <see cref="UpdateFeed"/> for how they are
/// trusted). Installing means: download, check the signature, unpack, check it is really Bismillah at the announced
/// version, then start the new exe in "apply" mode, which waits for this process to exit, swaps itself in and reopens.
/// </summary>
internal sealed class Updater
{
    private static readonly TimeSpan MinimumGap = TimeSpan.FromDays(7), RecheckEvery = TimeSpan.FromHours(6);
    private static readonly HttpClient Http = CreateClient();

    private readonly Settings _settings;
    private readonly Func<bool> _isPopupShowing;
    private readonly Action _quit;
    private readonly DispatcherTimer _timer = new() { Interval = RecheckEvery };
    private bool _working;

    public Updater(Settings settings, Func<bool> isPopupShowing, Action quit)
    {
        _settings = settings;
        _isPopupShowing = isPopupShowing;
        _quit = quit;
    }

    public static string CurrentVersion
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version;
            return v is null ? "0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>BISMILLAH_FEED_URL only exists for testing against a local server; updates still need a valid signature.</summary>
    private static Uri FeedUrl =>
        Uri.TryCreate(Environment.GetEnvironmentVariable("BISMILLAH_FEED_URL"), UriKind.Absolute, out var url) ? url : UpdateFeed.DefaultFeed;

    private static string PublicKey => Environment.GetEnvironmentVariable("BISMILLAH_UPDATE_KEY") ?? UpdateFeed.PublicKey;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Bismillah/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // MARK: automatic

    /// <summary>Looks shortly after launch if a week has passed since the last check, and again later if the app stays open.</summary>
    public void StartAutomaticChecks()
    {
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        first.Tick += (_, _) => { first.Stop(); CheckIfDue(); };
        first.Start();
        _timer.Tick += (_, _) => CheckIfDue();
        _timer.Start();
    }

    /// <summary>Checks (and installs) only if automatic updates are on and the last check was over a week ago.</summary>
    public void CheckIfDue()
    {
        if (!_settings.UpdateAutomatically || _working) return;
        if (_settings.LastUpdateCheck is { } last && DateTime.Now - last < MinimumGap) return;
        CheckNow(waitForQuietMoment: true);
    }

    /// <summary>The automatic path, right now (also used by the end-to-end update test).</summary>
    public async void CheckNow(bool waitForQuietMoment = false)
    {
        if (_working) return;
        _working = true;
        try
        {
            _settings.LastUpdateCheck = DateTime.Now;
            _settings.Save();
            var release = await LatestReleaseAsync();
            if (release is not null) await InstallAsync(release, waitForQuietMoment);
        }
        catch (Exception e)
        {
            // quiet failure: it tries again next week
            Log.Write("automatic update failed: " + e.Message);
        }
        finally { _working = false; }
    }

    // MARK: manual

    /// <summary>"Check for Updates…" in the menu: always asks GitHub and tells you what it found.</summary>
    public async void CheckManually()
    {
        if (_working) return;
        _working = true;
        ReleaseInfo? found = null;
        try
        {
            _settings.LastUpdateCheck = DateTime.Now;
            _settings.Save();
            found = await LatestReleaseAsync();
            if (found is null)
            {
                MessageBox.Show($"You have the latest version ({CurrentVersion}).", "Bismillah is up to date", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var answer = MessageBox.Show($"Bismillah {found.Version} is available. You have {CurrentVersion}.\n\nBismillah will restart to finish updating.",
                $"Bismillah {found.Version} is available", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (answer != MessageBoxResult.OK) return;
            await InstallAsync(found, waitForQuietMoment: false);
        }
        catch (UpdateException e) when (e.Message.StartsWith("CANNOT_REPLACE:"))
        {
            var answer = MessageBox.Show(e.Message["CANNOT_REPLACE:".Length..] + "\n\nOpen the release page instead?", "Can't update in place",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes && found is not null) Process.Start(new ProcessStartInfo(found.Page.ToString()) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Couldn't update Bismillah", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _working = false; }
    }

    // MARK: finding and installing

    private async Task<ReleaseInfo?> LatestReleaseAsync()
    {
        string json = await Http.GetStringAsync(FeedUrl);
        return UpdateFeed.Parse(json, CurrentVersion);
    }

    private async Task InstallAsync(ReleaseInfo release, bool waitForQuietMoment)
    {
        string target = Environment.ProcessPath ?? throw new UpdateException("Can't find where Bismillah is installed.");
        string folder = Path.GetDirectoryName(target)!;
        if (!UpdateSwap.CanWriteTo(folder))
            throw new UpdateException($"CANNOT_REPLACE:Bismillah can't change files in {folder}. Download the new version from the release page instead.");

        string work = Path.Combine(Path.GetTempPath(), "bismillah-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string zipPath = Path.Combine(work, "update.zip");
            await using (var download = await Http.GetStreamAsync(release.Zip))
            await using (var file = File.Create(zipPath))
                await download.CopyToAsync(file);

            string signature = await Http.GetStringAsync(release.Signature);
            if (!UpdateFeed.VerifySignature(await File.ReadAllBytesAsync(zipPath), signature, PublicKey))
                throw new UpdateException("The download's signature didn't match, so it was not installed.");

            string unpacked = Path.Combine(work, "unpacked");
            ZipFile.ExtractToDirectory(zipPath, unpacked);
            string newExe = Path.Combine(unpacked, "Bismillah.exe");
            if (!File.Exists(newExe)) throw new UpdateException("The download wasn't a valid Bismillah update (no Bismillah.exe inside).");
            string? packaged = FileVersionInfo.GetVersionInfo(newExe).FileVersion;
            if (packaged is null || !Version.TryParse(packaged, out var v) || $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" != release.Version)
                throw new UpdateException($"The download wasn't a valid Bismillah update (version {packaged ?? "?"} instead of {release.Version}).");

            // don't restart while a popup is on screen
            while (waitForQuietMoment && _isPopupShowing()) await Task.Delay(3000);

            Process.Start(new ProcessStartInfo(newExe)
            {
                ArgumentList = { "--apply-update", Environment.ProcessId.ToString(), target, work },
                UseShellExecute = false,
            });
            _quit();
        }
        catch
        {
            try { Directory.Delete(work, recursive: true); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Runs in the new exe, started by the old one: waits for it to exit, swaps itself in over the installed copy,
    /// opens the installed copy quietly, and tidies up. Returns the exit code.
    /// </summary>
    public static int ApplyUpdate(int oldProcessId, string target, string work)
    {
        try
        {
            using (var old = Process.GetProcessById(oldProcessId)) old.WaitForExit(30_000);
        }
        catch (ArgumentException) { /* already gone */ }

        try
        {
            UpdateSwap.Apply(Environment.ProcessPath!, target);
        }
        catch (Exception e)
        {
            // couldn't swap: the old version is put back and reopened below, rather than leaving nothing running
            Log.Write("couldn't apply the update: " + e.Message);
        }
        Process.Start(new ProcessStartInfo(target) { ArgumentList = { "--after-update" }, UseShellExecute = false });
        // this exe is still running from the scratch folder, so a short-lived helper removes it afterwards
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 6 127.0.0.1 >nul & rd /s /q \"{work}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
        });
        return 0;
    }
}
