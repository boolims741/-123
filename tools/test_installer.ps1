param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('EastAsiaInstallerTests-' + [Guid]::NewGuid().ToString('N')) }
Add-Type -Path @((Join-Path $root 'runtime/InstallAndCheck.cs'), (Join-Path $root 'tests/installer_harness.cs'))
$passed = [InstallerHarness]::Run($OutputDirectory)
foreach ($test in $passed) { Write-Output ('PASS ' + $test) }
Write-Output ('Passed ' + $passed.Length + ' installer filesystem cases. Windows UI, NTFS and game CLR loading were not tested.')
