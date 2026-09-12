param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,
    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = "Stop"
$releaseRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = (Resolve-Path $ProjectRoot).Path
$outputPath = [IO.Path]::GetFullPath($Output)

$env:LUZR_PROJECT_ROOT = $project
$env:LUZR_RELEASE_ROOT = $releaseRoot
$env:RUSTUP_HOME = Join-Path $releaseRoot "_packaging\toolchain\rustup"
$env:CARGO_HOME = Join-Path $releaseRoot "_packaging\toolchain\cargo"
$env:LUZR_CARGO_TARGET_DIR = Join-Path $releaseRoot "_packaging\cargo-target"
$env:RUSTUP_TOOLCHAIN = "stable-x86_64-pc-windows-gnu"
$mingwBin = Join-Path $releaseRoot "_packaging\toolchain\mingw-binutils\ucrt64\bin"
$env:PATH = $mingwBin + [IO.Path]::PathSeparator + ($env:PATH ?? "")

Push-Location $project
try {
    & py -3.14 -B -m scripts.build_portable_release --output $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Portable release build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
