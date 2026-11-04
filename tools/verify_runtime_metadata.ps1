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
        if ($name -in @('System.Private.CoreLib', 'netstandard')) {
            throw ('Unexpected build-host runtime reference: ' + $name)
        }
        $references += ($name + ' ' + $reference.Version.ToString())
    }
    if (-not ($references | Where-Object { $_ -eq 'mscorlib 4.0.0.0' })) { throw 'Expected .NET Framework mscorlib 4.0 reference.' }
    if ($pe.PEHeaders.CoffHeader.Machine.ToString() -ne 'Amd64') { throw 'Expected x64 PE architecture.' }
    if ($pe.PEHeaders.PEHeader.Magic.ToString() -ne 'PE32Plus') { throw 'Expected PE32Plus.' }
    if ($metadata.GetAssemblyDefinition().Version.ToString() -ne '0.10.1.0') { throw 'Expected v10.1 assembly version.' }
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
    Write-Output 'Verified module/model metadata, explicit AMD64/PE32Plus, v10.1, .NET Framework references; no netstandard or host .NET9 dependency.'
    $references
} finally { $pe.Dispose(); $stream.Dispose() }
