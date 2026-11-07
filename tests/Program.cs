using TheVanillafiyator;

string fixture = Path.Combine(Path.GetTempPath(), "TheVanillafiyator-check-" + Guid.NewGuid().ToString("N"));
string game = Path.Combine(fixture, "Game");
string settings = Path.Combine(fixture, "Preferences", "settings.json");
Directory.CreateDirectory(Path.Combine(game, "Pagoda/Content/Paks/~mods"));
Directory.CreateDirectory(Path.Combine(game, "Pagoda/Binaries/Win64"));
File.WriteAllText(Path.Combine(game, "Pagoda.exe"), "fixture");
File.WriteAllText(Path.Combine(game, "Pagoda/Binaries/Win64/PagodaSteam-Win64-Shipping.exe"), "fixture");
var location = new GameLocation(settings);
var originalInput = Console.In;
var originalOutput = Console.Out;

void Check(bool success, string description)
{
    if (!success) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}

string Resolve(string input, string? supplied = null)
{
    using var capture = new StringWriter();
    using var reader = new StringReader(input);
    Console.SetIn(reader);
    Console.SetOut(capture);
    try { return location.Resolve(supplied); }
    finally { Console.SetIn(originalInput); Console.SetOut(originalOutput); }
}

Check(Resolve("\"" + game + "\"\n") == game && File.Exists(settings), "First run accepts quoted folder and saves it");
string saved = File.ReadAllText(settings);
DateTime written = File.GetLastWriteTimeUtc(settings);
Check(Resolve("") == game, "Second run uses saved path without input");
Check(File.ReadAllText(settings) == saved && File.GetLastWriteTimeUtc(settings) == written,
    "Saved settings are not rewritten on repeat run");

string relocated = Path.Combine(fixture, "RelocatedGame");
if (!Path.GetFullPath(game).StartsWith(fixture + Path.DirectorySeparatorChar) ||
    !Path.GetFullPath(relocated).StartsWith(fixture + Path.DirectorySeparatorChar))
    throw new Exception("Fixture paths escaped test directory");
Directory.Move(game, relocated);
Check(Resolve("invalid-folder\n" + relocated + "\n") == relocated,
    "Moved install prompts again, rejects wrong folder and remembers replacement");
Check(Resolve("") == relocated, "Replacement is reused on subsequent run");
File.WriteAllText(settings, "{broken");
Check(Resolve(relocated + "\n") == relocated, "Malformed preferences recover through selection");
saved = File.ReadAllText(settings);
bool rejected = false;
try { Resolve("", Path.Combine(fixture, "Missing")); }
catch (ArgumentException) { rejected = true; }
Check(rejected && File.ReadAllText(settings) == saved, "Invalid --game does not replace saved path");
Check(Resolve("", relocated) == relocated, "Valid explicit location is saved");
string renamedSettings = Path.Combine(fixture, "NewPreferences", "settings.json");
location = new GameLocation(renamedSettings, settings);
Check(Resolve("") == relocated && File.Exists(renamedSettings), "Rename preserves and migrates the saved game location without prompting");
Check(File.ReadAllText(settings) == saved, "Migration preserves the previous preferences file");

foreach (string relative in new[] { "Pagoda/Binaries/Win64/Denali.dll", "Pagoda/Binaries/Win64/dwmapi.dll", ".limelight/state.json" })
{
    string target = Path.Combine(relocated, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, "fixture");
}
var scan = ModScanner.Scan(relocated);
Check(scan.Entries.Count(e => e.Classification == "CANDIDATE") == 4 && scan.Issues.Count == 0,
    "Confirmed loader files and Limelight folder are isolation candidates alongside mods");

var storage = new ModStorage(relocated);
string win64 = Path.Combine(relocated, "Pagoda/Binaries/Win64");
string stock = Path.Combine(win64, "tbb12.dll");
string modFile = Path.Combine(win64, "Denali.dll");
string emptyModFolder = Path.Combine(relocated, "Pagoda/Content/Paks/~mods/Empty");
string nestedModFile = Path.Combine(relocated, "Pagoda/Content/Paks/~mods/Nested/Costume.pak");
Directory.CreateDirectory(emptyModFolder);
Directory.CreateDirectory(Path.GetDirectoryName(nestedModFile)!);
File.WriteAllBytes(nestedModFile, [0, 17, 34, 255]);
File.WriteAllText(stock, "stock");

Check(storage.Isolate() == 4 && storage.StoredPaths().Count == 4 &&
    ModScanner.Scan(relocated).Entries.Count == 0 && File.ReadAllText(stock) == "stock",
    "Isolation moves recognized paths outside the game and leaves stock files in place");
Check(Directory.Exists(Path.Combine(storage.Folder, "mods/Pagoda/Content/Paks/~mods/Empty")) &&
    File.ReadAllText(Path.Combine(storage.Folder, "mods/Pagoda/Binaries/Win64/Denali.dll")) == "fixture",
    "Storage keeps original layout, empty folders and content");

void ExpectBlocked(Action action)
{
    try { action(); }
    catch (IOException) { return; }
    throw new Exception("Expected move to be blocked.");
}

ExpectBlocked(() => storage.Isolate());
File.WriteAllText(modFile, "conflict");
ExpectBlocked(() => storage.Restore());
Check(File.ReadAllText(modFile) == "conflict" && !Directory.Exists(emptyModFolder) && storage.StoredPaths().Count == 4,
    "Restore conflict blocks the entire batch without overwriting anything");
File.Move(modFile, Path.Combine(fixture, "Conflict.dll"));
Check(storage.Restore() == 4 && storage.StoredPaths().Count == 0 && Directory.Exists(emptyModFolder) &&
    File.ReadAllBytes(nestedModFile).SequenceEqual(new byte[] { 0, 17, 34, 255 }) && File.ReadAllText(modFile) == "fixture",
    "Restore returns file bytes and empty folders to their exact paths");

using (var held = File.Open(modFile, FileMode.Open, FileAccess.Read, FileShare.Read))
    ExpectBlocked(() => storage.Isolate());
Check(storage.StoredPaths().Count == 0 && Directory.Exists(emptyModFolder) && File.ReadAllText(modFile) == "fixture",
    "A locked DLL stops isolation and earlier moves are automatically undone");

string cliSettings = Path.Combine(fixture, "CliSettings");
(int Code, string Output) RunCli(params string[] args)
{
    using var capture = new StringWriter();
    Console.SetOut(capture);
    try { return (CommandLine.Run(args, ConsoleUi.Plain, cliSettings), capture.ToString()); }
    finally { Console.SetOut(originalOutput); }
}

var isolated = RunCli("isolate", "--game", relocated);
Check(isolated.Code == 0 && storage.StoredPaths().Count == 4 && !Directory.Exists(emptyModFolder),
    "CLI isolate parks mods and remembers the install");
// Recreate the mixed on-disk state left by a v1 interrupted move.
File.Move(Path.Combine(storage.Folder, "mods/Pagoda/Binaries/Win64/Denali.dll"), modFile);
var pending = RunCli("scan");
Check(pending.Code == 0 && pending.Output.Contains("3 of 4 paths in storage") &&
    pending.Output.Contains("[IN GAME] Pagoda/Binaries/Win64/Denali.dll"),
    "CLI scan shows physical locations for an existing partial restore record");
var restored = RunCli("restore");
Check(restored.Code == 0 && restored.Output.Contains("Restored 3 mod locations") && storage.StoredPaths().Count == 0 &&
    Directory.Exists(emptyModFolder) && File.ReadAllText(modFile) == "fixture",
    "CLI restores just the stored subset of an existing partial move");

using (var menuInput = new StringReader("9\n1\n2\n2\n3\n0\n"))
{
    Console.SetIn(menuInput);
    try
    {
        var menu = RunCli();
        Check(menu.Code == 0 && menu.Output.Contains("Enter 1, 2, 3, or 0.") &&
            menu.Output.Contains("4 candidates") && menu.Output.Contains("Moved 4 mod locations") &&
            menu.Output.Contains("There is already a saved move") && menu.Output.Contains("Restored 4 mod locations") &&
            storage.StoredPaths().Count == 0 && File.Exists(modFile),
            "Numbered menu scans, isolates, recovers from a failed choice, and restores in one session");
    }
    finally { Console.SetIn(originalInput); }
}

string ue4ss = Path.Combine(win64, "ue4ss");
string ue4ssMods = Path.Combine(ue4ss, "Mods");
Directory.CreateDirectory(Path.Combine(ue4ssMods, "Empty"));
File.WriteAllText(Path.Combine(ue4ss, "UE4SS.dll"), "loader");
File.WriteAllText(Path.Combine(ue4ssMods, "mods.txt"), "enabled");
string probe = Path.Combine(fixture, "WholeUe4ssMoveProbe");
using (var heldDirectory = DirectoryHandle.Open(ue4ssMods))
{
    bool denied = false;
    try { Directory.Move(ue4ss, probe); }
    catch (IOException ex) when (ex.HResult == unchecked((int)0x80070005)) { denied = true; }
    finally { if (Directory.Exists(probe)) Directory.Move(probe, ue4ss); }
    Check(denied, "An open child directory reproduces access denied when moving the UE4SS parent");

    Check(storage.Isolate() == 5 && !Directory.Exists(ue4ss) &&
        storage.InspectRestore().Count == 6 && storage.InspectRestore().All(path => path.InStorage && !path.InGame),
        "Isolate moves the open Mods directory separately and then moves the UE4SS parent");
    Check(storage.Restore() == 5 && storage.StoredPaths().Count == 0 &&
        File.ReadAllText(Path.Combine(ue4ss, "UE4SS.dll")) == "loader" &&
        File.ReadAllText(Path.Combine(ue4ssMods, "mods.txt")) == "enabled" &&
        Directory.Exists(Path.Combine(ue4ssMods, "Empty")),
        "Restore puts the UE4SS parent and open Mods directory back at their original paths");
}

Console.WriteLine("All 23 checks passed.");
Console.WriteLine("Fixture: " + fixture);

internal static class DirectoryHandle
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string path, uint access, uint share, nint security, uint mode, uint flags, nint template);

    public static Microsoft.Win32.SafeHandles.SafeFileHandle Open(string path)
    {
        // List-directory access with delete sharing, like an Explorer directory handle.
        var handle = CreateFile(path, 1, 7, 0, 3, 0x02000000, 0);
        if (!handle.IsInvalid) return handle;
        int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new System.ComponentModel.Win32Exception(error);
    }
}
