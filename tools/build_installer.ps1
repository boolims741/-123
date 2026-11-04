param(
    [string]$FrameworkReferences = '/workspace/eastasia-map/toolchain/net472',
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'build/installer/EastAsiaMap_Install.exe' }
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$sourcePath = Join-Path $root 'runtime/InstallAndCheck.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source, $parseOptions, 'runtime/InstallAndCheck.cs')
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $FrameworkReferences -Filter '*.dll' -File -Recurse | Sort-Object FullName)) {
    $stream = [IO.File]::OpenRead($file.FullName)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        if ($pe.HasMetadata -and [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe).IsAssembly) {
            $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName))
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication)
$options = $options.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true).WithPlatform([Microsoft.CodeAnalysis.Platform]::X64)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('EastAsiaMap_Install', [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $references, $options)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath))
$stream = [IO.File]::Create($OutputPath)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
foreach ($diagnostic in $result.Diagnostics) { Write-Output $diagnostic.ToString() }
if (-not $result.Success) { Remove-Item -LiteralPath $OutputPath; throw 'Installer compilation failed.' }
Write-Output ('Compiled .NET Framework 4.7.2 x64 Windows installer/checker: ' + $OutputPath)
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
