namespace TheVanillafiyator;

public sealed record ScanEntry(string RelativePath, string Classification, string Reason,
    long Files, long Bytes, bool Complete);
public sealed record ScanResult(List<ScanEntry> Entries, List<string> Issues);

public static class ModScanner
{
    private const string Paks = "Pagoda/Content/Paks/";
    private const string Win64 = "Pagoda/Binaries/Win64/";
    internal const string Ue4ssPath = Win64 + "ue4ss";
    private const string LegacyMods = Win64 + "Mods";

    private static readonly (string Path, string Reason)[] Candidates =
    [
        (Paks + "~mods", "Pak mods, including nested and empty folders."),
        (Paks + "LogicMods", "Blueprint logic mods and their packages."),
        (Ue4ssPath, "UE4SS runtime, installed mods, settings and logs."),
        (Win64 + "UE4SS.dll", "Legacy UE4SS runtime."),
        (Win64 + "UE4SS-settings.ini", "Legacy UE4SS settings."),
        (Win64 + "UE4SS.log", "Legacy UE4SS log."),
        (Win64 + "UE4SS.pdb", "Legacy UE4SS debug symbols."),
        (Win64 + "dwmapi.dll", "UE4SS proxy."),
        (Win64 + "Denali.dll", "UE4SS-related DLL."),
        (".limelight", "Limelight mod data inside the game install.")
    ];

    internal static bool IsCandidatePath(string path) =>
        path == LegacyMods || Candidates.Any(candidate => candidate.Path == path);

    public static ScanResult Scan(string gamePath)
    {
        if (!GameLocation.IsGameFolder(gamePath))
            throw new ArgumentException("The selected game folder is unavailable or incomplete.");

        var entries = new List<ScanEntry>();
        var issues = new List<string>();
        foreach (var candidate in Candidates)
            Add(candidate.Path, "CANDIDATE", candidate.Reason);

        bool legacy = File.Exists(Path.Combine(gamePath, Win64, "UE4SS.dll")) ||
            File.Exists(Path.Combine(gamePath, Win64, "UE4SS-settings.ini"));
        Add(LegacyMods, legacy ? "CANDIDATE" : "REVIEW",
            legacy ? "Legacy UE4SS Mods folder; preserves mods.txt and per-mod settings." :
            "Mods folder without adjacent UE4SS runtime/settings; ownership needs checking.");

        foreach (string name in new[] { "winmm.dll", "version.dll", "dxgi.dll", "dinput8.dll", "xinput1_3.dll" })
            Add(Win64 + name, "REVIEW", "Possible DLL proxy. The filename alone does not establish mod ownership.");

        Add(Win64 + "override.txt", "REVIEW", "May redirect UE4SS to a custom location; the target is not scanned.");

        return new ScanResult(entries, issues);

        void Add(string relative, string classification, string reason)
        {
            string full = Path.GetFullPath(Path.Combine(gamePath, relative));
            try
            {
                // Do not walk through a junction or symbolic link to count another tree.
                for (DirectoryInfo? parent = new DirectoryInfo(Path.GetDirectoryName(full)!);
                     parent is not null; parent = parent.Parent)
                {
                    if (!parent.Exists) return;
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        issues.Add($"{relative}: parent is a link/reparse point; scan skipped.");
                        return;
                    }
                }

                FileAttributes attributes;
                try { attributes = File.GetAttributes(full); }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }

                long files = 0, bytes = 0;
                int issueCount = issues.Count;
                Count(full, attributes);
                bool complete = issues.Count == issueCount;
                entries.Add(new ScanEntry(relative, complete ? classification : "REVIEW", reason, files, bytes, complete));

                void Count(string path, FileAttributes attrs)
                {
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                    {
                        issues.Add($"{Path.GetRelativePath(gamePath, path)}: link/reparse point; not followed.");
                        return;
                    }
                    try
                    {
                        if ((attrs & FileAttributes.Directory) == 0)
                        {
                            bytes += new FileInfo(path).Length;
                            files++;
                            return;
                        }
                        foreach (FileSystemInfo child in new DirectoryInfo(path).EnumerateFileSystemInfos())
                            Count(child.FullName, child.Attributes);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        issues.Add($"{Path.GetRelativePath(gamePath, path)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"{relative}: {ex.Message}");
            }
        }
    }
}
