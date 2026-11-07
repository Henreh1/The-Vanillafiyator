using System.Diagnostics;
using System.Text.Json;

namespace TheVanillafiyator;

public sealed record RestorePathStatus(string RelativePath, bool InGame, bool InStorage);

public sealed class ModStorage
{
    private const string Ue4ssMods = ModScanner.Ue4ssPath + "/Mods";
    private const string StoredUe4ssMods = ModScanner.Ue4ssPath + ".Mods";
    private readonly string game;
    private readonly string manifestPath;
    private readonly string payload;

    public string Folder { get; }

    public ModStorage(string gamePath)
    {
        game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
        if (Path.GetDirectoryName(game) is null)
            throw new ArgumentException("Choose the game folder, rather than the root of a drive.");
        Folder = game + ".vanillafiyator";
        manifestPath = Path.Combine(Folder, "restore.json");
        payload = Path.Combine(Folder, "mods");
    }

    public IReadOnlyList<string> StoredPaths() =>
        ReadManifest()?.Entries.Select(entry => entry.RelativePath).ToArray() ?? [];

    public IReadOnlyList<RestorePathStatus> InspectRestore() =>
        ReadManifest()?.Entries.Select(entry => new RestorePathStatus(entry.RelativePath,
            Attributes(Under(game, entry.RelativePath)).HasValue,
            Attributes(StoredPath(entry)).HasValue)).ToArray() ?? [];

    public int Isolate()
    {
        CheckGameClosed();
        using var storageLock = Lock();
        if (ReadManifest() is not null)
            throw new IOException("There is already a saved move. Choose 3 or run restore before isolating again.");

        ScanResult scan = ModScanner.Scan(game);
        if (scan.Issues.Count > 0)
            throw new IOException($"Resolve the scan issue before moving files. {scan.Issues[0]}");
        var entries = scan.Entries.Where(entry => entry.Classification == "CANDIDATE")
            .Select(entry => new SavedEntry(entry.RelativePath,
                (Attributes(Under(game, entry.RelativePath)) ?? throw new IOException($"Path disappeared: {entry.RelativePath}"))
                .HasFlag(FileAttributes.Directory)))
            .ToList();
        if (entries.Count == 0) return 0;

        int ue4ssIndex = entries.FindIndex(entry => entry.RelativePath == ModScanner.Ue4ssPath && entry.Directory);
        bool splitUe4ss = ue4ssIndex >= 0 &&
            Attributes(Under(game, Ue4ssMods))?.HasFlag(FileAttributes.Directory) == true;
        if (splitUe4ss)
        {
            // Explorer can allow renaming Mods itself while Windows refuses
            // to rename its parent with that descendant directory open.
            entries.Insert(ue4ssIndex, new SavedEntry(Ue4ssMods, true));
        }

        foreach (SavedEntry entry in entries)
            CheckMove(entry, restore: false);

        var manifest = new RestoreManifest(splitUe4ss ? 2 : 1, game, entries.ToArray());
        WriteManifest(manifest);
        try
        {
            foreach (SavedEntry entry in entries)
            {
                if (splitUe4ss && entry.RelativePath == ModScanner.Ue4ssPath && Attributes(Under(game, Ue4ssMods)).HasValue)
                    throw new IOException("UE4SS Mods reappeared during isolation. Close apps using it before retrying.");
                Move(entry, restore: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                CheckGameClosed();
                RestoreBatch(manifest);
            }
            catch (Exception recovery) when (recovery is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Move interrupted. {ex.Message}{Environment.NewLine}" +
                    $"Automatic restore stopped: {recovery.Message}{Environment.NewLine}" +
                    "The restore record was kept. Resolve the error and run restore.", ex);
            }
            throw new IOException($"Isolation stopped. Completed moves were undone. {ex.Message}", ex);
        }
        return CountLocations(entries);
    }

    public int Restore()
    {
        CheckGameClosed();
        using var storageLock = Lock();
        RestoreManifest? manifest = ReadManifest();
        return manifest is null ? 0 : RestoreBatch(manifest);
    }

    private int RestoreBatch(RestoreManifest manifest)
    {
        // Check the entire batch before restoring the first path. Paths already
        // at home also cover moves interrupted before isolation finished.
        if (manifest.Version == 2 && Attributes(Under(payload, Ue4ssMods)).HasValue)
            throw new IOException("The stored UE4SS directory already contains a Mods path. Move the conflicting item elsewhere before retrying.");
        var toRestore = manifest.Entries
            .OrderBy(entry => entry.RelativePath == Ue4ssMods)
            .Where(entry => CheckMove(entry, restore: true))
            .ToArray();
        foreach (SavedEntry entry in toRestore)
            Move(entry, restore: true);

        File.Delete(manifestPath);
        return CountLocations(toRestore);
    }

    private static int CountLocations(IEnumerable<SavedEntry> entries) =>
        entries.Select(entry => entry.RelativePath == Ue4ssMods ? ModScanner.Ue4ssPath : entry.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private string StoredPath(SavedEntry entry) =>
        Under(payload, entry.RelativePath == Ue4ssMods ? StoredUe4ssMods : entry.RelativePath);

    private bool CheckMove(SavedEntry entry, bool restore)
    {
        string original = Under(game, entry.RelativePath);
        string stored = StoredPath(entry);
        FileAttributes? originalAttributes = Attributes(original);
        FileAttributes? storedAttributes = Attributes(stored);
        if (originalAttributes.HasValue && storedAttributes.HasValue)
            throw new IOException($"Both locations contain {entry.RelativePath}. Move the conflicting item elsewhere before retrying.");
        if (!originalAttributes.HasValue && !storedAttributes.HasValue)
            throw new IOException($"Missing from both locations: {entry.RelativePath}");
        if ((originalAttributes ?? storedAttributes)!.Value.HasFlag(FileAttributes.Directory) != entry.Directory)
            throw new IOException($"File or folder type changed: {entry.RelativePath}");
        if (!restore && !originalAttributes.HasValue)
            throw new IOException($"Storage already contains {entry.RelativePath}. Keep it somewhere safe before retrying.");
        return !restore || storedAttributes.HasValue;
    }

    private void Move(SavedEntry entry, bool restore)
    {
        if (!CheckMove(entry, restore)) return;
        string original = Under(game, entry.RelativePath);
        string stored = StoredPath(entry);
        string from = restore ? stored : original;
        string to = restore ? original : stored;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (entry.Directory) Directory.Move(from, to);
            else File.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not move {entry.RelativePath}. {ex.Message}", ex);
        }
    }

    private static string Under(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Path escapes its folder: {relative}");
        return full;
    }

    private static FileAttributes? Attributes(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is not null)
        {
            FileAttributes? parentAttributes = Attributes(parent);
            if (!parentAttributes.HasValue) return null;
            if (!parentAttributes.Value.HasFlag(FileAttributes.Directory))
                throw new IOException($"Expected a folder at {parent}");
        }

        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"Linked paths cannot be moved or used as storage: {path}");
        return attributes;
    }

    private FileStream Lock()
    {
        Attributes(Folder);
        Directory.CreateDirectory(Folder);
        string lockPath = Path.Combine(Folder, "operation.lock");
        Attributes(lockPath);
        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private RestoreManifest? ReadManifest()
    {
        if (Attributes(manifestPath) is null) return null;
        RestoreManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<RestoreManifest>(File.ReadAllText(manifestPath)); }
        catch (JsonException ex) { throw new IOException($"The restore file could not be read: {manifestPath}", ex); }
        if (manifest is null || manifest.Version is not (1 or 2) ||
            !string.Equals(manifest.GamePath, game, StringComparison.OrdinalIgnoreCase) ||
            manifest.Entries is not { Length: > 0 } ||
            manifest.Entries.Any(entry => entry is null || !(ModScanner.IsCandidatePath(entry.RelativePath) ||
                (manifest.Version == 2 && entry.RelativePath == Ue4ssMods && entry.Directory))) ||
            (manifest.Version == 2 && (!manifest.Entries.Any(entry => entry.RelativePath == Ue4ssMods) ||
                !manifest.Entries.Any(entry => entry.RelativePath == ModScanner.Ue4ssPath && entry.Directory))) ||
            manifest.Entries.Select(entry => entry.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Entries.Length)
            throw new IOException($"The restore file does not match this install or contains invalid paths: {manifestPath}");
        return manifest;
    }

    private void WriteManifest(RestoreManifest manifest)
    {
        string temporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, manifestPath);
    }

    private void CheckGameClosed()
    {
        if (!GameLocation.IsGameFolder(game))
            throw new IOException("The saved game folder is unavailable or incomplete.");
        Attributes(game);
        foreach (string name in new[] { "Pagoda", "PagodaSteam-Win64-Shipping" })
        {
            Process[] processes = Process.GetProcessesByName(name);
            foreach (Process process in processes) process.Dispose();
            if (processes.Length > 0)
                throw new IOException("Close Dead as Disco before moving or restoring mods.");
        }
    }

    private sealed record RestoreManifest(int Version, string GamePath, SavedEntry[] Entries);
    private sealed record SavedEntry(string RelativePath, bool Directory);
}
