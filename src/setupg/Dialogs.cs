using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Com0com.Core;

namespace Com0com.Setupg;

internal enum DialogKind { Info, Question, Warning, Error }

/// <summary>Simple modal message box (Avalonia has none built in).</summary>
internal sealed class MessageDialog : Window
{
    private Choice _result = Choice.None;

    private MessageDialog(string message, string title, IEnumerable<(string Text, Choice Value, bool IsDefault, bool IsCancel)> buttons, DialogKind kind)
    {
        Title = title;
        Icon = App.Icon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 360;
        MaxWidth = 640;

        var (glyph, color) = kind switch
        {
            DialogKind.Error => ("✖", Color.FromRgb(0xC4, 0x2B, 0x1C)),
            DialogKind.Warning => ("!", Color.FromRgb(0xC1, 0x7D, 0x00)),
            DialogKind.Question => ("?", Color.FromRgb(0x00, 0x5F, 0xB8)),
            _ => ("i", Color.FromRgb(0x00, 0x5F, 0xB8)),
        };
        var badge = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var text = new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        foreach (var (caption, value, isDefault, isCancel) in buttons)
        {
            var b = new Button { Content = caption, MinWidth = 88, IsDefault = isDefault, IsCancel = isCancel };
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (isDefault) b.Classes.Add("accent");
            b.Click += (_, _) =>
            {
                _result = value;
                Close();
            };
            buttonRow.Children.Add(b);
        }

        var body = new DockPanel { Margin = new Thickness(20, 20, 20, 16) };
        DockPanel.SetDock(badge, Dock.Left);
        body.Children.Add(badge);
        body.Children.Add(text);

        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            Padding = new Thickness(20, 12),
            Child = buttonRow,
        };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
    }

    private static IEnumerable<(string, Choice, bool, bool)> ButtonsFor(Buttons buttons) => buttons switch
    {
        Buttons.Ok => [("OK", Choice.Ok, true, true)],
        Buttons.OkCancel => [("OK", Choice.Ok, true, false), ("Cancel", Choice.Cancel, false, true)],
        Buttons.YesNo => [("Yes", Choice.Yes, true, false), ("No", Choice.No, false, true)],
        Buttons.RetryCancel => [("Retry", Choice.Retry, true, false), ("Cancel", Choice.Cancel, false, true)],
        _ => [("Cancel", Choice.Cancel, false, true), ("Try Again", Choice.TryAgain, true, false), ("Continue", Choice.Continue, false, false)],
    };

    /// <summary>Shows a modal box; closing it with the window button counts as Cancel (or No / OK).</summary>
    public static async Task<Choice> ShowAsync(Window owner, string message, Buttons buttons, DialogKind kind, string title = "NewVirtualCOMPair")
    {
        var dlg = new MessageDialog(message, title, ButtonsFor(buttons), kind);
        await dlg.ShowDialog(owner);
        if (dlg._result != Choice.None) return dlg._result;
        return buttons switch
        {
            Buttons.Ok => Choice.Ok,
            Buttons.YesNo => Choice.No,
            _ => Choice.Cancel,
        };
    }

    public static Task ShowInfoAsync(Window owner, string message, string title = "NewVirtualCOMPair") =>
        ShowAsync(owner, message, Buttons.Ok, DialogKind.Info, title);

    public static async Task<bool> ConfirmAsync(Window owner, string message, DialogKind kind = DialogKind.Question) =>
        await ShowAsync(owner, message, Buttons.OkCancel, kind) == Choice.Ok;

    /// <summary>Yes / No question (MessageBoxButtons.YesNo of the original).</summary>
    public static async Task<bool> YesNoAsync(Window owner, string message, string title = "NewVirtualCOMPair") =>
        await ShowAsync(owner, message, Buttons.YesNo, DialogKind.Question, title) == Choice.Yes;

    /// <summary>Yes / No / Cancel question; closing the box counts as Cancel.</summary>
    public static async Task<Choice> YesNoCancelAsync(Window owner, string message, string title = "NewVirtualCOMPair")
    {
        var dlg = new MessageDialog(message, title,
            [("Yes", Choice.Yes, true, false), ("No", Choice.No, false, false), ("Cancel", Choice.Cancel, false, true)],
            DialogKind.Question);
        await dlg.ShowDialog(owner);
        return dlg._result == Choice.None ? Choice.Cancel : dlg._result;
    }
}
