param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('EastAsiaRuntimeHarness-' + [Guid]::NewGuid().ToString('N'))
}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
Add-Type -Path @((Join-Path $root 'runtime/EastAsiaMap.Runtime.cs'), (Join-Path $root 'tests/runtime_cache_harness.cs'))
$passed = [RuntimeCacheHarness]::Run($OutputDirectory)
foreach ($test in $passed) { Write-Output ('PASS ' + $test) }
Write-Output ('Passed ' + $passed.Length + ' managed behavioral cases. Bannerlord native engine was not tested.')
