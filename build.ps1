$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "bin"
$srcDir = Join-Path $root "src"
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$manifest = Join-Path $root "app.manifest"
$outFile = Join-Path $outDir "BoardBeam.exe"

if (!(Test-Path $compiler)) {
    throw "Cannot find the .NET Framework C# compiler: $compiler"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = Get-ChildItem -Path $srcDir -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /win32manifest:$manifest `
    /out:$outFile `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Built $outFile"

