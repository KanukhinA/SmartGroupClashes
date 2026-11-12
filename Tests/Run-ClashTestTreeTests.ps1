$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild / Roslyn not found. Install Visual Studio Build Tools.' }
$compiler = Join-Path (Split-Path $msbuild -Parent) 'Roslyn\csc.exe'
$outputDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$testExe = Join-Path $outputDir 'ClashTestTreeTests.exe'
& $compiler /nologo /langversion:7.3 /target:exe "/out:$testExe" (Join-Path $repoRoot 'GroupClashes\ViewModels\ViewModelBase.cs') (Join-Path $repoRoot 'GroupClashes\ViewModels\ClashTestTreeNode.cs') (Join-Path $repoRoot 'GroupClashes\ViewModels\ClashTestItemViewModel.cs') (Join-Path $repoRoot 'GroupClashes\ViewModels\ClashTestCatalog.cs') (Join-Path $PSScriptRoot 'NativeClashApiStubs.cs') (Join-Path $PSScriptRoot 'ClashTestTreeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tree test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Clash test tree tests failed.' }
