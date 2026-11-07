using System.Text.Json;

namespace TheVanillafiyator;

public sealed class GameLocation(string settingsPath, string? previousSettingsPath = null, ConsoleUi? ui = null)
{
    private readonly ConsoleUi output = ui ?? ConsoleUi.Plain;

    public string Resolve(string? suppliedPath)
    {
        if (suppliedPath is not null)
        {
            string supplied = Normalize(suppliedPath);
            if (!IsGameFolder(supplied))
                throw new ArgumentException("That folder is not a complete Dead as Disco install. Choose the folder containing Pagoda.exe and Pagoda.");
            Save(supplied);
            return supplied;
        }

        string? saved = Load();
        if (saved is not null)
        {
            if (IsGameFolder(saved))
            {
                if (!File.Exists(settingsPath)) Save(saved);
                output.Note("Using saved game folder.");
                return saved;
            }
            output.Warning($"The saved game folder is unavailable or incomplete: {saved}");
            output.Note("If the drive is disconnected, reconnect it; otherwise enter the new location.");
        }

        while (true)
        {
            output.Prompt("Game install folder (the folder containing Pagoda.exe):");
            string? input = Console.ReadLine();
            if (input is null)
                throw new ArgumentException("No game folder provided. Run interactively or use --game \"game folder\".");
            string path;
            try { path = Normalize(input); }
            catch (ArgumentException)
            {
                output.Warning("Please enter a valid folder path.");
                continue;
            }
            if (!IsGameFolder(path))
            {
                output.Warning("Game not found there. Expected Pagoda.exe, Pagoda\\Content\\Paks and the shipping executable.");
                continue;
            }
            Save(path);
            return path;
        }
    }

    public static bool IsGameFolder(string path) =>
        Path.IsPathFullyQualified(path) &&
        File.Exists(Path.Combine(path, "Pagoda.exe")) &&
        File.Exists(Path.Combine(path, "Pagoda", "Binaries", "Win64", "PagodaSteam-Win64-Shipping.exe")) &&
        Directory.Exists(Path.Combine(path, "Pagoda", "Content", "Paks"));

    public string? GetSavedGame()
    {
        string? saved = Load();
        return saved is not null && IsGameFolder(saved) ? saved : null;
    }

    private static string Normalize(string path)
    {
        path = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The game folder cannot be blank.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
    }

    private string? Load()
    {
        string? source = File.Exists(settingsPath) ? settingsPath : previousSettingsPath;
        if (source is null || !File.Exists(source)) return null;
        try
        {
            var settings = JsonSerializer.Deserialize<SavedLocation>(File.ReadAllText(source));
            return string.IsNullOrWhiteSpace(settings?.GameDirectory) ? null : settings.GameDirectory;
        }
        catch (JsonException)
        {
            output.Warning("Saved settings could not be read. Please select the game folder again.");
            return null;
        }
    }

    private void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
        string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                new SavedLocation(path), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        output.Note($"Game folder remembered in {settingsPath}");
    }

    private sealed record SavedLocation(string GameDirectory);
}
