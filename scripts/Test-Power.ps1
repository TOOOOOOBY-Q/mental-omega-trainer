param([string]$Python, [string]$EmulationDependencies, [string]$GameExecutable)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$build = Join-Path $repo 'release\tests'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $build 'PowerOverrideHarness.exe'
$source = Join-Path $repo 'src\PowerOverride.cs'
$hookSource = Join-Path $repo 'src\RemoteCallHook.cs'
$buildSource = Join-Path $repo 'src\BuildOverride.cs'
$tests = Join-Path $repo 'tests\PowerOverrideHarness.cs'
# Only fixed virtual addresses are relocated for the native lifecycle fixture:
# a CLR child may already use the game's low addresses. No production logic is
# replaced. The unmodified production stub is separately exported/emulated below.
$relocatedSource = [IO.File]::ReadAllText($source)
$relocatedBuild = [IO.File]::ReadAllText($buildSource)
$relocatedTests = [IO.File]::ReadAllText($tests)
foreach ($address in @('0x508D81', '0x508D86', '0x454CE0', '0xA83D4C', '0x500000', '0x450000', '0xA80000', '0x4C9B68', '0x4C9B6D', '0x426630', '0x4C0000', '0x420000')) {
    $replacement = '0x{0:X8}' -f ([Convert]::ToInt32($address.Substring(2), 16) + 0x30000000)
    $relocatedSource = $relocatedSource.Replace($address, $replacement)
    $relocatedBuild = $relocatedBuild.Replace($address, $replacement)
    $relocatedTests = $relocatedTests.Replace($address, $replacement)
}
$fixtureSource = Join-Path $build 'PowerOverride.Relocated.cs'
$fixtureTests = Join-Path $build 'PowerOverrideHarness.Relocated.cs'
$fixtureBuild = Join-Path $build 'BuildOverride.Relocated.cs'
[IO.File]::WriteAllText($fixtureSource, $relocatedSource)
[IO.File]::WriteAllText($fixtureBuild, $relocatedBuild)
[IO.File]::WriteAllText($fixtureTests, $relocatedTests)
& $csc /nologo /target:exe /platform:x86 "/out:$harness" $fixtureSource $fixtureBuild $hookSource $fixtureTests
if ($LASTEXITCODE -ne 0) { throw 'Power test harness compilation failed' }
& $harness
if ($LASTEXITCODE -ne 0) { throw 'Native power lifecycle tests failed' }
$exporter = Join-Path $build 'PowerStubExporter.exe'
& $csc /nologo /target:exe /platform:x86 "/out:$exporter" $source $buildSource $hookSource $tests
if ($LASTEXITCODE -ne 0) { throw 'Production stub exporter compilation failed' }
$stub = Join-Path $build 'power-stub.json'
& $exporter emit $stub
if ($LASTEXITCODE -ne 0) { throw 'Stub export failed' }
$buildStub = Join-Path $build 'build-stub.json'
& $exporter emit-build $buildStub
if ($LASTEXITCODE -ne 0) { throw 'Build stub export failed' }
if ($Python) {
    $emulationArgs = @((Join-Path $repo 'tests\power_override_emulation.py'), '--stub', $stub)
    if ($EmulationDependencies) { $emulationArgs += @('--deps', $EmulationDependencies) }
    if ($GameExecutable) { $emulationArgs += @('--game', $GameExecutable) }
    & $Python @emulationArgs
    if ($LASTEXITCODE -ne 0) { throw 'Power instruction emulation failed' }
    $emulationArgs[0] = Join-Path $repo 'tests\build_override_emulation.py'
    $emulationArgs[2] = $buildStub
    & $Python @emulationArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build instruction emulation failed' }
}
