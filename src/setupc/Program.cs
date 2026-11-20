using System.Runtime.InteropServices;
using System.Text;
using Com0com.Core;

namespace Com0com.Setupc;

/// <summary>
/// Drop-in replacement for the original com0com setupc.exe: same commands, options, help text,
/// interactive "command&gt;" mode, MessageBox prompts and exit codes (0 = success, 1 = failure).
/// Port of Main()/MainA() in the original setup/setup.cpp.
/// </summary>
internal static class Program
{
    private const string Title = "Setup for NewVirtualCOMPair";

    private static int Main(string[] args)
    {
        string progName = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "setupc";

        // The original joins its arguments with spaces and re-tokenizes them (ParseCmd).
        var argv = ParseCmd(string.Join(" ", args));
        if (argv.Count > 0)
            return Run(progName, argv);

        LegacyUi.ConsoleWrite($"Enter 'help' to get info about usage of {Title}.\n\n");
        while (true)
        {
            LegacyUi.ConsoleWrite("command> ");
            string? line = Console.ReadLine();
            if (line == null) return 0;
            argv = ParseCmd(line);
            if (argv.Count == 1 && argv[0].Equals("quit", StringComparison.OrdinalIgnoreCase)) return 0;
            Run("", argv);
        }
    }

    /// <summary>Splits on blanks; a leading and a trailing double quote are stripped from each token.</summary>
    private static List<string> ParseCmd(string cmd)
    {
        var list = new List<string>();
        foreach (var raw in cmd.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count + 2 > 9) break; // the original argv[10] limit
            string arg = raw.StartsWith('"') ? raw[1..] : raw;
            if (arg.Length > 0 && arg.EndsWith('"')) arg = arg[..^1];
            list.Add(arg);
        }
        return list;
    }

    private static int Run(string progName, List<string> args)
    {
        var ui = new LegacyUi();
        var options = new SetupOptions { InfDirectory = DefaultInfDir() };
        int timeout = 0;
        bool repeatTimeout = false;

        // Options (case-sensitive, before the command), as in the original.
        int i = 0;
        while (i < args.Count && args[i].StartsWith('-'))
        {
            string opt = args[i];
            bool hasValue = i + 1 < args.Count;
            if (opt == "--output" && hasValue)
            {
                ui.OutputFile = args[i + 1];
                i += 2;
            }
            else if (opt == "--wait" && hasValue)
            {
                string v = args[i + 1];
                string digits = v.StartsWith('+') ? v[1..] : v;
                if (digits.Length == 0 || !digits.All(char.IsAsciiDigit) || !int.TryParse(digits, out int num))
                {
                    LegacyUi.ConsoleWrite($"Invalid option {opt} {v}\n");
                    return 1;
                }
                timeout = num;
                repeatTimeout = v.StartsWith('+');
                i += 2;
            }
            else if (opt == "--detail-prms") { options.DetailParams = true; i++; }
            else if (opt == "--silent") { ui.Silent = true; i++; }
            else if (opt == "--no-update") { options.NoUpdate = true; i++; }
            else if (opt == "--no-update-fnames") { options.NoUpdateFriendlyNames = true; i++; }
            else if (opt == "--show-fnames") { options.ShowFriendlyNames = true; i++; }
            else
            {
                LegacyUi.ConsoleWrite($"Invalid option {opt}\n");
                return 1;
            }
        }

        var cmd = args.Skip(i).ToList();
        int argc = cmd.Count + 1; // the original counts argv[0]
        string Arg(int n) => cmd[n - 1];
        bool Is(string name) => Arg(1).Equals(name, StringComparison.OrdinalIgnoreCase);

        var setup = new Com0comSetup(ui, options);

        int Complete(bool ok)
        {
            if (setup.RebootRequired && !ui.Silent)
                SetupPromptReboot(IntPtr.Zero, IntPtr.Zero, false);
            if (!ok) return 1;
            if (timeout > 0) setup.WaitForInstallCompletion(timeout, repeatTimeout && !ui.Silent);
            return 0;
        }

        if (argc == 1) return Complete(true);

        if (argc == 2 && Is("help"))
        {
            ui.Title = Title + " (HELP)";
            Help(progName);
            return Complete(true);
        }
        if (argc == 2 && Is("quit")) return Complete(true);
        if (argc == 3 && Is("busynames"))
        {
            ui.Title = Title + " (BUSY NAMES)";
            foreach (var name in setup.BusyNames(Arg(2))) ui.Write(name + "\n");
            return Complete(true);
        }
        if (argc == 2 && Is("list"))
        {
            ui.Title = Title + " (LIST)";
            return Complete(Change(setup, ui, null, null));
        }
        if (argc == 2 && Is("listfnames"))
        {
            ui.Title = Title + " (LIST FRIENDLY NAMES)";
            return Complete(setup.UpdateFriendlyNames(false));
        }

        if (argc == 2 && Is("updatefnames"))
        {
            ui.Title = Title + " (UPDATE FRIENDLY NAMES)";
            return Complete(setup.UpdateFriendlyNames(true));
        }
        if (argc == 4 && Is("change"))
        {
            ui.Title = Title + " (CHANGE)";
            return Complete(Change(setup, ui, Arg(2), Arg(3)));
        }
        if (argc == 3 && Is("remove"))
        {
            ui.Title = Title + " (REMOVE)";
            if (TryNum(Arg(2), out int num))
            {
                // The original fails silently if the pair isn't installed.
                if (!setup.ListPairs().Any(p => p.Num == num)) return Complete(false);
                return Complete(setup.RemovePair(num));
            }
        }
        else if (argc == 3 && Is("disable"))
        {
            ui.Title = Title + " (DISABLE)";
            if (Arg(2).Equals("all", StringComparison.OrdinalIgnoreCase)) return Complete(setup.DisableAll());
        }
        else if (argc == 3 && Is("enable"))
        {
            ui.Title = Title + " (ENABLE)";
            if (Arg(2).Equals("all", StringComparison.OrdinalIgnoreCase)) return Complete(setup.EnableAll());
        }
        else if (argc == 2 && Is("reload"))
        {
            ui.Title = Title + " (RELOAD)";
            return Complete(setup.Reload());
        }

        // The remaining commands need valid INF files.
        if (!setup.CheckInfFiles()) return 1;

        if (argc == 2 && Is("preinstall"))
        {
            ui.Title = Title + " (PREINSTALL)";
            return Complete(setup.Preinstall());
        }
        if (argc == 2 && Is("uninstall"))
        {
            ui.Title = Title + " (UNINSTALL)";
            return Complete(setup.Uninstall());
        }
        if (argc == 2 && Is("infclean"))
        {
            ui.Title = Title + " (INF CLEAN)";
            return Complete(setup.InfClean());
        }
        if (argc == 2 && Is("update"))
        {
            ui.Title = Title + " (UPDATE)";
            return Complete(setup.Update());
        }
        if (argc == 2 && Is("install"))
        {
            ui.Title = Title + " (INSTALL)";
            // "install" only (re)installs the driver; with --no-update it does nothing.
            return Complete(options.NoUpdate || setup.InstallDriver());
        }
        if (argc == 4 && Is("install"))
        {
            ui.Title = Title + " (INSTALL)";
            return Complete(setup.InstallPair(-1, Arg(2), Arg(3)) >= 0);
        }
        if (argc == 5 && Is("install"))
        {
            ui.Title = Title + " (INSTALL)";
            if (TryNum(Arg(2), out int num))
                return Complete(setup.InstallPair(num, Arg(3), Arg(4)) >= 0);
        }

        LegacyUi.ConsoleWrite("Invalid command\n");
        return 1;
    }

    private static bool TryNum(string s, out int num) =>
        int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, null, out num) && num >= 0;

    /// <summary>
    /// "list" (portId == null) and "change": shows every port with its parameters; the given port
    /// is changed in place. Like the original it always succeeds (errors are only reported).
    /// </summary>
    private static bool Change(Com0comSetup setup, LegacyUi ui, string? portId, string? parameters)
    {
        foreach (var pair in setup.ListPairs())
        {
            foreach (var port in new[] { pair.A, pair.B })
            {
                if (portId != null && parameters != null && port.Id.Equals(portId, StringComparison.OrdinalIgnoreCase))
                    setup.ChangePort(port.Id, parameters); // prints the current and the changed parameters
                else
                    ui.Write($"       {port.Id} {port.Parameters.Format(setup.Options.DetailParams)}\n");
            }
        }
        return true;
    }

    private static string DefaultInfDir()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            if (File.Exists(Path.Combine(dir, C0C.InfName))) return dir;
        return AppContext.BaseDirectory;
    }

    [DllImport("setupapi.dll")]
    private static extern int SetupPromptReboot(IntPtr fileQueue, IntPtr owner, [MarshalAs(UnmanagedType.Bool)] bool scanOnly);

    private static void Help(string progName)
    {
        string p = progName.Length > 0 ? progName + " " : "";
        var sb = new StringBuilder();
        sb.Append($$"""
            {{Title}}

            Usage:
              {{p}}[options] <command>

            Options:
              --output <file>              - file for output, default is console
              --wait [+]<to>               - wait <to> seconds for install completion. If
                                             <to> has '+' prefix then ask user to continue
                                             waiting after <to> seconds elapsing
                                             (by default <to> is 0 - no wait)
              --detail-prms                - show detailed parameters
              --silent                     - suppress dialogs if possible
              --no-update                  - do not update driver while install command
                                             execution (the other install command w/o this
                                             option expected later)
              --no-update-fnames           - do not update friendly names
              --show-fnames                - show friendly names activity

            Commands:
              install <n> <prmsA> <prmsB>  - install a pair of linked ports with
               or                            identifiers CNCA<n> and CNCB<n>
              install <prmsA> <prmsB>        (by default <n> is the first not used number),
                                             set their parameters to <prmsA> and <prmsB>
              install                      - can be used to update driver after execution
                                             of install commands with --no-update option
              remove <n>                   - remove a pair of linked ports with
                                             identifiers CNCA<n> and CNCB<n>
              disable all                  - disable all ports in current hardware profile
              enable all                   - enable all ports in current hardware profile
              change <portid> <prms>       - set parameters <prms> for port with
                                             identifier <portid>
              list                         - for each port show its identifier and
                                             parameters
              preinstall                   - preinstall driver
              update                       - update driver
              reload                       - reload driver
              uninstall                    - uninstall all ports and the driver
              infclean                     - clean old INF files
              busynames <pattern>          - show names that already in use and match the
                                             <pattern> (wildcards: '*' and '?')
              updatefnames                 - update friendly names
              listfnames                   - for each bus and port show its identifier and
                                             friendly name
              quit                         - quit
              help                         - print this help

            Syntax of port parameters string:
              -                       - use driver's defaults for all parameters
              *                       - use current settings for all parameters
              <par>=<val>[,...]       - set value <val> for each parameter <par>

            Parameters:
              PortName=<portname>     - set port name to <portname>
                                        (port identifier by default)
              EmuBR={yes|no}          - enable/disable baud rate emulation in the direction
                                        to the paired port (disabled by default)
              EmuOverrun={yes|no}     - enable/disable buffer overrun (disabled by default)
              EmuNoise=<n>            - probability in range 0-0.99999999 of error per
                                        character frame in the direction to the paired port
                                        (0 by default)
              AddRTTO=<n>             - add <n> milliseconds to the total time-out period
                                        for read operations (0 by default)
              AddRITO=<n>             - add <n> milliseconds to the maximum time allowed to
                                        elapse between the arrival of two characters for
                                        read operations (0 by default)
              PlugInMode={yes|no}     - enable/disable plug-in mode, the plug-in mode port
                                        is hidden and can't be open if the paired port is
                                        not open (disabled by default)
              ExclusiveMode={yes|no}  - enable/disable exclusive mode, the exclusive mode
                                        port is hidden if it is open (disabled by default)
              HiddenMode={yes|no}     - enable/disable hidden mode, the hidden mode port is
                                        hidden as it is possible for port enumerators
                                        (disabled by default)
              AllDataBits={yes|no}    - enable/disable all data bits transfer disregard
                                        data bits setting (disabled by default)
              cts=[!]<p>              - wire CTS pin to <p> (rrts by default)
              dsr=[!]<p>              - wire DSR pin to <p> (rdtr by default)
              dcd=[!]<p>              - wire DCD pin to <p> (rdtr by default)
              ri=[!]<p>               - wire RI pin to <p> (!on by default)

            The possible values of <p> above can be rrts, lrts, rdtr, ldtr, rout1, lout1,
            rout2, lout2 (remote/local RTS/DTR/OUT1/OUT2), ropen, lopen (logical ON if
            remote/local port is open) or on (logical ON). The exclamation sign (!) can be
            used to invert the value.

            Special values:
              -                       - use driver's default value
              *                       - use current setting

            If parameter 'PortName=COM#' is used then the Ports class installer will be
            invoked to set the real port name. The Ports class installer selects the COM
            port number and sets the real port name to COM<n>, where <n> is the selected
            port number. Thereafter use parameter RealPortName=COM<n> to change the real
            port name.

            Examples:
              {{p}}install - -
              {{p}}install 5 * *
              {{p}}remove 0
              {{p}}install PortName=COM2 PortName=COM4
              {{p}}install PortName=COM5,EmuBR=yes,EmuOverrun=yes -
              {{p}}change CNCA0 EmuBR=yes,EmuOverrun=yes
              {{p}}change CNCA0 PortName=-
              {{p}}list
              {{p}}uninstall
              {{p}}busynames COM?*


            """);
        LegacyUi.ConsoleWrite(sb.ToString());
    }
}
