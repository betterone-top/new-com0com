using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Com0com.Core;

namespace Com0com.Setupg;

/// <summary>
/// Replica of the original setupg window (setupg/Form1.h + Form1.resx): pair tree on the left,
/// port names, mirrored option check boxes and the pin map on the right, Add Pair / Remove Pair /
/// Reset / Apply below. Positions are the original ones scaled by <see cref="Scale"/>.
/// Changed values are blue (a port name already in use is red), as in the original.
/// Functions the original GUI didn't have (driver maintenance, log, about) are in the
/// context menu of the pair tree.
/// </summary>
internal sealed class MainWindow : Window
{
    /// <summary>The original form is 445x366 at 96 DPI with an 8 pt font; today's controls need more room.</summary>
    public const double Scale = 1.5;
    private const double UiFont = 12.5;

    private static readonly IBrush Changed = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0xFF));
    private static readonly IBrush Invalid = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
    private static readonly IBrush Normal = Brushes.Black;

    private static readonly (string Key, string Caption, double Y, string Tip)[] Flags =
    [
        ("emubr", "emulate baud rate", 47, "Enable/disable baud rate emulation\nin the direction to the paired port."),
        ("emuoverrun", "enable buffer overrun", 63, "Enable/disable receive buffer overrun."),
        ("pluginmode", "enable plug-in mode", 79, "Enable/disable plug-in mode.\nThe plug-in mode port is hidden and can't\nbe open if the paired port is not open."),
        ("exclusivemode", "enable exclusive mode", 94, "Enable/disable exclusive mode.\nThe exclusive mode port is hidden if it is open."),
        ("hiddenmode", "enable hidden mode", 110, "Enable/disable hidden mode.\nThe hidden mode port is hidden as\nit is possible for port enumerators."),
    ];

    private const string NameTip = "Port name.\nThe name is red if it's already in use by other device.\nEnter '-' to reset the name to the default.";
    private const string PortsClassTip = "Allow Ports class installer to set\nthe real port name. The Ports\nclass installer selects the COM\n" +
                                         "port number and sets the real\nport name to COM<n>, where\n<n> is the selected port number.\n" +
                                         "Thereafter you will be able to\nchange port number.";

    private readonly Com0comSetup _setup;
    private readonly Canvas _canvas = new();
    private readonly TreeView _tree = new() { FontSize = UiFont };
    private readonly TextBox[] _name = new TextBox[2];
    private readonly CheckBox[] _portsClass = new CheckBox[2];
    private readonly CheckBox[,] _flags = new CheckBox[2, Flags.Length];
    private readonly PinMap _pinMap = new(Scale, UiFont);
    private readonly Button _add, _remove, _reset, _apply;

    /// <summary>The pairs as the original kept them: number → [A, B] parameters (lower-case keys, upper-case values).</summary>
    private SortedDictionary<int, Dictionary<string, string>[]> _pairs = [];
    private HashSet<string> _busyNames = new(StringComparer.OrdinalIgnoreCase);
    private int? _current;
    private readonly Dictionary<int, bool[]> _expanded = [];
    private bool _loading, _busy, _selecting, _rebootAsked;

    private readonly StringBuilder _logText = new();
    private LogWindow? _logWindow;

    public MainWindow()
    {
        Title = "NewVirtualCOMPair";
        Icon = App.Icon;
        Width = 445 * Scale;
        Height = 366 * Scale;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontSize = UiFont;

        _setup = new Com0comSetup(new GuiUi(this), new SetupOptions { InfDirectory = DefaultInfDir() });

        // ---- pair tree
        var treeBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = _tree,
        };
        Place(treeBorder, 9, 10, 156, 318);
        ToolTip.SetTip(_tree, "Current parameters.");
        _tree.SelectionChanged += async (_, _) => await OnTreeSelectionChangedAsync();
        _tree.ContextMenu = BuildContextMenu();

        // ---- buttons
        _add = MakeButton("Add Pair", "Add a new port pair with default parameters.", 9);
        _remove = MakeButton("Remove Pair", "Remove the selected port pair.", 88);
        _reset = MakeButton("Reset", "Reload the current parameters and redraw the form.", 175);
        _apply = MakeButton("Apply", "Apply the changed parameters (highlighted with blue color).", 361);
        _add.Click += async (_, _) => await AddPairAsync();
        _remove.Click += async (_, _) => await RemovePairAsync();
        _reset.Click += async (_, _) => await RefreshAsync();
        _apply.Click += async (_, _) => await ApplyAsync();

        // ---- port names and options, A mirrored to the left of B
        for (int i = 0; i < 2; i++)
        {
            int side = i;
            var name = new TextBox
            {
                FontSize = UiFont,
                MinHeight = 0,
                Padding = new Thickness(4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right,
            };
            ToolTip.SetTip(name, NameTip);
            Place(name, i == 0 ? 175 : 361, 12, 76, 20);
            name.TextChanged += (_, _) => { if (!_loading) PortNameChanged(side); };
            _name[i] = name;

            _portsClass[i] = MakeCheckBox("use Ports class", PortsClassTip, side, 31);
            _portsClass[i].IsCheckedChanged += (_, _) => { if (!_loading) PortsClassChanged(side); };

            for (int k = 0; k < Flags.Length; k++)
            {
                int flag = k;
                var cb = MakeCheckBox(Flags[k].Caption, Flags[k].Tip, side, Flags[k].Y);
                cb.IsCheckedChanged += (_, _) => { if (!_loading) FlagChanged(side, flag); };
                _flags[i, k] = cb;
            }
        }

        // ---- pin map with its pin names (the original labels sat 38 px left/right of the 188x179 picture)
        Canvas.SetLeft(_pinMap, 174 * Scale);
        Canvas.SetTop(_pinMap, 133 * Scale);
        _canvas.Children.Add(_pinMap);
        _pinMap.Changed += (_, _) => UpdateButtons();

        _canvas.Width = Width;
        _canvas.Height = Height;
        Content = _canvas;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F5 && !_busy) { _ = RefreshAsync(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Opened += async (_, _) => await RefreshAsync();
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        ResetControls();
    }

    private static string DefaultInfDir()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            if (File.Exists(Path.Combine(dir, C0C.InfName))) return dir;
        return AppContext.BaseDirectory;
    }

    // ------------------------------------------------------------------ layout helpers

    private void Place(Control c, double x, double y, double w, double h)
    {
        Canvas.SetLeft(c, x * Scale);
        Canvas.SetTop(c, y * Scale);
        c.Width = w * Scale;
        c.Height = h * Scale;
        _canvas.Children.Add(c);
    }

    private Button MakeButton(string text, string tip, double x)
    {
        var b = new Button
        {
            Content = text,
            FontSize = UiFont,
            MinHeight = 0,
            Padding = new Thickness(4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        Place(b, x, 337, 75, 21);
        return b;
    }

    /// <summary>Option check box: port A's text is left of the box (RightToLeft), port B's right of it.</summary>
    private CheckBox MakeCheckBox(string text, string tip, int side, double y)
    {
        var cb = new CheckBox
        {
            Content = text,
            FontSize = UiFont,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FlowDirection = side == 0 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            HorizontalAlignment = side == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
        ToolTip.SetTip(cb, tip);
        Place(cb, side == 0 ? 169 : 308, y, 135, 17);
        return cb;
    }

    // ------------------------------------------------------------------ pairs (PortPairs of the original)

    private static Dictionary<string, string> ParseParams(string s)
    {
        var d = new Dictionary<string, string>();
        foreach (var item in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = item.Split('=');
            if (kv.Length == 2) d[kv[0].ToLowerInvariant()] = kv[1].ToUpperInvariant();
        }
        return d;
    }

    private Dictionary<string, string>[]? CurrentPair =>
        _current is int n && _pairs.TryGetValue(n, out var p) ? p : null;

    private Dictionary<string, string>? Params(int i) => CurrentPair?[i];

    private bool IsValidName(string name) => !_busyNames.Contains(name);

    /// <summary>Reads all pairs ("--detail-prms list") and the busy names.</summary>
    private async Task LoadPairsAsync()
    {
        var result = await Work("Reading port pairs", s => new Tuple<List<PairInfo>, List<string>>(s.ListPairs(), s.BusyNames("*")));
        if (result == null) return;
        _pairs = new SortedDictionary<int, Dictionary<string, string>[]>(result.Item1.ToDictionary(
            p => p.Num,
            p => new[] { ParseParams(p.A.Parameters.Format(true)), ParseParams(p.B.Parameters.Format(true)) }));
        _busyNames = new HashSet<string>(result.Item2, StringComparer.OrdinalIgnoreCase);
    }

    private async Task RefreshAsync()
    {
        await LoadPairsAsync();
        BuildTree();
        ResetControls();
    }

    // ------------------------------------------------------------------ tree

    private void BuildTree()
    {
        _selecting = true;
        try
        {
            foreach (var node in _tree.Items.OfType<TreeViewItem>())
            {
                if (node.Tag is not int n) continue;
                var ports = node.Items.OfType<TreeViewItem>().ToArray();
                _expanded[n] = [node.IsExpanded, ports.ElementAtOrDefault(0)?.IsExpanded == true, ports.ElementAtOrDefault(1)?.IsExpanded == true];
            }

            _tree.Items.Clear();
            TreeViewItem? select = null;
            foreach (var (num, pair) in _pairs)
            {
                var exp = _expanded.GetValueOrDefault(num) ?? [true, false, false];
                var node = new TreeViewItem { Header = $"Virtual Port Pair {num}", Tag = num, IsExpanded = exp[0] };
                for (int i = 0; i < 2; i++)
                {
                    string name = pair[i].GetValueOrDefault("portname") ?? $"CNC{(i == 0 ? "A" : "B")}{num}";
                    if (name == "COM#" && pair[i].TryGetValue("realportname", out var real)) name = real;
                    var port = new TreeViewItem { Header = name, Tag = num, IsExpanded = exp[i + 1] };
                    foreach (var (k, v) in pair[i]) port.Items.Add(new TreeViewItem { Header = $"{k}={v}", Tag = num });
                    node.Items.Add(port);
                }
                _tree.Items.Add(node);
                if (num == _current) select = node;
            }
            select ??= _tree.Items.OfType<TreeViewItem>().FirstOrDefault();
            _tree.SelectedItem = select;
            _current = select?.Tag as int?;
        }
        finally
        {
            _selecting = false;
        }
    }

    private TreeViewItem? PairNode(int? num) =>
        _tree.Items.OfType<TreeViewItem>().FirstOrDefault(n => n.Tag is int t && t == num);

    private void SelectPair(int? num)
    {
        _selecting = true;
        try { _tree.SelectedItem = PairNode(num); }
        finally { _selecting = false; }
    }

    /// <summary>BeforeSelect + AfterSelect of the original: offer to apply pending changes first.</summary>
    private async Task OnTreeSelectionChangedAsync()
    {
        if (_selecting || _busy) return;
        int? target = (_tree.SelectedItem as TreeViewItem)?.Tag as int?;
        if (target == null || target == _current) return;

        if (HasChanges())
        {
            var res = await MessageDialog.YesNoCancelAsync(this,
                $"The parameters of \"{PairNode(_current)?.Header}\" were changed.\nWould you like to apply the changes?", "Apply");
            if (res == Choice.Cancel)
            {
                SelectPair(_current);
                return;
            }
            if (res == Choice.Yes) await ApplyAsync();
        }
        _current = target;
        SelectPair(target);
        ResetControls();
    }

    // ------------------------------------------------------------------ controls (ForEachControlPair(Reset) etc.)

    /// <summary>Name shown for port i, whether the box is editable and whether it uses the Ports class.</summary>
    private (string Name, bool Enabled, bool Checked) PortNameCurrent(int i)
    {
        var p = Params(i);
        if (p == null || !p.TryGetValue("portname", out var name)) return ("", true, false);
        if (name.ToUpperInvariant() == "COM#")
            return p.TryGetValue("realportname", out var real) ? (real, true, true) : ("", false, true);
        return (name, true, false);
    }

    private void ResetControls()
    {
        _loading = true;
        try
        {
            bool any = CurrentPair != null;
            for (int i = 0; i < 2; i++)
            {
                var (name, enabled, chk) = PortNameCurrent(i);
                _name[i].Text = name;
                _name[i].IsEnabled = enabled && any;
                _portsClass[i].IsChecked = chk;
                _portsClass[i].IsEnabled = any;
                for (int k = 0; k < Flags.Length; k++)
                {
                    _flags[i, k].IsChecked = Params(i)?.GetValueOrDefault(Flags[k].Key) == "YES";
                    _flags[i, k].IsEnabled = any;
                }
            }
            _pinMap.Init(Params(0), Params(1));
            _pinMap.IsEnabled = any;
        }
        finally
        {
            _loading = false;
        }
        for (int i = 0; i < 2; i++)
        {
            PortNameChanged(i);
            for (int k = 0; k < Flags.Length; k++) FlagChanged(i, k);
        }
        _remove.IsEnabled = CurrentPair != null;
        UpdateButtons();
    }

    private void PortNameChanged(int i)
    {
        var (name, enabled, chk) = PortNameCurrent(i);
        string value = (_name[i].Text ?? "").ToUpperInvariant();
        _name[i].Foreground = name != value ? (IsValidName(value) ? Changed : Invalid) : Normal;
        _portsClass[i].Foreground = chk != (_portsClass[i].IsChecked == true) ? Changed : Normal;
        _name[i].IsEnabled = CurrentPair != null && (_portsClass[i].IsChecked != true || (chk && enabled));
        UpdateButtons();
    }

    private void PortsClassChanged(int i)
    {
        var (name, _, chk) = PortNameCurrent(i);
        bool box = _portsClass[i].IsChecked == true;
        _loading = true;
        if (chk || !box)
            _name[i].Text = !box && name == "" ? "-" : name;
        else
            _name[i].Text = "";
        _loading = false;
        PortNameChanged(i);
    }

    private void FlagChanged(int i, int k)
    {
        string value = _flags[i, k].IsChecked == true ? "YES" : "NO";
        string? current = Params(i)?.GetValueOrDefault(Flags[k].Key);
        _flags[i, k].Foreground = current != null && current != value ? Changed : Normal;
        UpdateButtons();
    }

    /// <summary>Changed parameters per port as setupc "change" strings (GetChanges of the original).</summary>
    private string[] GetChanges()
    {
        var result = new[] { "", "" };
        if (CurrentPair == null) return result;
        var pins = _pinMap.GetChanges();
        for (int i = 0; i < 2; i++)
        {
            var changes = new List<string>();
            foreach (var (k, v) in pins[i]) changes.Add($"{k}={v}");

            var (name, _, chk) = PortNameCurrent(i);
            string value = (_name[i].Text ?? "").ToUpperInvariant();
            if (_portsClass[i].IsChecked == true)
            {
                if (chk) { if (name != value) changes.Add("realportname=" + value); }
                else changes.Add("portname=COM#");
            }
            else if (chk || name != value)
            {
                changes.Add("portname=" + value);
            }

            for (int k = 0; k < Flags.Length; k++)
            {
                string fv = _flags[i, k].IsChecked == true ? "YES" : "NO";
                if (Params(i)?.GetValueOrDefault(Flags[k].Key) is { } cur && cur != fv) changes.Add($"{Flags[k].Key}={fv}");
            }
            result[i] = string.Join(',', changes);
        }
        return result;
    }

    private bool HasChanges() => GetChanges().Any(c => c.Length > 0);

    private void UpdateButtons()
    {
        if (_loading) return;
        _apply.IsEnabled = !_busy && CurrentPair != null;
        _reset.IsEnabled = !_busy;
    }

    // ------------------------------------------------------------------ buttons

    /// <summary>SaveChanges of the original: false if the user cancelled.</summary>
    private async Task<bool> SaveChangesAsync()
    {
        if (CurrentPair == null || !HasChanges()) return true;
        var res = await MessageDialog.YesNoCancelAsync(this,
            $"The parameters of \"{PairNode(_current)?.Header}\" were changed.\nWould you like to apply the changes?", "Apply");
        if (res == Choice.Cancel) return false;
        if (res == Choice.Yes) await ApplyAsync();
        return true;
    }

    private async Task ApplyAsync()
    {
        if (_current is not int num) return;
        var changes = GetChanges();
        if (changes.All(c => c.Length == 0)) return;
        await Work($"Changing pair {num}", s =>
        {
            for (int i = 0; i < 2; i++)
                if (changes[i].Length > 0) s.ChangePort(C0C.PortId(i == 0, num), changes[i]);
            s.WaitForInstallCompletion(30, false);
            return true;
        });
        await CheckRebootAsync();
        await RefreshAsync();
    }

    private async Task AddPairAsync()
    {
        if (!await SaveChangesAsync()) return;
        int added = await Work("Adding a pair", s =>
        {
            int n = s.InstallPair(-1, "-", "-");
            if (n >= 0) s.WaitForInstallCompletion(30, false);
            return n;
        });
        await CheckRebootAsync();
        if (added >= 0) _current = added;
        await RefreshAsync();
    }

    private async Task RemovePairAsync()
    {
        if (_current is not int num) return;
        if (!await MessageDialog.YesNoAsync(this, $"Would you like to remove \"{PairNode(num)?.Header}\"?", "")) return;
        await Work($"Removing pair {num}", s => s.RemovePair(num));
        await CheckRebootAsync();
        _current = null;
        await RefreshAsync();
    }

    // ------------------------------------------------------------------ context menu: what the original GUI didn't have

    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Func<Task> action)
        {
            var m = new MenuItem { Header = header };
            m.Click += async (_, _) => await action();
            return m;
        }
        Func<Task> Run(string status, Func<Com0comSetup, bool> op) => async () =>
        {
            await Work(status, s =>
            {
                bool ok = op(s);
                if (ok) s.WaitForInstallCompletion(30, false);
                return ok;
            });
            await CheckRebootAsync();
            await RefreshAsync();
        };

        var driver = new MenuItem { Header = "Driver" };
        driver.Items.Add(Item("Install driver for pending pairs", Run("Installing driver", s => s.InstallDriver())));
        driver.Items.Add(Item("Update driver", Run("Updating driver", s => s.Update())));
        driver.Items.Add(Item("Reload driver", Run("Reloading driver", s => s.Reload())));
        driver.Items.Add(new Separator());
        driver.Items.Add(Item("Enable all ports", Run("Enabling", s => s.EnableAll())));
        driver.Items.Add(Item("Disable all ports", Run("Disabling", s => s.DisableAll())));
        driver.Items.Add(new Separator());
        driver.Items.Add(Item("Preinstall (class and INF files)", Run("Preinstalling", s => s.Preinstall())));
        driver.Items.Add(Item("Clean old INF files", Run("Cleaning INF files", s => s.InfClean())));
        driver.Items.Add(Item("Update friendly names", Run("Updating friendly names", s => s.UpdateFriendlyNames(true))));
        driver.Items.Add(new Separator());
        driver.Items.Add(Item("Uninstall everything...", async () =>
        {
            if (await MessageDialog.ConfirmAsync(this, "Remove all port pairs and uninstall the com0com driver?", DialogKind.Warning))
                await Run("Uninstalling", s => s.Uninstall())();
        }));
        driver.Items.Add(new Separator());
        driver.Items.Add(Item("Open Device Manager", () =>
        {
            Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
            return Task.CompletedTask;
        }));

        return new ContextMenu
        {
            Items =
            {
                Item("Refresh (F5)", RefreshAsync),
                driver,
                new Separator(),
                Item("Show log", () => { ShowLog(); return Task.CompletedTask; }),
                Item("Port parameters help", () => MessageDialog.ShowInfoAsync(this, PortParameters.Help, "Port parameters")),
                Item("About", () => MessageDialog.ShowInfoAsync(this,
                    $"NewVirtualCOMPair {BuildInfo.Version}\n\n" +
                    "NewVirtualCOMPair by Big Mouse\n" +
                    "Virtual serial port pairs: setup tools for the com0com driver. A hobby project, no warranty.\n" +
                    $"Driver directory: {_setup.Options.InfDirectory}\n\n" +
                    "This program: GNU GPL v3 or later.\n" +
                    "com0com driver Copyright (c) 2004-2012 Vyacheslav Frolov, GPL v2 or later.", "About")),
            },
        };
    }

    // ------------------------------------------------------------------ work, log, reboot

    private async Task<T?> Work<T>(string status, Func<Com0comSetup, T> op)
    {
        _busy = true;
        _canvas.IsEnabled = false;
        Cursor = new Cursor(StandardCursorType.Wait);
        Title = $"NewVirtualCOMPair - {status}...";
        AppendLog($"\n=== {status} ===\n");
        try
        {
            return await Task.Run(() => op(_setup));
        }
        catch (Exception e)
        {
            AppendLog($"ERROR: {e.Message}\n");
            await MessageDialog.ShowAsync(this, e.Message, Buttons.Ok, DialogKind.Error);
            return default;
        }
        finally
        {
            _busy = false;
            _canvas.IsEnabled = true;
            Cursor = Cursor.Default;
            Title = "NewVirtualCOMPair";
            UpdateButtons();
        }
    }

    private async Task CheckRebootAsync()
    {
        // RebootRequired stays set for the rest of the session; ask only once.
        if (!_setup.RebootRequired || _rebootAsked) return;
        _rebootAsked = true;
        if (await MessageDialog.ShowAsync(this, "A reboot is required to complete the operation.\nRestart Windows now?",
                Buttons.YesNo, DialogKind.Question) == Choice.Yes)
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
        }
    }

    internal void AppendLog(string text)
    {
        text = text.ReplaceLineEndings("\n");
        _logText.Append(text);
        _logWindow?.Append(text);
    }

    private void ShowLog()
    {
        if (_logWindow == null)
        {
            _logWindow = new LogWindow(_logText.ToString());
            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.Show(this);
        }
        else
        {
            _logWindow.Activate();
        }
    }
}

/// <summary>Output of the setup operations (what setupc would have printed).</summary>
internal sealed class LogWindow : Window
{
    private readonly TextBox _text = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,Courier New"),
        FontSize = 12,
    };

    public LogWindow(string text)
    {
        Title = "NewVirtualCOMPair - log";
        Icon = App.Icon;
        Width = 760;
        Height = 420;
        _text.Text = text;
        Content = _text;
        Opened += (_, _) => _text.CaretIndex = _text.Text?.Length ?? 0;
    }

    public void Append(string text)
    {
        _text.Text += text;
        _text.CaretIndex = _text.Text?.Length ?? 0;
    }
}
