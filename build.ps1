$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "bin"
$srcDir = Join-Path $root "src"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$manifest = Join-Path $root "app.manifest"
$appConfig = Join-Path $root "app.config"
$outFile = Join-Path $outDir "BoardBeam.exe"

if (!(Test-Path $compiler)) {
    throw "Cannot find the .NET Framework C# compiler: $compiler"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = Get-ChildItem -Path $srcDir -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }

$winrtRef = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll"
$mdFoundation = Join-Path $env:WINDIR "System32\WinMetadata\Windows.Foundation.winmd"
$mdStorage = Join-Path $env:WINDIR "System32\WinMetadata\Windows.Storage.winmd"
$mdGraphics = Join-Path $env:WINDIR "System32\WinMetadata\Windows.Graphics.winmd"
$mdMedia = Join-Path $env:WINDIR "System32\WinMetadata\Windows.Media.winmd"
$mdGlobalization = Join-Path $env:WINDIR "System32\WinMetadata\Windows.Globalization.winmd"

$refs = @(
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll"
)
if (Test-Path $mdFoundation) { $refs += "/reference:$mdFoundation" }
if (Test-Path $mdStorage) { $refs += "/reference:$mdStorage" }
if (Test-Path $mdGraphics) { $refs += "/reference:$mdGraphics" }
if (Test-Path $mdMedia) { $refs += "/reference:$mdMedia" }
if (Test-Path $mdGlobalization) { $refs += "/reference:$mdGlobalization" }
if (Test-Path $winrtRef) { $refs += "/reference:$winrtRef" }
# WinRT winmd 引用 System.Runtime facade（解析 System.Attribute 等），需显式引用
$sysRuntime = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll"
if (Test-Path $mdMedia) {
    if (Test-Path $sysRuntime) { $refs += "/reference:$sysRuntime" }
}

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /win32manifest:$manifest `
    /out:$outFile `
    @refs `
    $sources

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# 复制 app.config 到输出目录（.NET 运行时按 <exename>.config 读取，
# 用于启用 PerMonitorV2 自动缩放的 ApplicationConfigurationSection）
$appConfig = Join-Path $root "app.config"
if (Test-Path $appConfig) {
    Copy-Item -Path $appConfig -Destination (Join-Path $outDir "BoardBeam.exe.config") -Force
}

Write-Host "Built $outFile"

