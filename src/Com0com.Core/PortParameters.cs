using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Com0com.Core;

public enum ParamType { Other, Flag, Pin, Probability, Unsigned }

/// <summary>Description of one per-port driver parameter.</summary>
public sealed record ParamInfo(string Name, ParamType Type, uint Mask, uint Default);

/// <summary>
/// Per-port parameters stored in HKLM\SYSTEM\CurrentControlSet\Services\com0com\Parameters\&lt;CNCxN&gt;.
/// Port of setup/params.cpp: tracks which values are explicitly set (stored in registry)
/// and which were changed since Load().
/// </summary>
public sealed class PortParameters
{
    private const uint MPortName = 0x00000001;
    private const uint MRealPortName = 0x01000000;

    public static readonly IReadOnlyList<ParamInfo> All =
    [
        new("PortName", ParamType.Other, MPortName, 0),
        new("RealPortName", ParamType.Other, MRealPortName, 0),
        new("EmuBR", ParamType.Flag, 0x00000002, 0),
        new("EmuOverrun", ParamType.Flag, 0x00000004, 0),
        new("PlugInMode", ParamType.Flag, 0x00000008, 0),
        new("ExclusiveMode", ParamType.Flag, 0x00000010, 0),
        new("HiddenMode", ParamType.Flag, 0x00000020, 0),
        new("AllDataBits", ParamType.Flag, 0x00000040, 0),
        new("cts", ParamType.Pin, 0x00000100, C0C.PinRRTS),
        new("dsr", ParamType.Pin, 0x00000200, C0C.PinRDTR),
        new("dcd", ParamType.Pin, 0x00000400, C0C.PinRDTR),
        new("ri", ParamType.Pin, 0x00000800, C0C.PinON | C0C.PinNegative),
        new("EmuNoise", ParamType.Probability, 0x00010000, 0),
        new("AddRTTO", ParamType.Unsigned, 0x00020000, 0),
        new("AddRITO", ParamType.Unsigned, 0x00040000, 0),
    ];

    public static readonly IReadOnlyList<(string Name, uint Bit)> PinSources =
    [
        ("rrts", C0C.PinRRTS), ("rdtr", C0C.PinRDTR), ("rout1", C0C.PinROUT1), ("rout2", C0C.PinROUT2),
        ("ropen", C0C.PinROPEN), ("lrts", C0C.PinLRTS), ("ldtr", C0C.PinLDTR), ("lout1", C0C.PinLOUT1),
        ("lout2", C0C.PinLOUT2), ("lopen", C0C.PinLOPEN), ("on", C0C.PinON),
    ];

    public static ParamInfo? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private readonly Dictionary<uint, uint> _dw = [];
    private string _portName = "";
    private string _realPortName = "";
    private uint _maskChanged;
    private uint _maskExplicit;

    public PortParameters(string phPortName)
    {
        PhPortName = phPortName;
        Init();
    }

    private PortParameters(PortParameters other)
    {
        PhPortName = other.PhPortName;
        _dw = new Dictionary<uint, uint>(other._dw);
        _portName = other._portName;
        _realPortName = other._realPortName;
        _maskChanged = other._maskChanged;
        _maskExplicit = other._maskExplicit;
    }

    /// <summary>Port identifier, e.g. CNCA0.</summary>
    public string PhPortName { get; }

    public bool Changed => _maskChanged != 0;

    /// <summary>Effective port name (PortName or the identifier if not set).</summary>
    public string PortName => (_maskExplicit & MPortName) != 0 ? _portName : PhPortName;

    /// <summary>RealPortName (only meaningful for PortName=COM#), or "".</summary>
    public string RealPortName => (_maskExplicit & MRealPortName) != 0 ? _realPortName : "";

    public bool IsExplicit(string name) => Find(name) is { } p && (_maskExplicit & p.Mask) != 0;

    private string RegKeyPath => $@"{C0C.RegKeyServices}\{C0C.Service}\Parameters\{PhPortName}";

    private void Init()
    {
        _portName = "";
        _realPortName = "";
        foreach (var p in All)
            if (p.Type != ParamType.Other) _dw[p.Mask] = 0;
        _maskChanged = 0;
        _maskExplicit = 0;
    }

    /// <summary>Loads parameters from the registry. Missing key means all defaults.</summary>
    public void Load()
    {
        Init();
        using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath, false);
        if (key == null) return;

        if (key.GetValue("PortName") is string pn)
        {
            _portName = pn;
            _maskExplicit |= MPortName;
        }

        foreach (var p in All)
        {
            if (p.Type == ParamType.Other) continue;
            if (key.GetValue(p.Name) is int v)
            {
                _dw[p.Mask] = unchecked((uint)v);
                _maskExplicit |= p.Mask;
            }
        }
    }

    /// <summary>Writes changed values back to the registry.</summary>
    public void Save()
    {
        if (!Changed) return;
        using var key = Registry.LocalMachine.CreateSubKey(RegKeyPath, true);

        if ((_maskChanged & MPortName) != 0)
        {
            if ((_maskExplicit & MPortName) != 0) key.SetValue("PortName", _portName, RegistryValueKind.String);
            else key.DeleteValue("PortName", false);
            _maskChanged &= ~MPortName;
        }

        foreach (var p in All)
        {
            if (p.Type == ParamType.Other || (_maskChanged & p.Mask) == 0) continue;
            if ((_maskExplicit & p.Mask) != 0) key.SetValue(p.Name, unchecked((int)_dw[p.Mask]), RegistryValueKind.DWord);
            else key.DeleteValue(p.Name, false);
            _maskChanged &= ~p.Mask;
        }
    }

    /// <summary>
    /// Sets RealPortName as already present (from the Ports class device key), without marking it changed.
    /// Passing "" forgets it, so any RealPortName given in a parameter string is ignored.
    /// </summary>
    public void InitRealPortName(string realPortName = "")
    {
        if (realPortName.Length == 0)
        {
            _maskExplicit &= ~MRealPortName;
            _maskChanged &= ~MRealPortName;
            return;
        }
        _realPortName = realPortName.ToUpperInvariant();
        _maskChanged &= ~MRealPortName;
        _maskExplicit |= MRealPortName;
    }

    /// <summary>
    /// Applies a parameter string: "-" (all defaults), "*" (no change) or "Key=Val[,Key=Val...]".
    /// Values "-" (default) and "*" (keep) are accepted for any key. The change is atomic.
    /// </summary>
    public bool TryApply(string parameters, out string? error)
    {
        var tmp = new PortParameters(this);
        error = null;

        if (parameters is "-" or "*")
        {
            foreach (var p in All)
                if (!tmp.SetValue(p, parameters, out error)) return false;
        }
        else
        {
            foreach (var item in parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0 || eq == item.Length - 1)
                {
                    error = $"Missing value for '{item}'";
                    return false;
                }
                var p = Find(item[..eq].Trim());
                if (p == null)
                {
                    error = $"Invalid parameter '{item[..eq]}'";
                    return false;
                }
                if (!tmp.SetValue(p, item[(eq + 1)..].Trim(), out error)) return false;
            }
        }

        _dw.Clear();
        foreach (var kv in tmp._dw) _dw[kv.Key] = kv.Value;
        _portName = tmp._portName;
        _realPortName = tmp._realPortName;
        _maskChanged = tmp._maskChanged;
        _maskExplicit = tmp._maskExplicit;
        return true;
    }

    public void Apply(string parameters)
    {
        if (!TryApply(parameters, out var error)) throw new SetupException(error!);
    }

    private bool SetValue(ParamInfo p, string val, out string? error)
    {
        error = null;
        if (val == "*") return true;
        if (val == "-")
        {
            if ((_maskExplicit & p.Mask) != 0)
            {
                _maskExplicit &= ~p.Mask;
                _maskChanged |= p.Mask;
            }
            return true;
        }

        switch (p.Type)
        {
            case ParamType.Other when p.Mask == MPortName:
                if (val.Length == 0) { error = $"Invalid port name '{val}'"; return false; }
                if ((_maskExplicit & MPortName) == 0 || !string.Equals(_portName, val, StringComparison.OrdinalIgnoreCase))
                {
                    _portName = val.ToUpperInvariant();
                    _maskChanged |= MPortName;
                }
                break;
            case ParamType.Other:
                if (val.Length == 0) { error = $"Invalid real port name '{val}'"; return false; }
                if ((_maskExplicit & MRealPortName) == 0 || !string.Equals(_realPortName, val, StringComparison.OrdinalIgnoreCase))
                {
                    _realPortName = val.ToUpperInvariant();
                    _maskChanged |= MRealPortName;
                }
                break;
            case ParamType.Flag:
                uint flag;
                if (val.Equals("yes", StringComparison.OrdinalIgnoreCase)) flag = 0xFFFFFFFF;
                else if (val.Equals("no", StringComparison.OrdinalIgnoreCase)) flag = 0;
                else { error = $"Invalid value '{val}' for {p.Name} (expected yes or no)"; return false; }
                SetDw(p.Mask, flag);
                break;
            case ParamType.Pin:
                if (!TryParsePin(val, out uint pin)) { error = $"Invalid value '{val}' for {p.Name}"; return false; }
                SetDw(p.Mask, pin);
                break;
            case ParamType.Probability:
                if (!TryParseProbability(val, out uint prob, out error)) return false;
                SetDw(p.Mask, prob);
                break;
            case ParamType.Unsigned:
                if (val.Length == 0 || !val.All(char.IsAsciiDigit) ||
                    !uint.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out uint u))
                {
                    error = $"Invalid value '{val}' for {p.Name}";
                    return false;
                }
                SetDw(p.Mask, u);
                break;
        }

        // Original semantics: a value becomes explicit only if it actually changed.
        if ((_maskChanged & p.Mask) != 0) _maskExplicit |= p.Mask;
        return true;
    }

    private void SetDw(uint mask, uint value)
    {
        if (_dw[mask] != value)
        {
            _dw[mask] = value;
            _maskChanged |= mask;
        }
    }

    public static bool TryParsePin(string val, out uint pin)
    {
        pin = 0;
        if (val.StartsWith('!'))
        {
            pin = C0C.PinNegative;
            val = val[1..];
        }
        foreach (var (name, bit) in PinSources)
        {
            if (val.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                pin |= bit;
                return true;
            }
        }
        return false;
    }

    public static string? FormatPin(uint pin)
    {
        uint src = pin & ~C0C.PinNegative;
        foreach (var (name, bit) in PinSources)
            if (bit == src) return ((pin & C0C.PinNegative) != 0 ? "!" : "") + name;
        return null;
    }

    private static bool TryParseProbability(string val, out uint result, out string? error)
    {
        result = 0;
        error = null;
        if (val.StartsWith("0.", StringComparison.Ordinal))
        {
            string digits = val[2..].TrimEnd('0');
            if (digits.Length > 8)
            {
                error = $"Too long value '{val}'";
                return false;
            }
            if (!digits.All(char.IsAsciiDigit))
            {
                error = $"Invalid value '{val}'";
                return false;
            }
            result = digits.Length == 0 ? 0 : uint.Parse(digits.PadRight(8, '0'), CultureInfo.InvariantCulture);
            return true;
        }
        if (val == "0") return true;
        error = $"Invalid value '{val}' (expected 0 - 0.99999999)";
        return false;
    }

    private static string FormatProbability(uint v)
    {
        if (v == 0) return "0";
        return "0." + v.ToString("D8", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    /// <summary>Formatted value of one parameter, or null if not explicit and <paramref name="withDefault"/> is false.</summary>
    public string? GetValue(string name, bool withDefault = true)
    {
        var p = Find(name) ?? throw new ArgumentException($"Unknown parameter {name}");
        bool isExplicit = (_maskExplicit & p.Mask) != 0;
        if (p.Mask == MPortName) return isExplicit ? _portName : withDefault ? PhPortName : null;
        if (p.Mask == MRealPortName) return isExplicit ? _realPortName : null;
        if (!isExplicit && !withDefault) return null;
        uint v = isExplicit ? _dw[p.Mask] : p.Default;
        return p.Type switch
        {
            ParamType.Flag => v != 0 ? "yes" : "no",
            ParamType.Pin => FormatPin(v),
            ParamType.Probability => FormatProbability(v),
            ParamType.Unsigned => v.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>Parameter string like the original setupc output.</summary>
    public string Format(bool detail)
    {
        var sb = new StringBuilder();
        sb.Append("PortName=").Append((_maskExplicit & MPortName) != 0 ? _portName : detail ? PhPortName : "-");
        if ((_maskExplicit & MRealPortName) != 0) sb.Append(",RealPortName=").Append(_realPortName);
        foreach (var p in All)
        {
            if (p.Type == ParamType.Other) continue;
            if ((_maskExplicit & p.Mask) == 0 && !detail) continue;
            var v = GetValue(p.Name);
            if (v != null) sb.Append(',').Append(p.Name).Append('=').Append(v);
        }
        return sb.ToString();
    }

    public override string ToString() => Format(false);

    public const string Help = """
        Port parameters (Key=Value, separate several with ',' or spaces):
          PortName=<name>         port name (default: the port identifier, e.g. CNCA0)
                                  COM# lets the Ports class installer pick a COM<n> name
          RealPortName=COM<n>     rename a PortName=COM# port to COM<n>
          EmuBR=yes|no            emulate baud rate towards the paired port (no)
          EmuOverrun=yes|no       emulate buffer overrun (no)
          EmuNoise=<p>            probability 0-0.99999999 of a corrupted frame (0)
          AddRTTO=<ms>            add <ms> to the read total time-out (0)
          AddRITO=<ms>            add <ms> to the read interval time-out (0)
          PlugInMode=yes|no       port is hidden until the paired port is open (no)
          ExclusiveMode=yes|no    port is hidden while it is open (no)
          HiddenMode=yes|no       hide port from port enumerators (no)
          AllDataBits=yes|no      transfer all data bits regardless of byte size (no)
          cts=[!]<pin>            wire CTS to <pin> (rrts)
          dsr=[!]<pin>            wire DSR to <pin> (rdtr)
          dcd=[!]<pin>            wire DCD to <pin> (rdtr)
          ri=[!]<pin>             wire RI to <pin> (!on)

        <pin>: rrts lrts rdtr ldtr rout1 lout1 rout2 lout2 (remote/local RTS DTR OUT1 OUT2),
               ropen lopen (ON while remote/local port is open), on. '!' inverts.
        Special values: '-' = driver default, '*' = keep current.
        A whole parameter string of '-' resets everything, '*' keeps everything.
        """;
}
