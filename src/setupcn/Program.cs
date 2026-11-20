using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Com0com.Core;

namespace Com0com.Cli;

internal sealed class UsageException(string message) : Exception(message);

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitFailed = 1;
    private const int ExitUsage = 2;
    private const int ExitRebootRequired = 3010; // ERROR_SUCCESS_REBOOT_REQUIRED

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            if (Console.IsInputRedirected)
            {
                Console.Out.Write(Help.Main);
                return ExitUsage;
            }
            return Shell();
        }
        return Run(args, interactive: false);
    }

    private static int Shell()
    {
        Console.WriteLine("NewVirtualCOMPair shell. Type 'help' for commands, 'exit' to quit.");
        while (true)
        {
            Console.Write("setupcn> ");
            var line = Console.ReadLine();
            if (line == null) return ExitOk;
            var args = Tokenize(line);
            if (args.Count == 0) continue;
            if (args[0] is "exit" or "quit") return ExitOk;
            int rc = Run([.. args], interactive: true);
            if (rc != ExitOk) Console.WriteLine($"(exit code {rc})");
        }
    }

    /// <summary>Splits a command line, honouring double quotes.</summary>
    internal static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false, any = false;
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; any = true; }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any) list.Add(cur.ToString());
                cur.Clear();
                any = false;
            }
            else { cur.Append(c); any = true; }
        }
        if (any) list.Add(cur.ToString());
        return list;
    }

    private static int Run(string[] argv, bool interactive)
    {
        var ui = new ConsoleUi();
        var options = new SetupOptions { InfDirectory = DefaultInfDir() };
        var a = new Args(argv);
        int waitSeconds = 0;
        bool waitAsk = false, json = false;

        try
        {
            // Global options may appear anywhere before or after the command words.
            a.TakeOptions(opt =>
            {
                switch (opt)
                {
                    case "-s" or "--silent": ui.Silent = true; return true;
                    case "-q" or "--quiet": ui.Quiet = true; return true;
                    case "--json": json = true; ui.Quiet = true; return true;
                    case "--detail": options.DetailParams = true; return true;
                    case "--no-update": options.NoUpdate = true; return true;
                    case "--no-fnames": options.NoUpdateFriendlyNames = true; return true;
                    case "--show-fnames": options.ShowFriendlyNames = true; return true;
                    case "--inf-dir": options.InfDirectory = Path.GetFullPath(a.OptionValue(opt)); return true;
                    case "--output" or "-o": ui.OutputFile = Path.GetFullPath(a.OptionValue(opt)); return true;
                    case "--wait" or "-w":
                        (waitSeconds, waitAsk) = ParseWait(a.OptionValue(opt));
                        return true;
                    case "-h" or "--help" or "-?" or "/?": a.HelpRequested = true; return true;
                }
                return false;
            });

            string cmd = a.Next()?.ToLowerInvariant() ?? "help";
            if (a.HelpRequested || cmd == "help")
            {
                string topic = cmd == "help" ? a.Next() ?? "" : cmd;
                Console.Out.Write(Help.For(topic));
                return ExitOk;
            }

            var setup = new Com0comSetup(ui, options);
            bool ok = Dispatch(cmd, a, setup, ui, json, interactive);

            if (ok && waitSeconds != 0) setup.WaitForInstallCompletion(waitSeconds, waitAsk && !ui.Silent);

            if (setup.RebootRequired)
            {
                ui.Print("A reboot is required to complete the operation.");
                return ok ? ExitRebootRequired : ExitFailed;
            }
            return ok ? ExitOk : ExitFailed;
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            Console.Error.WriteLine("Run 'setupcn help' for usage.");
            return ExitUsage;
        }
        catch (SetupException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return ExitFailed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return ExitFailed;
        }
    }

    private static readonly HashSet<string> Commands =
    [
        "list", "ls", "pair", "add", "remove", "rm", "port", "set", "reset", "show",
        "driver", "enable", "disable", "names", "wait", "version",
    ];

    private static bool Dispatch(string cmd, Args a, Com0comSetup setup, ConsoleUi ui, bool json, bool interactive)
    {
        // Report typos as usage errors before asking for elevation.
        if (!Commands.Contains(cmd)) throw new UsageException($"unknown command '{cmd}'");
        if (!IsReadOnly(cmd, a) && !IsAdministrator())
            throw new SetupException("this command must be run as Administrator (elevated).");

        switch (cmd)
        {
            case "list" or "ls":
                a.End();
                return List(setup, ui, json);

            case "pair":
                return Pair(a.Required("pair subcommand (add, remove, list)").ToLowerInvariant(), a, setup, ui, json);
            case "add":
                return Pair("add", a, setup, ui, json);
            case "remove" or "rm":
                return Pair("remove", a, setup, ui, json);

            case "port":
                return Port(a.Required("port subcommand (set, show, reset)").ToLowerInvariant(), a, setup, ui, json);
            case "set":
                return Port("set", a, setup, ui, json);
            case "reset":
                return Port("reset", a, setup, ui, json);
            case "show":
                return Port("show", a, setup, ui, json);

            case "driver":
            {
                string sub = a.Required("driver subcommand").ToLowerInvariant();
                a.End();
                return sub switch
                {
                    "install" => setup.InstallDriver(),
                    "update" => setup.Update(),
                    "reload" => setup.Reload(),
                    "preinstall" => setup.Preinstall(),
                    "uninstall" => ConfirmUninstall(ui) && setup.Uninstall(),
                    "clean" or "infclean" => setup.InfClean(),
                    _ => throw new UsageException($"unknown driver subcommand '{sub}'"),
                };
            }

            case "enable" or "disable":
                a.Optional(); // accept the legacy "all"
                a.End();
                return cmd == "enable" ? setup.EnableAll() : setup.DisableAll();

            case "names":
            {
                string sub = a.Required("names subcommand (busy, friendly)").ToLowerInvariant();
                switch (sub)
                {
                    case "busy":
                    {
                        string pattern = a.Optional() ?? "*";
                        a.End();
                        var names = setup.BusyNames(pattern);
                        if (json) ui.Print(Json(w => WriteStrings(w, names), indented: false));
                        else foreach (var n in names) ui.Print(n);
                        return true;
                    }
                    case "friendly":
                    {
                        bool update = a.Flag("--update");
                        a.End();
                        return setup.UpdateFriendlyNames(update);
                    }
                    default:
                        throw new UsageException($"unknown names subcommand '{sub}'");
                }
            }

            case "wait":
            {
                var (sec, ask) = ParseWait(a.Optional() ?? "-1");
                a.End();
                return setup.WaitForInstallCompletion(sec == 0 ? -1 : sec, ask && !ui.Silent);
            }

            case "version":
                a.End();
                ui.Print($"setupcn {BuildInfo.Version}");
                return true;

            default:
                throw new UsageException($"unknown command '{cmd}'");
        }
    }

    /// <summary>Commands that only read state and work without elevation.</summary>
    private static bool IsReadOnly(string cmd, Args a)
    {
        string sub = a.Peek()?.ToLowerInvariant() ?? "";
        return cmd switch
        {
            "list" or "ls" or "show" or "version" or "wait" => true,
            "pair" => sub is "list" or "ls",
            "port" => sub == "show",
            "names" => sub == "busy" || (sub == "friendly" && !a.Contains("--update")),
            _ => false,
        };
    }

    private static bool ConfirmUninstall(ConsoleUi ui)
    {
        if (ui.Silent || Console.IsInputRedirected) return true;
        Console.Write("This removes all port pairs and the com0com driver. Continue? [y/N] ");
        return Console.ReadLine()?.Trim().StartsWith('y') == true;
    }

    // ------------------------------------------------------------------ pair

    private static bool Pair(string sub, Args a, Com0comSetup setup, ConsoleUi ui, bool json)
    {
        switch (sub)
        {
            case "list" or "ls":
                a.End();
                return List(setup, ui, json);

            case "add":
            {
                int num = -1;
                var prmsA = new List<string>();
                var prmsB = new List<string>();
                string? n = null;
                while (a.Next() is { } t)
                {
                    switch (t)
                    {
                        case "-a" or "--a": prmsA.Add(PortArg(a.OptionValue(t))); break;
                        case "-b" or "--b": prmsB.Add(PortArg(a.OptionValue(t))); break;
                        case "--both": var v = PortArg(a.OptionValue(t)); prmsA.Add(v); prmsB.Add(v); break;
                        case "--id": n = a.OptionValue(t); break;
                        default:
                            if (n == null && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out _)) n = t;
                            else throw new UsageException($"unexpected argument '{t}'");
                            break;
                    }
                }
                if (n != null && (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out num) || num < 0))
                    throw new UsageException($"invalid pair number '{n}'");

                int i = setup.InstallPair(num, Join(prmsA), Join(prmsB));
                if (i < 0) return false;
                if (json) PrintJson(ui, setup.ListPairs().Where(p => p.Num == i));
                else ui.Print($"Installed pair {i}: {C0C.PortId(true, i)} <-> {C0C.PortId(false, i)}");
                return true;
            }

            case "remove" or "rm":
            {
                var nums = new List<int>();
                while (a.Next() is { } t)
                {
                    if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                        throw new UsageException($"invalid pair number '{t}'");
                    nums.Add(n);
                }
                if (nums.Count == 0) throw new UsageException("missing pair number");
                bool ok = true;
                foreach (int n in nums) ok &= setup.RemovePair(n);
                return ok;
            }

            default:
                throw new UsageException($"unknown pair subcommand '{sub}'");
        }
    }

    /// <summary>"-a COM5" is shorthand for "-a PortName=COM5".</summary>
    private static string PortArg(string v) => v is "-" or "*" || v.Contains('=') ? v : "PortName=" + v;

    private static string Join(List<string> parts)
    {
        if (parts.Count == 0) return "-";
        if (parts.Count == 1) return parts[0];
        return string.Join(',', parts.Where(p => p != "*"));
    }

    // ------------------------------------------------------------------ port

    private static bool Port(string sub, Args a, Com0comSetup setup, ConsoleUi ui, bool json)
    {
        switch (sub)
        {
            case "set":
            {
                string id = a.Required("port identifier (e.g. CNCA0)");
                var prms = new List<string>();
                while (a.Next() is { } t) prms.Add(t);
                if (prms.Count == 0) throw new UsageException("missing parameters, e.g. PortName=COM5 EmuBR=yes");
                string s = prms.Count == 1 && prms[0] is "-" or "*" ? prms[0] : string.Join(',', prms);
                return setup.ChangePort(id.ToUpperInvariant(), s);
            }

            case "reset":
            {
                string id = a.Required("port identifier (e.g. CNCA0)");
                var keys = new List<string>();
                while (a.Next() is { } t) keys.Add(t);
                string s = keys.Count == 0 ? "-" : string.Join(',', keys.Select(k => k + "=-"));
                return setup.ChangePort(id.ToUpperInvariant(), s);
            }

            case "show":
            {
                string id = a.Required("port identifier (e.g. CNCA0)");
                a.End();
                if (!C0C.TryParsePortId(id, out bool isA, out int num))
                    throw new UsageException($"invalid port identifier '{id}'");
                var pair = setup.ListPairs().FirstOrDefault(p => p.Num == num);
                if (pair == null)
                {
                    ui.Print($"Port pair {num} is not installed.");
                    return false;
                }
                var port = pair[isA];
                if (json)
                {
                    ui.Print(Json(w => WritePort(w, port)));
                    return true;
                }
                ui.Print($"{port.Id}  ({(port.Present ? "present" : "not present")})");
                if (port.FriendlyName != null) ui.Print($"  FriendlyName  = {port.FriendlyName}");
                foreach (var p in PortParameters.All)
                {
                    var v = port.Parameters.GetValue(p.Name);
                    if (v == null) continue;
                    ui.Print($"  {p.Name,-13} = {v}{(port.Parameters.IsExplicit(p.Name) ? "" : "  (default)")}");
                }
                return true;
            }

            default:
                throw new UsageException($"unknown port subcommand '{sub}'");
        }
    }

    // ------------------------------------------------------------------ list

    // JSON is written with Utf8JsonWriter (no reflection), so setupcn can be compiled with Native AOT.

    private static string Json(Action<Utf8JsonWriter> write, bool indented = true)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented }))
            write(w);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteStrings(Utf8JsonWriter w, IEnumerable<string> items)
    {
        w.WriteStartArray();
        foreach (var s in items) w.WriteStringValue(s);
        w.WriteEndArray();
    }

    private static void WritePort(Utf8JsonWriter w, PortInfo port)
    {
        w.WriteStartObject();
        w.WriteString("id", port.Id);
        w.WriteString("name", port.DisplayName);
        w.WriteBoolean("present", port.Present);
        w.WriteString("friendlyName", port.FriendlyName);
        w.WriteStartObject("parameters");
        foreach (var p in PortParameters.All)
            if (port.Parameters.GetValue(p.Name) is { } v) w.WriteString(p.Name, v);
        w.WriteEndObject();
        w.WritePropertyName("explicit");
        WriteStrings(w, PortParameters.All.Where(p => port.Parameters.IsExplicit(p.Name)).Select(p => p.Name));
        w.WriteEndObject();
    }

    private static void PrintJson(ConsoleUi ui, IEnumerable<PairInfo> pairs) =>
        ui.Print(Json(w =>
        {
            w.WriteStartArray();
            foreach (var p in pairs)
            {
                w.WriteStartObject();
                w.WriteNumber("num", p.Num);
                w.WritePropertyName("a");
                WritePort(w, p.A);
                w.WritePropertyName("b");
                WritePort(w, p.B);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }));

    private static bool List(Com0comSetup setup, ConsoleUi ui, bool json)
    {
        var pairs = setup.ListPairs();
        if (json)
        {
            PrintJson(ui, pairs);
            return true;
        }
        if (pairs.Count == 0)
        {
            ui.Print("No port pairs installed.");
            return true;
        }
        foreach (var p in pairs)
        {
            foreach (var port in new[] { p.A, p.B })
            {
                string flag = port.Present ? " " : "?";
                ui.Print($"{flag} {port.Id,-7} {port.DisplayName,-10} {port.Parameters.Format(setup.Options.DetailParams)}");
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ misc

    private static (int Seconds, bool Ask) ParseWait(string v)
    {
        bool ask = v.StartsWith('+');
        if (!int.TryParse(ask ? v[1..] : v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int sec))
            throw new UsageException($"invalid wait time '{v}'");
        return (sec, ask);
    }

    private static string DefaultInfDir()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            if (File.Exists(Path.Combine(dir, C0C.InfName))) return dir;
        return AppContext.BaseDirectory;
    }

    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>Minimal argument cursor.</summary>
internal sealed class Args(string[] argv)
{
    private readonly List<string> _rest = [.. argv];
    private int _pos;

    public bool HelpRequested { get; set; }

    /// <summary>Removes recognised global options (anywhere in the line, except after "--").</summary>
    public void TakeOptions(Func<string, bool> handler)
    {
        for (int i = 0; i < _rest.Count;)
        {
            if (_rest[i] == "--") break;
            _pos = i;
            string opt = _rest[i];
            if (opt.StartsWith('-') && opt.Length > 1 && !char.IsDigit(opt[1]))
            {
                _rest.RemoveAt(i);
                if (handler(opt)) continue;
                _rest.Insert(i, opt);
            }
            i++;
        }
        _pos = 0;
    }

    /// <summary>Consumes the value that follows an option at the current position.</summary>
    public string OptionValue(string opt)
    {
        if (_pos >= _rest.Count) throw new UsageException($"option {opt} requires a value");
        var v = _rest[_pos];
        _rest.RemoveAt(_pos);
        return v;
    }

    public string? Next()
    {
        if (_pos < _rest.Count && _rest[_pos] == "--") _rest.RemoveAt(_pos);
        return _pos < _rest.Count ? _rest[_pos++] : null;
    }

    public string? Optional() => Next();

    public string? Peek() => _pos < _rest.Count ? _rest[_pos] : null;

    public bool Contains(string s) => _rest.Skip(_pos).Any(x => x.Equals(s, StringComparison.OrdinalIgnoreCase));

    public string Required(string what) => Next() ?? throw new UsageException($"missing {what}");

    public bool Flag(string name)
    {
        int i = _rest.FindIndex(_pos, s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        _rest.RemoveAt(i);
        return true;
    }

    public void End()
    {
        if (_pos < _rest.Count) throw new UsageException($"unexpected argument '{_rest[_pos]}'");
    }
}
