using System.Runtime.InteropServices;
using Com0com.Core;

namespace Com0com.Setupc;

/// <summary>
/// UI of the original setup: traces go to the console and (with --output) are appended to a file,
/// questions are Windows message boxes (MB_SETFOREGROUND), no answer in --silent mode.
/// </summary>
internal sealed class LegacyUi : ISetupUi
{
    public bool Silent { get; set; }
    public string? OutputFile { get; set; }
    public string Title { get; set; } = "Setup for NewVirtualCOMPair";

    /// <summary>Console only (help text, "Invalid command"), like the original ConsoleWrite().</summary>
    public static void ConsoleWrite(string text)
    {
        Console.Out.Write(text);
        Console.Out.Flush();
    }

    /// <summary>Trace(): console plus the --output file (with CR LF line ends).</summary>
    public void Write(string text)
    {
        ConsoleWrite(text);
        if (OutputFile == null) return;
        try
        {
            File.AppendAllText(OutputFile, text.ReplaceLineEndings("\r\n"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original ignores output file errors too.
        }
    }

    public Choice Ask(string message, Buttons buttons, bool warning = false)
    {
        if (Silent) return Choice.None;

        uint type = buttons switch
        {
            Buttons.Ok => MB_OK | (warning ? MB_ICONWARNING : MB_ICONSTOP),
            Buttons.OkCancel => MB_OKCANCEL | MB_ICONWARNING,
            Buttons.YesNo => MB_YESNO,
            Buttons.RetryCancel => MB_RETRYCANCEL,
            _ => MB_CANCELTRYCONTINUE | (warning ? MB_ICONWARNING : 0),
        };
        return MessageBoxW(IntPtr.Zero, message + "\n", Title, type | MB_SETFOREGROUND) switch
        {
            IDOK => Choice.Ok,
            IDCANCEL => Choice.Cancel,
            IDRETRY => Choice.Retry,
            IDYES => Choice.Yes,
            IDNO => Choice.No,
            IDTRYAGAIN => Choice.TryAgain,
            IDCONTINUE => Choice.Continue,
            _ => Choice.None,
        };
    }

    private const uint MB_OK = 0x0, MB_OKCANCEL = 0x1, MB_YESNO = 0x4, MB_RETRYCANCEL = 0x5, MB_CANCELTRYCONTINUE = 0x6;
    private const uint MB_ICONSTOP = 0x10, MB_ICONWARNING = 0x30, MB_SETFOREGROUND = 0x10000;
    private const int IDOK = 1, IDCANCEL = 2, IDRETRY = 4, IDYES = 6, IDNO = 7, IDTRYAGAIN = 10, IDCONTINUE = 11;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
