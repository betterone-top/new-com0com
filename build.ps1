<#
.SYNOPSIS
  Builds setupc.exe, setupcn.exe and setupg.exe and copies them together with the driver files into .\dist.

.DESCRIPTION
  By default nothing has to be installed on the target machine (Windows 10/11 x64):
    setupc.exe  Native AOT (~2 MB), same commands and output as the original com0com setupc
    setupcn.exe     Native AOT (~2 MB), command line tool with the new syntax
    setupg.exe  Native AOT Avalonia app (~15 MB) + libSkiaSharp.dll, libHarfBuzzSharp.dll, av_libglesv2.dll
  Native AOT needs Visual Studio with the "Desktop development with C++" workload on the build machine.

.PARAMETER FrameworkDependent
  Build small executables that need the .NET 10 Desktop Runtime on the target machine.

.PARAMETER Installer
  Also build the Inno Setup installer into .\release (needs Inno Setup 6/7; ISCC.exe is searched in
  the usual places, or set the INNO_ISCC environment variable).

.PARAMETER SkipTests
  Don't run the core unit tests.
#>
param(
    [switch]$FrameworkDependent,
    [switch]$Installer,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

if (-not $SkipTests) {
    dotnet run --project (Join-Path $root 'tests\Com0com.Core.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

# Empty dist rather than deleting it, so an Explorer window or shell sitting in it doesn't block the build.
if (Test-Path $dist) { Get-ChildItem $dist -Force | Remove-Item -Recurse -Force }

# One version for everything built by this run: UTC build time as year.month.day.HHmm without
# leading zeros (e.g. 26.11.20.1526, 26.1.5.930).
$now = (Get-Date).ToUniversalTime()
$version = '{0}.{1}.{2}.{3}' -f ($now.Year % 100), $now.Month, $now.Day, ($now.Hour * 100 + $now.Minute)
"Version $version"

$common = @('-c', 'Release', '-r', 'win-x64', '-o', $dist, '-p:DebugType=none', '-nologo', "-p:Version=$version")

if ($FrameworkDependent) {
    $cli = $common + @('-p:SelfContained=false', '-p:PublishSingleFile=true')
    $gui = $cli + @('-p:IncludeNativeLibrariesForSelfExtract=true')
} else {
    # The AOT toolchain's vcvarsall (VS 2026) calls vswhere.exe through PATH.
    $vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$vsInstaller;$env:PATH" }

    $cli = $gui = $common + @('-p:SelfContained=true', '-p:PublishAot=true', '-p:OptimizationPreference=Size')
}

foreach ($p in @(@('setupc', $cli), @('setupcn', $cli), @('setupg', $gui))) {
    $name, $args_ = $p
    dotnet publish (Join-Path $root "src\$name\$name.csproj") @args_
    if ($LASTEXITCODE -ne 0) { throw "Publish of $name failed (Native AOT needs Visual Studio with the C++ workload)" }
}

# Native debug symbols shipped in the Skia/HarfBuzz packages aren't needed for distribution.
Remove-Item (Join-Path $dist '*.pdb') -ErrorAction SilentlyContinue

Get-ChildItem $dist | Format-Table Name, Length

if ($Installer) {
    $iscc = @($env:INNO_ISCC,
              'D:\ProgramData\Inno Setup 7\ISCC.exe',
              "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
              "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'ISCC.exe (Inno Setup) not found; set INNO_ISCC to its path' }
    & $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\NewVirtualCOMPair.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
    Get-ChildItem (Join-Path $root 'release') | Format-Table Name, Length
}
