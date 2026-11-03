param([string]$DllPath)
$ErrorActionPreference = 'Stop'
if (-not $DllPath) { $DllPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'build/runtime/EastAsiaMap.Runtime.dll' }
$stream = [IO.File]::OpenRead($DllPath)
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $references = @()
    foreach ($handle in $metadata.AssemblyReferences) {
        $reference = $metadata.GetAssemblyReference($handle)
        $name = $metadata.GetString($reference.Name)
        if ($name -in @('System.Private.CoreLib', 'System.Runtime')) {
            throw ('Unexpected build-host runtime reference: ' + $name)
        }
        $references += ($name + ' ' + $reference.Version.ToString())
    }
    if (-not ($references | Where-Object { $_ -eq 'netstandard 2.0.0.0' })) { throw 'Expected .NET Standard 2.0 reference.' }
    foreach ($needed in @('TaleWorlds.MountAndBlade', 'TaleWorlds.CampaignSystem', 'NavalDLC')) {
        if (-not ($references | Where-Object { $_.StartsWith($needed + ' ') })) { throw ('Missing game reference: ' + $needed) }
    }
    $types = @()
    foreach ($handle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($handle)
        $types += ($metadata.GetString($type.Namespace) + '.' + $metadata.GetString($type.Name))
    }
    foreach ($needed in @('EastAsiaMap.Runtime.EastAsiaMapSubModule', 'EastAsiaMap.Runtime.EastAsiaNavalMapDistanceModel')) {
        if ($needed -notin $types) { throw ('Missing loader/model type: ' + $needed) }
    }
    Write-Output 'Verified module/model metadata and .NET Standard 2.0 compatibility references (no host .NET9 reference).'
    $references
} finally { $pe.Dispose(); $stream.Dispose() }
