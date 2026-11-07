using System.Runtime.InteropServices;

namespace TheVanillafiyator;

public sealed class ConsoleUi : IDisposable
{
    private const string Reset = "\u001b[0m";
    private const string ResetForeground = "\u001b[39m";
    private const string Pink = "\u001b[38;2;255;60;172m";
    private const string Blue = "\u001b[38;2;53;231;255m";
    private readonly bool color;
    private readonly nint handle;
    private readonly uint originalMode;

    public static ConsoleUi Plain { get; } = new(false);

    private ConsoleUi(bool color, nint handle = default, uint originalMode = 0)
    {
        this.color = color;
        this.handle = handle;
        this.originalMode = originalMode;
    }

    public static ConsoleUi Create()
    {
        if (Console.IsOutputRedirected || Environment.GetEnvironmentVariable("NO_COLOR") is not null ||
            Environment.GetEnvironmentVariable("TERM") == "dumb")
            return Plain;

        if (!OperatingSystem.IsWindows()) return new(true);
        nint output = GetStdHandle(-11);
        if (!GetConsoleMode(output, out uint mode) || !SetConsoleMode(output, mode | 0x0004))
            return Plain;
        return new(true, output, mode);
    }

    public void Banner()
    {
        Heading("THE VANILLAFIYATOR");
        Accent("LIMELIGHT / DEAD AS DISCO");
        Blank();
    }

    public void Heading(string text) => Line(text, Pink);
    public void Accent(string text) => Line(text, Blue);
    public void Note(string text) => Line(text);
    public void Info(string text) => Line(text);
    public void Warning(string text) => Line(text, Pink);
    public void Blank() => Line();

    public void Prompt(string text)
    {
        Console.Write(color ? $"{Blue}> {text}{ResetForeground} " : $"> {text} ");
    }

    public void Entry(ScanEntry entry)
    {
        Line($"[{entry.Classification}]  {entry.RelativePath}",
            entry.Classification == "CANDIDATE" ? Blue : Pink);
        Note($"  {entry.Files:N0} {(entry.Files == 1 ? "file" : "files")} / {Size(entry.Bytes)}{(entry.Complete ? " / " : " / partial count / ")}{entry.Reason}");
    }

    public void Summary(ScanResult result)
    {
        int candidates = result.Entries.Count(e => e.Classification == "CANDIDATE");
        int reviews = result.Entries.Count(e => e.Classification == "REVIEW");
        Blank();
        Line($"{candidates} candidates / {reviews} for review / {result.Issues.Count} scan issues",
            reviews == 0 && result.Issues.Count == 0 ? Blue : Pink);
    }

    private void Line(string text = "", string? foreground = null) =>
        Console.WriteLine(color && foreground is not null ? foreground + text + ResetForeground : text);

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):N1} GiB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):N1} MiB",
        >= 1L << 10 => $"{bytes / 1024d:N1} KiB",
        _ => $"{bytes:N0} bytes"
    };

    public void Dispose()
    {
        if (color && handle != default) SetConsoleMode(handle, originalMode);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int standardHandle);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint consoleHandle, out uint mode);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint consoleHandle, uint mode);
}
