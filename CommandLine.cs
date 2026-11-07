namespace TheVanillafiyator;

internal static class CommandLine
{
    public static int Run(string[] args, ConsoleUi ui, string appData)
    {
        try
        {
            string? command = null;
            string? suppliedPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "scan" or "isolate" or "restore" when i == 0:
                        command = args[i];
                        break;
                    case "--game" when i + 1 < args.Length && suppliedPath is null:
                        suppliedPath = args[++i];
                        break;
                    case "--help" or "-h":
                        ui.Banner();
                        ui.Info("TheVanillafiyator [scan | isolate | restore] [--game \"game folder\"]");
                        ui.Note("Run without a command for the numbered menu.");
                        ui.Note("  scan     List recognized mod locations and pending restores.");
                        ui.Note("  isolate  Move recognized mods into storage beside the game folder.");
                        ui.Note("  restore  Put stored mods back in their original locations.");
                        ui.Note("The first run asks for the game folder. Use --game to change it.");
                        return 0;
                    default:
                        throw new ArgumentException($"Unknown or incomplete argument: {args[i]}. Use --help.");
                }
            }

            ui.Banner();
            var location = new GameLocation(Path.Combine(appData, "TheVanillafiyator", "settings.json"),
                Path.Combine(appData, "LimelightVanilla", "settings.json"), ui);
            string gamePath = location.Resolve(suppliedPath);
            var storage = new ModStorage(gamePath);
            ui.Info($"Game: {gamePath}");
            ui.Info($"Storage: {storage.Folder}");
            ui.Blank();

            return command is null ? RunMenu(gamePath, storage, ui) : Execute(command, gamePath, storage, ui);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Stopped: {ex.Message}");
            return 1;
        }
    }

    private static int RunMenu(string gamePath, ModStorage storage, ConsoleUi ui)
    {
        while (true)
        {
            ui.Heading("CHOOSE AN ACTION");
            ui.Accent("  1  Scan");
            ui.Accent("  2  Isolate mods");
            ui.Accent("  3  Restore mods");
            ui.Note("  0  Exit");
            ui.Prompt("Option");
            string? choice = Console.ReadLine()?.Trim();
            if (choice is null or "0") return 0;

            string? command = choice switch
            {
                "1" => "scan",
                "2" => "isolate",
                "3" => "restore",
                _ => null
            };
            if (command is null)
            {
                ui.Warning("Enter 1, 2, 3, or 0.");
                ui.Blank();
                continue;
            }

            ui.Blank();
            try { Execute(command, gamePath, storage, ui); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ui.Warning($"Stopped: {ex.Message}");
            }
            ui.Blank();
        }
    }

    private static int Execute(string command, string gamePath, ModStorage storage, ConsoleUi ui)
    {
        switch (command)
        {
            case "isolate":
                ui.Heading("ISOLATE MODS");
                int moved = storage.Isolate();
                ui.Accent(moved == 0 ? "No recognized mod locations to move." : $"Moved {moved} mod locations to storage. Choose 3 or run restore to put them back.");
                return 0;
            case "restore":
                ui.Heading("RESTORE MODS");
                int restored = storage.Restore();
                ui.Accent(restored == 0 ? "No stored mod locations needed moving." : $"Restored {restored} mod locations to the game folder.");
                return 0;
            default:
                return Scan(gamePath, storage, ui);
        }
    }

    private static int Scan(string gamePath, ModStorage storage, ConsoleUi ui)
    {
        ui.Heading("SCAN");
        ScanResult result = ModScanner.Scan(gamePath);
        foreach (ScanEntry entry in result.Entries) ui.Entry(entry);
        if (result.Entries.Count == 0) ui.Note("No recognized mod locations in the game folder.");
        foreach (string issue in result.Issues) ui.Warning($"[REVIEW] {issue}");
        ui.Summary(result);

        IReadOnlyList<RestorePathStatus> pending = storage.InspectRestore();
        if (pending.Count == 0)
        {
            ui.Note("Choose 2 or run isolate to move the candidate locations. Scan covers known mod locations.");
            return result.Issues.Count == 0 ? 0 : 2;
        }

        ui.Blank();
        ui.Heading($"RESTORE PENDING / {pending.Count(path => path.InStorage)} of {pending.Count} paths in storage");
        bool problem = false;
        foreach (RestorePathStatus path in pending)
        {
            switch (path.InGame, path.InStorage)
            {
                case (true, false):
                    ui.Note($"[IN GAME] {path.RelativePath}");
                    break;
                case (false, true):
                    ui.Accent($"[STORED]  {path.RelativePath}");
                    break;
                case (true, true):
                    ui.Warning($"[CONFLICT] {path.RelativePath} exists in both locations.");
                    problem = true;
                    break;
                case (false, false):
                    ui.Warning($"[MISSING] {path.RelativePath} is absent from both locations.");
                    problem = true;
                    break;
            }
        }
        ui.Note(problem ? "Resolve the marked paths before running restore." : "Choose 3 or run restore to put the stored paths back.");
        return problem || result.Issues.Count > 0 ? 2 : 0;
    }
}
