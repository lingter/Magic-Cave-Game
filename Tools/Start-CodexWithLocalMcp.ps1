# Run from a separate PowerShell window after fully exiting Codex.
# Only the launched Codex process receives these loopback proxy exclusions.
$ErrorActionPreference = 'Stop'

$codexPackage = Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1
if (-not $codexPackage) { throw 'Codex installation was not found.' }
$codexExecutable = Join-Path $codexPackage.InstallLocation 'app\ChatGPT.exe'
if (-not (Test-Path -LiteralPath $codexExecutable)) { throw "Codex executable was not found: $codexExecutable" }

$runningCodex = Get-Process -Name ChatGPT -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($codexPackage.InstallLocation, [StringComparison]::OrdinalIgnoreCase)
}
if ($runningCodex) { throw 'Fully exit Codex before running this script. An existing process will not inherit NO_PROXY.' }

$previousNoProxy = [Environment]::GetEnvironmentVariable('NO_PROXY', 'Process')
$localBypass = @($previousNoProxy -split ',' | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })
$localBypass += @('localhost', '127.0.0.1', '::1')
try {
    $env:NO_PROXY = ($localBypass | Select-Object -Unique) -join ','
    Start-Process -FilePath $codexExecutable -WorkingDirectory (Split-Path -Parent $PSScriptRoot) -WindowStyle Hidden
}
finally {
    if ($null -eq $previousNoProxy) { Remove-Item Env:NO_PROXY -ErrorAction SilentlyContinue }
    else { $env:NO_PROXY = $previousNoProxy }
}
