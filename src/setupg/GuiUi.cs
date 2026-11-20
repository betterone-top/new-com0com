using Avalonia.Threading;
using Com0com.Core;

namespace Com0com.Setupg;

/// <summary>
/// Bridges core trace/prompt calls to the window. Core operations run on a worker thread;
/// prompts block that thread while the dialog runs on the UI thread.
/// </summary>
internal sealed class GuiUi(MainWindow window) : ISetupUi
{
    public void Write(string text) => Dispatcher.UIThread.Post(() => window.AppendLog(text));

    public Choice Ask(string message, Buttons buttons, bool warning = false)
    {
        var kind = buttons switch
        {
            Buttons.Ok => warning ? DialogKind.Warning : DialogKind.Error,
            Buttons.YesNo => DialogKind.Question,
            _ => DialogKind.Warning,
        };
        if (Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Core operations must not run on the UI thread.");
        return Dispatcher.UIThread.InvokeAsync(() => MessageDialog.ShowAsync(window, message, buttons, kind))
            .GetAwaiter().GetResult();
    }
}
