param(
    [string]$GameReferences = '/workspace/eastasia-map/analysis/reference156',
    [string]$FrameworkReferences = '/workspace/eastasia-map/toolchain/netstandard20',
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
    foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter '*.dll' -File | Sort-Object Name)) {
        $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName))
    }
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$options = $options.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('EastAsiaMap.Runtime', [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $references, $options)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath))
$stream = [IO.File]::Create($OutputPath)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
foreach ($diagnostic in $result.Diagnostics) { Write-Output $diagnostic.ToString() }
if (-not $result.Success) {
    Remove-Item -LiteralPath $OutputPath
    throw 'Runtime compilation failed. No DLL produced.'
}
Write-Output ('Compiled against explicit .NET Standard 2.0 and Bannerlord 1.5.6.125053 reference assemblies: ' + $OutputPath)
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
