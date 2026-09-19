# 构建心灵终结单机修改器。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts\Build.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -NoAdmin   # 不嵌入管理员清单（仅用于本地界面测试）
#
# 可选签名（需要 signtool 和签名证书）：
#   $env:MO_TRAINER_SIGN_PFX = 'C:\path\to\cert.pfx'
#   $env:MO_TRAINER_SIGN_PASSWORD = '...'
#   powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -Sign
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\release\心灵终结修改器_MO336.exe'),
    [switch]$NoAdmin,
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot '..\src\MOTrainer336.cs'
$manifest = Join-Path $PSScriptRoot '..\src\app.manifest'

# 定位 .NET Framework 自带的 C# 编译器（目标框架 4.x，Windows 10/11 均内置运行时）。
$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '未找到 csc.exe；请确认已安装 .NET Framework 4.x' }

$outDir = Split-Path -Parent $OutputPath
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$args = @('/nologo', '/target:winexe', '/platform:x86', '/utf8output', '/optimize+', "/out:$OutputPath")
if (-not $NoAdmin) { $args += "/win32manifest:$manifest" }
$args += $source

Write-Host "编译器: $csc"
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }

$info = (Get-Item $OutputPath).VersionInfo
Write-Host "产物: $OutputPath"
Write-Host "版本: $($info.FileVersion)  产品: $($info.ProductName)  公司: $($info.CompanyName)"
if ($info.FileVersion -ne '3.3.6.4') { throw "版本号不符合预期（期望 3.3.6.4，实际 $($info.FileVersion)）" }

if ($Sign) {
    $pfx = $env:MO_TRAINER_SIGN_PFX
    $password = $env:MO_TRAINER_SIGN_PASSWORD
    if (-not $pfx -or -not (Test-Path $pfx)) { throw '签名需要环境变量 MO_TRAINER_SIGN_PFX 指向证书文件' }
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) { throw '签名需要 Windows SDK 中的 signtool.exe 加入 PATH' }
    & signtool.exe sign /fd SHA256 /f $pfx /p $password $OutputPath
    if ($LASTEXITCODE -ne 0) { throw "签名失败（退出码 $LASTEXITCODE）" }
    Write-Host '签名完成'
} else {
    Write-Host '未签名：发布版本请使用 -Sign 并配置签名证书环境变量'
}

$hash = (Get-FileHash $OutputPath -Algorithm SHA256).Hash
Write-Host "SHA-256: $hash"
