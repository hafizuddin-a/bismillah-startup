namespace Bismillah;

/// <summary>
/// Puts a new copy of the app in place of the installed one. Windows lets a running exe be renamed but not
/// overwritten, so the old file is moved aside first and moved back if anything goes wrong.
/// </summary>
public static class UpdateSwap
{
    public static void Apply(string newExe, string targetExe)
    {
        if (!File.Exists(newExe)) throw new FileNotFoundException("The new version is missing.", newExe);
        string aside = targetExe + ".old";
        bool movedAside = false;
        try
        {
            if (File.Exists(aside)) File.Delete(aside);
            if (File.Exists(targetExe)) { File.Move(targetExe, aside); movedAside = true; }
            File.Copy(newExe, targetExe, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(targetExe)) File.Delete(targetExe);
                if (movedAside) File.Move(aside, targetExe);
            }
            catch { /* nothing more can be done */ }
            throw;
        }
        try { File.Delete(aside); } catch { /* still in use; the next update removes it */ }
    }

    /// <summary>True when files can be created in `folder`, which in-place updating needs.</summary>
    public static bool CanWriteTo(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
