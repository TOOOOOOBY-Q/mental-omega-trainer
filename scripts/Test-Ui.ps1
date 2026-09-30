param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\release\ui-preview'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $repo 'release\tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $testDirectory 'TrainerUiHarness.exe'
$sources = @('src\MOTrainer336.cs', 'src\TrainerUi.cs', 'src\PowerOverride.cs', 'src\BuildOverride.cs', 'src\RemoteCallHook.cs', 'tests\TrainerUiHarness.cs') | ForEach-Object { Join-Path $repo $_ }
& $csc /nologo /target:exe /platform:x86 /utf8output /main:MOTrainer336.TrainerUiHarness "/out:$harness" @sources
if ($LASTEXITCODE -ne 0) { throw 'UI harness compilation failed' }
& $harness $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'UI verification failed' }
