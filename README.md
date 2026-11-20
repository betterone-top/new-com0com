# NewVirtualCOMPair

Virtual serial port pairs for Windows: new setup tools (setupc / setupcn / setupg) for the com0com driver.

A rewrite of the setup and configuration tools of [com0com](https://sourceforge.net/projects/com0com/) 3.0
in C# (.NET 10) by **Big Mouse**. **The driver itself is unchanged** (com0com.sys/.inf/.cat in `driver/`).

> **A hobby project — built for fun, use at your own risk.** It comes with no warranty of any kind
> (see the license). Installing drivers changes your system; try it on a machine you don't mind fixing.

> **Unofficial project.** com0com (virtual serial port pairs / null-modem emulator) is developed by
> Vyacheslav Frolov. This repository is a third-party rewrite of its setup tools (setupc / setupg) and is
> not affiliated with the original author. The driver binaries and driver sources come from the original
> project without any modification.

| Path | Contents |
|---|---|
| `src/Com0com.Core` | Core library: feature-by-feature port of the original `setup/*.cpp` (SetupAPI / CfgMgr32 / msports / newdev P/Invoke) |
| `src/setupc` | `setupc.exe`: **identical usage** to the original setupc (commands, options, output format, help, interactive mode, message boxes, exit codes) |
| `src/setupcn` | `setupcn.exe`: command line tool with a new syntax (noun verb, JSON output, console prompts, see below) |
| `src/setupg` | GUI `setupg.exe`: replica of the original setupg window (Avalonia, UI built in code, calls the core library directly) |
| `tests/Com0com.Core.Tests` | Dependency-free tests (parameter parsing/formatting, COM names, wildcards, ...): `dotnet run --project tests/Com0com.Core.Tests` |
| `driver/` | Signed driver released by the original project (com0com 3.0.0.0, x64), copied to the output on build; the files must stay byte-for-byte identical (signature check) |
| `src_orig/` | Original project sources (including the driver in `sys/` and the original setup), extracted from the original CVS repository with `tools/rcs_head.py`; kept for reference and to satisfy the GPL source requirement |
| `tools/system-test.ps1` | End-to-end test against the real driver (requires admin; temporarily installs and removes test port pairs) |
| `tools/make-icon.ps1` | Regenerates the application icons (`src/*.ico`, 16-256 px) |

## Build

```powershell
./build.ps1                      # run tests + publish to .\dist (nothing needs to be installed on the target)
./build.ps1 -FrameworkDependent  # small build (a few hundred KB) that needs the .NET 10 Desktop Runtime on the target
```

The version is the UTC build time, `year.month.day.HHmm` without leading zeros (e.g. `26.11.20.1526`,
`26.1.5.930`); `build.ps1` stamps all programs and the installer with the same value
(`setupcn version` and the GUI's About box show it).

Default output:

| File | Build | Size |
|---|---|---|
| `setupc.exe` | Native AOT (native code) | about 1.7 MB |
| `setupcn.exe` | Native AOT (native code) | about 1.8 MB |
| `setupg.exe` + `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll` | Native AOT (Avalonia) | about 32 MB in total |

Copy the whole `dist/` directory (including the driver files) to the target machine. For command line use
only, `setupc.exe` (or `setupcn.exe`) plus the driver files is enough.

The build machine needs the .NET 10 SDK and the Visual Studio "Desktop development with C++" workload
(used by the Native AOT linker).
Target platform: Windows 10/11 x64 (a 32-bit process can't install drivers on 64-bit Windows).

## Installer

```powershell
./build.ps1 -Installer     # also builds release\NewVirtualCOMPair-setup-<version>-x64.exe (Inno Setup 6/7)
```

The installer (English / Simplified Chinese, Windows 10/11 x64, admin) does what the original
NSIS package did:

- copies the files to `Program Files\com0com` and runs `setupc preinstall`, `update`, `infclean`;
- optionally installs a port pair: `COM# <-> COM#` (Windows picks free COM numbers, default) and/or
  `CNCA0 <-> CNCB0`; on upgrades no pairs are added;
- creates Start menu shortcuts (and optionally a desktop shortcut);
- `/SILENT` / `/VERYSILENT` installs run `setupc --silent`.

The setupc output is written to `setup.log` in the installation directory.
**Uninstalling runs `setupc uninstall`, which removes all port pairs and the com0com driver** (as the
original uninstaller did).

## setupc (original-compatible)

Same usage as `setupc.exe` of the original com0com 3.0, so existing scripts keep working, for example:

```
setupc install PortName=COM5,EmuBR=yes -
setupc --silent install 5 * *
setupc change CNCA0 EmuBR=yes,EmuOverrun=yes
setupc list
setupc remove 0
setupc busynames COM?*
```

See `setupc help` for the full reference. Questions are shown as Windows message boxes (none with
`--silent`); exit code 0 = success, 1 = failure.

## setupcn (new syntax)

```
setupcn list                               list all port pairs
setupcn add -a COM5 -b COM6                install a pair COM5 <-> COM6
setupcn add 3                              install CNCA3 <-> CNCB3
setupcn add -a COM# -b COM#                let Windows (Ports class) assign the COM numbers
setupcn set CNCA0 PortName=COM10 EmuBR=yes change parameters (separate several with spaces or commas)
setupcn set CNCA0 RealPortName=COM12       rename a COM# port
setupcn reset CNCA0 EmuBR                  reset a parameter to the driver default (no parameter = all)
setupcn show CNCA0                         show all parameters of a port (including defaults)
setupcn rm 0                               remove a port pair
setupcn driver install|update|reload|preinstall|uninstall|clean
setupcn enable | disable
setupcn names busy COM?*                   names already in use
setupcn names friendly [--update]          show/update device friendly names
setupcn help [params|examples]             help; "examples" includes a mapping from the original commands
```

Global options: `-s/--silent` (for scripts, never asks), `-w/--wait [+]seconds`, `--json`, `--detail`,
`--inf-dir <dir>`, `--no-update`, `--no-fnames`, `-o/--output <file>`.
Run without arguments for an interactive shell.

Exit codes: 0 success, 1 failed/cancelled, 2 usage error, 3010 success but a reboot is required.

Read-only commands (list / show / names busy / names friendly) work without admin rights; commands that
change something must be run from an elevated prompt. setupg asks for admin rights (UAC) on start.

## Differences from the original

- `setupc` keeps the original usage; the tool with the new noun/verb syntax is `setupcn`
  (see `setupcn help examples` for the mapping from the original commands). The setupc message boxes are
  titled "Setup for NewVirtualCOMPair" (original: "Setup for com0com").
- setupg replicates the original window (pair tree, mirrored options, pin map with drag-to-wire and
  double-click inverters, changed values in blue) using Avalonia, and calls the core library directly instead
  of running setupc. Additions: hovering a pin highlights its wires, and the context menu of the pair tree has
  driver maintenance, a log window and About.
- In silent mode, when the requested pair number is already in use, installation continues with another
  number (the original fails).
- Uninstalling deletes the driver files listed in the INF directly from `%SystemRoot%\System32\drivers`.
- When a reboot is required: `setupc` shows the system reboot prompt like the original; `setupcn` exits
  with 3010; the GUI asks whether to restart now.

## License

Copyright (c) 2026 Big Mouse.

This project is licensed under the **GNU General Public License v3.0 or later**, see [LICENSE](LICENSE).

- The core library is a port of the original com0com `setup/*.cpp` (a derivative work). The original is
  licensed "GPL v2 or (at your option) any later version", which allows this rewrite to use GPL v3 or later.
- The com0com driver in `driver/` and the original sources in `src_orig/` are
  Copyright (c) 2004-2012 Vyacheslav Frolov and remain under their original license,
  GPL v2 or later (see `src_orig/license.txt`).
- Avalonia (MIT) is referenced via NuGet.

### Additional term (GPL v3 section 7(b))

For the code written for this rewrite (everything outside `driver/` and `src_orig/`), the following
additional term applies under section 7(b) of the GNU GPL v3:

> Conveyed copies and modified versions must preserve the author attribution
> **"NewVirtualCOMPair by Big Mouse"** in the About dialog of the GUI (or an equivalent place shown to the
> user) and in the README or other documentation that accompanies the program.
