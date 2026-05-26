$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $root "src/WpsAddin"
$targetDir = Join-Path $env:APPDATA "kingsoft/wps/jsaddons/engineering-docs-wps-addin_0.1.0"

if (-not (Test-Path -LiteralPath $sourceDir)) {
    throw "WpsAddin source directory not found: $sourceDir"
}

$packagePath = Join-Path $sourceDir "package.json"
$package = Get-Content -Raw -LiteralPath $packagePath | ConvertFrom-Json

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$files = @(
    "app.js",
    "index.html",
    "styles.css",
    "ribbon.xml",
    "manifest.xml",
    "main.js",
    "material-ledger.html",
    "package.json"
)

foreach ($file in $files) {
    $source = Join-Path $sourceDir $file
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $targetDir $file) -Force
    }
}

$sourceJsDir = Join-Path $sourceDir "js"
$targetJsDir = Join-Path $targetDir "js"
if (Test-Path -LiteralPath $sourceJsDir) {
    if (Test-Path -LiteralPath $targetJsDir) {
        Remove-Item -LiteralPath $targetJsDir -Recurse -Force
    }

    Copy-Item -LiteralPath $sourceJsDir -Destination $targetDir -Recurse -Force
}

Write-Host "WPS addin synced."
Write-Host "Source: $sourceDir"
Write-Host "Target: $targetDir"
Write-Host "Addin version: $($package.version)"
Write-Host ""
Write-Host "Key file hashes:"

$hashFiles = @(
    "app.js",
    "index.html",
    "styles.css",
    "ribbon.xml",
    "manifest.xml",
    "package.json"
)

$hashFiles |
    ForEach-Object {
        $file = $_
        $path = Join-Path $targetDir $_
        if (Test-Path -LiteralPath $path) {
            $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $path
            [PSCustomObject]@{
                File = $file
                Hash = $hash.Hash
            }
        }
    } |
    Format-Table -AutoSize

Write-Host ""
Write-Host "If WPS is already open, refresh the task pane or restart WPS."
