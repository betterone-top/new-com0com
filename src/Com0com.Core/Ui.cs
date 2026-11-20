using System.ComponentModel;

namespace Com0com.Core;

/// <summary>Buttons offered to the user (mirrors the MB_* sets used by the original setup).</summary>
public enum Buttons
{
    Ok,
    OkCancel,
    YesNo,
    RetryCancel,
    CancelTryContinue,
}

/// <summary>User answer. <see cref="None"/> means "no answer" (silent mode or dialog failure).</summary>
public enum Choice
{
    None,
    Ok,
    Cancel,
    Yes,
    No,
    Retry,
    TryAgain,
    Continue,
}

/// <summary>
/// Front-end abstraction. The original setup wrote everything through Trace() and asked the
/// user through MessageBox(); CLI and GUI provide their own implementation of both.
/// </summary>
public interface ISetupUi
{
    /// <summary>Progress / trace output (may contain partial lines).</summary>
    void Write(string text);

    /// <summary>Ask the user. Must return <see cref="Choice.None"/> when running silently.</summary>
    Choice Ask(string message, Buttons buttons, bool warning = false);
}

/// <summary>UI that never asks anything; useful for tests and unattended use.</summary>
public sealed class SilentUi(Action<string>? write = null) : ISetupUi
{
    public void Write(string text) => write?.Invoke(text);
    public Choice Ask(string message, Buttons buttons, bool warning = false) => Choice.None;
}

internal static class UiExtensions
{
    public static void Line(this ISetupUi ui, string text = "") => ui.Write(text + "\n");

    public static Choice Show(this ISetupUi ui, Buttons buttons, string message, bool warning = false)
    {
        ui.Write($"\nDIALOG: {{\n{message.TrimEnd('\n')}\n}} ... ");
        var res = ui.Ask(message.TrimEnd('\n'), buttons, warning);
        ui.Line(res == Choice.None ? "NO ANSWER" : res.ToString().ToUpperInvariant());
        return res;
    }

    public static Choice ShowError(this ISetupUi ui, Buttons buttons, int err, string what, bool warning = false)
        => ui.Show(buttons, $"{what}\nERROR: {FormatError(err)}", warning);

    public static Choice ShowLastError(this ISetupUi ui, Buttons buttons, string what, bool warning = false)
        => ui.ShowError(buttons, System.Runtime.InteropServices.Marshal.GetLastWin32Error(), what, warning);

    public static string FormatError(int err)
    {
        string text = new Win32Exception(err).Message;
        return (err & 0xFFFF0000) == 0 ? $"{err} - {text}" : $"0x{err:X8} - {text}";
    }
}

/// <summary>Thrown when an operation was cancelled or failed and could not continue.</summary>
public sealed class SetupException(string message) : Exception(message);
