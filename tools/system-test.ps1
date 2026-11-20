<#
.SYNOPSIS
  End-to-end test of setupc.exe (original syntax) and setupcn.exe (new syntax) against the real driver.
  Must run elevated. Installs temporary pairs 90-93 (COM240-COM243), removes them again and checks
  that the pre-existing pairs are unchanged. All commands run silently (no dialogs).

.PARAMETER Dist
  Directory with setupc.exe, setupcn.exe and the driver files (default: ..\dist).

.PARAMETER Log
  Result log file (default: system-test.log next to this script).
#>
param(
    [string]$Dist = (Join-Path $PSScriptRoot '..\dist'),
    [string]$Log = (Join-Path $PSScriptRoot 'system-test.log')
)
$ErrorActionPreference = 'Continue'
$Dist = (Resolve-Path $Dist).Path
$setupc = Join-Path $Dist 'setupc.exe'
$setupcn = Join-Path $Dist 'setupcn.exe'
Set-Location $Dist
"system test $(Get-Date -Format s)  dist=$Dist" | Set-Content $Log -Encoding utf8

$script:pass = 0
$script:fail = 0

function Out-Log([string]$text) { $text | Add-Content $Log -Encoding utf8 }

function Invoke-Tool([string]$exe, [string[]]$argv) {
    $out = & $exe @argv 2>&1 | Out-String
    return [pscustomobject]@{ Rc = $LASTEXITCODE; Out = $out }
}

# Runs a command, checks the exit code and an optional condition; logs everything.
function Step([string]$title, [string]$exe, [string[]]$argv, [int[]]$rc = @(0), [scriptblock]$check = $null) {
    $r = Invoke-Tool $exe $argv
    $ok = $rc -contains $r.Rc
    $why = if ($ok) { '' } else { "exit code $($r.Rc), expected $($rc -join '/')" }
    if ($ok -and $check) {
        try { $res = & $check $r.Out } catch { $res = "exception: $_" }
        if ($res -ne $true) { $ok = $false; $why = if ($res -is [string]) { $res } else { 'check failed' } }
    }
    if ($ok) { $script:pass++ } else { $script:fail++ }
    Out-Log ("`n[{0}] {1}`n> {2} {3}`n(rc={4}) {5}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $title,
        (Split-Path $exe -Leaf), ($argv -join ' '), $r.Rc, $why)
    Out-Log ($r.Out.TrimEnd())
}

function ListText { (Invoke-Tool $setupc @('list')).Out }
function Busy([string]$pattern) { (Invoke-Tool $setupc @('busynames', $pattern)).Out }
function Show([string]$id) { (Invoke-Tool $setupcn @('show', $id, '--json')).Out | ConvertFrom-Json }
function Has([string]$text, [string]$pattern) { if ($text -match $pattern) { $true } else { "missing /$pattern/" } }
function HasNot([string]$text, [string]$pattern) { if ($text -notmatch $pattern) { $true } else { "unexpected /$pattern/" } }

$pending = Get-PnpDevice | Where-Object { $_.HardwareID -contains 'root\com0com' } | Where-Object {
    ((Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_DevNodeStatus').Data -band 0x100) -ne 0 }
Out-Log ("devices already waiting for a reboot: " + $(if ($pending) { ($pending.InstanceId -join ', ') } else { 'none' }))
$baseline = ListText
Out-Log "`n--- baseline list ---`n$($baseline.TrimEnd())"

# ------------------------------------------------------------------ setupc (original syntax)
Out-Log "`n================ setupc ================"
Step 'install pair 90 with defaults' $setupc @('--silent', '--wait', '30', 'install', '90', '-', '-') -check {
    $l = ListText; if ($l -match 'CNCA90 PortName=-' -and $l -match 'CNCB90 PortName=-') { $true } else { 'pair 90 not listed' } }
Step 'change CNCA90 flags and pin' $setupc @('--silent', 'change', 'CNCA90', 'EmuBR=yes,EmuOverrun=yes,cts=ropen') -check {
    param($o)
    if ($o -notmatch 'change CNCA90 ') { return 'no "change" line' }
    Has (ListText) 'CNCA90 PortName=-,EmuBR=yes,EmuOverrun=yes,cts=ropen' }
Step 'change CNCA90 PortName=COM240' $setupc @('--silent', '--wait', '30', 'change', 'CNCA90', 'PortName=COM240') -check {
    $r = Has (ListText) 'CNCA90 PortName=COM240,'; if ($r -ne $true) { return $r }
    Has (Busy 'COM240') 'COM240' }
Step 'change CNCA90 PortName=- (releases COM240)' $setupc @('--silent', '--wait', '30', 'change', 'CNCA90', 'PortName=-') -check {
    $r = Has (ListText) 'CNCA90 PortName=-,'; if ($r -ne $true) { return $r }
    HasNot (Busy 'COM240') 'COM240' }
Step 'change with invalid parameter (original: reported, exit 0)' $setupc @('--silent', 'change', 'CNCA90', 'Foo=1') -check {
    param($o) Has $o 'Invalid parameter' }
Step 'remove 90' $setupc @('--silent', '--wait', '30', 'remove', '90') -check { HasNot (ListText) 'CNC.90 ' }
Step 'remove 90 again (original: silent failure)' $setupc @('--silent', 'remove', '90') -rc 1 -check {
    param($o) if ($o.Trim().Length -eq 0) { $true } else { 'expected no output' } }
Step 'install 90 * * (help example)' $setupc @('--silent', '--wait', '30', 'install', '90', '*', '*') -check { Has (ListText) 'CNCA90 PortName=-' }
Step 'remove 90' $setupc @('--silent', '--wait', '30', 'remove', '90') -check { HasNot (ListText) 'CNC.90 ' }
Step 'install 91 with --no-update' $setupc @('--silent', '--no-update', 'install', '91', 'PortName=COM241', '-') -check { Has (ListText) 'CNCA91 PortName=COM241' }
Step 'install (driver for --no-update pairs)' $setupc @('--silent', '--wait', '30', 'install')
Step 'remove 91' $setupc @('--silent', '--wait', '30', 'remove', '91') -check {
    $r = HasNot (ListText) 'CNC.91 '; if ($r -ne $true) { return $r }
    HasNot (Busy 'COM241') 'COM241' }
Step 'disable all' $setupc @('--silent', 'disable', 'all')
Step 'enable all' $setupc @('--silent', '--wait', '30', 'enable', 'all')
Step 'reload' $setupc @('--silent', '--wait', '30', 'reload')
Step 'invalid command' $setupc @('remove', 'x') -rc 1 -check { param($o) Has $o 'Invalid command' }

# ------------------------------------------------------------------ setupcn (new syntax)
# setupcn exits 3010 when Windows reports a pending reboot (e.g. a com0com device flagged DN_NEED_RESTART).
$ok = @(0, 3010)
Out-Log "`n================ setupcn ================"
Step 'add 92 COM242/COM243' $setupcn @('-s', '-w', '30', 'add', '92', '-a', 'COM242', '-b', 'COM243') -rc $ok -check {
    $a = Show 'CNCA92'; $b = Show 'CNCB92'
    if ($a.name -ne 'COM242' -or $b.name -ne 'COM243') { return "names $($a.name)/$($b.name)" }
    if (-not $a.present) { return 'CNCA92 not present' }
    Has (Busy 'COM24?') 'COM242' }
Step 'set CNCB92 EmuBR=yes cts=ropen' $setupcn @('-s', '-w', '30', 'set', 'CNCB92', 'EmuBR=yes', 'cts=ropen') -rc $ok -check {
    $b = Show 'CNCB92'; if ($b.parameters.EmuBR -eq 'yes' -and $b.parameters.cts -eq 'ropen') { $true } else { 'not applied' } }
Step 'reset CNCB92 EmuBR' $setupcn @('-s', '-w', '30', 'reset', 'CNCB92', 'EmuBR') -rc $ok -check {
    $b = Show 'CNCB92'
    if ($b.explicit -contains 'EmuBR') { return 'EmuBR still explicit' }
    if ($b.parameters.cts -ne 'ropen') { return 'cts lost' }
    $true }
Step 'port reset CNCB92 (all)' $setupcn @('-s', '-w', '30', 'port', 'reset', 'CNCB92') -rc $ok -check {
    $b = Show 'CNCB92'; if ($b.explicit.Count -eq 0 -and $b.name -eq 'CNCB92') { $true } else { "explicit: $($b.explicit -join ',')" } }
Step 'rm 92' $setupcn @('-s', '-w', '30', 'rm', '92') -rc $ok -check {
    $busy = Busy 'COM24?'; if ($busy -match 'COM24[23]') { "names still busy: $busy" } else { $true } }
Step 'add 93 with Ports class (COM#)' $setupcn @('-s', '-w', '60', 'add', '93', '-a', 'COM#', '-b', 'COM#') -rc 0, 3010 -check {
    $a = Show 'CNCA93'
    if ($a.parameters.PortName -ne 'COM#') { return "PortName=$($a.parameters.PortName)" }
    if ($a.name -notmatch '^COM\d+$') { return "no real name assigned: $($a.name)" }
    $true }
Step 'set CNCA93 RealPortName=COM244' $setupcn @('-s', '-w', '30', 'set', 'CNCA93', 'RealPortName=COM244') -rc 0, 3010 -check {
    $a = Show 'CNCA93'; if ($a.name -eq 'COM244') { $true } else { "name is $($a.name)" } }
Step 'rm 93' $setupcn @('-s', '-w', '30', 'rm', '93') -rc 0, 3010 -check { HasNot (ListText) 'CNC.93 ' }
Step 'rm 93 again' $setupcn @('-s', 'rm', '93') -rc 1
Step 'unknown command' $setupcn @('frobnicate') -rc 2

# ------------------------------------------------------------------ final state
Out-Log "`n================ final ================"
$final = ListText
if ($final -eq $baseline) { $script:pass++; Out-Log '[PASS] existing pairs unchanged' }
else { $script:fail++; Out-Log "[FAIL] list differs from baseline:`n$($final.TrimEnd())" }
$leftover = Busy 'COM24?'
if ($leftover.Trim().Length -eq 0) { $script:pass++; Out-Log '[PASS] COM240-COM249 all free again' }
else { $script:fail++; Out-Log "[FAIL] names left busy: $leftover" }

Out-Log "`nRESULT: $script:pass passed, $script:fail failed"
