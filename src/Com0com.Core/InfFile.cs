using System.Runtime.InteropServices;
using System.Text;
using Com0com.Core.Native;
using static Com0com.Core.Native.NativeMethods;

namespace Com0com.Core;

/// <summary>Required (section, key, field, value) match used to recognise our INF files.</summary>
internal sealed record InfField(string Section, string? Key, uint Field, string Value);

/// <summary>Port of setup/inffile.cpp.</summary>
internal sealed class InfFile : IDisposable
{
    private readonly ISetupUi _ui;
    private IntPtr _hInf = INVALID_HANDLE_VALUE;
    private string? _oemPath;

    public InfFile(ISetupUi ui, string path)
    {
        _ui = ui;
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public void Dispose() => Close();

    private void Close()
    {
        if (_hInf != INVALID_HANDLE_VALUE)
        {
            SetupCloseInfFile(_hInf);
            _hInf = INVALID_HANDLE_VALUE;
        }
    }

    private bool Open(bool showErrors)
    {
        if (_hInf != INVALID_HANDLE_VALUE) return true;
        while (true)
        {
            _hInf = SetupOpenInfFileW(Path, null, INF_STYLE_WIN4, out uint errLine);
            if (_hInf != INVALID_HANDLE_VALUE) return true;
            if (!showErrors) return false;
            if (_ui.ShowLastError(Buttons.RetryCancel, $"SetupOpenInfFile({Path}) on line {errLine}") != Choice.Retry)
                return false;
        }
    }

    private static string? GetField(ref INFCONTEXT ctx, uint field)
    {
        if (!SetupGetStringFieldW(ref ctx, field, null, 0, out uint size)) return null;
        var sb = new StringBuilder((int)size);
        return SetupGetStringFieldW(ref ctx, field, sb, size, out _) ? sb.ToString() : null;
    }

    /// <summary>True if every field matches (case-insensitive).</summary>
    public bool Test(IEnumerable<InfField> fields, bool showErrors = true)
    {
        if (!Open(showErrors)) return false;
        foreach (var f in fields)
        {
            if (!SetupFindFirstLineW(_hInf, f.Section, f.Key, out var ctx)) return false;
            while (true)
            {
                var v = GetField(ref ctx, f.Field);
                if (v == null) return false;
                if (v.Equals(f.Value, StringComparison.OrdinalIgnoreCase)) break;
                if (!SetupFindNextMatchLineW(ref ctx, f.Key, out ctx)) return false;
            }
        }
        return true;
    }

    /// <summary>[Version] value with string substitution, or null.</summary>
    public string? Version(string key)
    {
        if (!Open(false)) return null;
        return SetupFindFirstLineW(_hInf, "Version", key, out var ctx) ? GetField(ref ctx, 1) : null;
    }

    /// <summary>Files listed in a CopyFiles section.</summary>
    public List<string> SectionFiles(string section)
    {
        var list = new List<string>();
        if (!Open(true) || !SetupFindFirstLineW(_hInf, section, null, out var ctx)) return list;
        do
        {
            var f = GetField(ref ctx, 1);
            if (!string.IsNullOrEmpty(f)) list.Add(f);
        } while (SetupFindNextMatchLineW(ref ctx, null, out ctx));
        return list;
    }

    /// <summary>Path of the installed copy in %windir%\INF (oemNN.inf), or null if not installed.</summary>
    public string? OemPath
    {
        get
        {
            if (_oemPath == null)
            {
                var sb = new StringBuilder(260);
                if (SetupCopyOEMInfW(Path, null, SPOST_NONE, SP_COPY_REPLACEONLY | SP_COPY_NOOVERWRITE,
                        sb, (uint)sb.Capacity, IntPtr.Zero, IntPtr.Zero) ||
                    Marshal.GetLastWin32Error() == ERROR_FILE_EXISTS)
                {
                    _oemPath = sb.ToString();
                }
            }
            return string.IsNullOrEmpty(_oemPath) ? null : _oemPath;
        }
    }

    public bool InstallOemInf()
    {
        bool wasInstalled = OemPath != null;
        if (!SetupCopyOEMInfW(Path, null, SPOST_PATH, 0, null, 0, IntPtr.Zero, IntPtr.Zero))
        {
            _ui.ShowLastError(Buttons.Ok, $"SetupCopyOEMInf({Path})");
            return false;
        }
        _oemPath = null;
        _ui.Line($"{(wasInstalled ? "Re-installed" : "Installed")} {Path} to {OemPath}");
        return true;
    }

    public bool UninstallOemInf()
    {
        var oem = OemPath;
        if (oem == null)
        {
            _ui.Line($"File {Path} not installed");
            return true;
        }
        return UninstallInf(_ui, oem);
    }

    /// <summary>Deletes the driver files listed in <paramref name="section"/> from %windir%\System32\drivers.</summary>
    public bool UninstallFiles(string section)
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\drivers";
        foreach (var file in SectionFiles(section))
        {
            string target = System.IO.Path.Combine(dir, file);
            while (true)
            {
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                        _ui.Line($"Deleted File {target}");
                    }
                    else
                    {
                        _ui.Line($"File {target} not installed");
                    }
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    var r = _ui.Show(Buttons.CancelTryContinue, $"Delete({target})\nERROR: {e.Message}", warning: true);
                    if (r == Choice.TryAgain) continue;
                    if (r == Choice.Continue) break;
                    return false;
                }
            }
        }
        return true;
    }

    private static bool UninstallInf(ISetupUi ui, string path)
    {
        string name = System.IO.Path.GetFileName(path);
        while (true)
        {
            if (SetupUninstallOEMInfW(name, 0, IntPtr.Zero))
            {
                ui.Line($"Uninstalled {name}");
                return true;
            }
            if (ui.ShowLastError(Buttons.CancelTryContinue, $"SetupUninstallOEMInf({name})") != Choice.TryAgain)
                return false;
        }
    }

    /// <summary>Scans %windir%\INF\oem*.inf and uninstalls those matching one of the rule sets.</summary>
    public static bool UninstallAll(ISetupUi ui, IReadOnlyList<(InfField[] Fields, bool AskUser)> rules, ICollection<string>? exclude)
    {
        ui.Write("Scan INF files .");
        if (!SetupGetInfFileListW(null, INF_STYLE_WIN4, null, 0, out uint size))
        {
            ui.Line(" FAIL");
            ui.ShowLastError(Buttons.Ok, "SetupGetInfFileList()");
            return false;
        }
        size += 256;
        var buf = new char[size];
        if (!SetupGetInfFileListW(null, INF_STYLE_WIN4, buf, size, out _))
        {
            ui.Line(" FAIL");
            ui.ShowLastError(Buttons.Ok, "SetupGetInfFileList()");
            return false;
        }
        ui.Write("...");

        string infDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        var files = new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => f.StartsWith("oem", StringComparison.OrdinalIgnoreCase)).ToList();
        int step = Math.Max(1, files.Count / 3), i = 0;

        foreach (var f in files)
        {
            if (++i % step == 0) ui.Write(".");
            string path = System.IO.Path.Combine(infDir, f);
            if (exclude != null && exclude.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;

            bool doUninstall;
            using (var inf = new InfFile(ui, path))
            {
                if (rules.Any(r => !r.AskUser && inf.Test(r.Fields, false)))
                {
                    doUninstall = true;
                }
                else if (rules.Any(r => r.AskUser && inf.Test(r.Fields, false)))
                {
                    var res = ui.Show(Buttons.YesNo,
                        $"The file {path} possible should be deleted too.\n\n{path}:\n" +
                        $"  ClassGUID = {inf.Version("ClassGUID")}\n" +
                        $"  Class = {inf.Version("Class")}\n" +
                        $"  Provider = {inf.Version("Provider")}\n" +
                        $"  DriverVer = {inf.Version("DriverVer")}\n" +
                        $"  UninstallInfTag = {inf.Version("UninstallInfTag")}\n\n" +
                        "Would you like to delete it?\n");
                    doUninstall = res == Choice.Yes;
                }
                else
                {
                    doUninstall = false;
                }
            }

            if (doUninstall)
            {
                ui.Line();
                UninstallInf(ui, path);
            }
        }
        ui.Line(" done.");
        return true;
    }
}
