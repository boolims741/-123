param(
    [string]$GameReferences = '/workspace/eastasia-map/analysis/reference156-net472',
    [string]$FrameworkReferences = '/workspace/eastasia-map/toolchain/net472',
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'build/runtime/EastAsiaMap.Runtime.dll' }
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$sourcePath = Join-Path $root 'runtime/EastAsiaMap.Runtime.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source, $parseOptions, 'runtime/EastAsiaMap.Runtime.cs')
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($directory in @($FrameworkReferences, $GameReferences)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing reference directory: $directory" }
    foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter '*.dll' -File -Recurse | Sort-Object FullName)) {
        $referenceStream = [IO.File]::OpenRead($file.FullName)
        $referencePe = [System.Reflection.PortableExecutable.PEReader]::new($referenceStream)
        try {
            if ($referencePe.HasMetadata -and [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($referencePe).IsAssembly) {
                $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName))
            }
        } finally { $referencePe.Dispose(); $referenceStream.Dispose() }
    }
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$options = $options.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true).WithPlatform([Microsoft.CodeAnalysis.Platform]::X64)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('EastAsiaMap.Runtime', [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $references, $options)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath))
$stream = [IO.File]::Create($OutputPath)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
foreach ($diagnostic in $result.Diagnostics) { Write-Output $diagnostic.ToString() }
if (-not $result.Success) {
    Remove-Item -LiteralPath $OutputPath
    throw 'Runtime compilation failed. No DLL produced.'
}
Write-Output ('Compiled x64 against explicit .NET Framework 4.7.2 and Bannerlord 1.5.6.125053 net472 reference assemblies: ' + $OutputPath)
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
