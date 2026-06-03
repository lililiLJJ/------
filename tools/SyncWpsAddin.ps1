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

Write-Host "Building WPS addin frontend..."
Push-Location $sourceDir
try {
    npm run build
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceDir "dist/index.html"))) {
    throw "WpsAddin dist/index.html not found. Please run npm run build in src/WpsAddin first."
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceDir "dist/app.bundle.js"))) {
    throw "WpsAddin dist/app.bundle.js not found. Please run npm run build in src/WpsAddin first."
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Get-ChildItem -LiteralPath $targetDir -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction Stop

$excludedNames = @(
    "node_modules"
)

Get-ChildItem -LiteralPath $sourceDir -Force |
    Where-Object { $excludedNames -notcontains $_.Name } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $targetDir -Recurse -Force
    }

$hashFiles = @(
    "index.html",
    "inspection-batch-plan-center.html",
    "styles.css",
    "boot-loader.js",
    "dist/index.html",
    "dist/app.bundle.js",
    "js/ribbon.js",
    "manifest.xml",
    "package.json"
)

Write-Host "WPS addin synced."
Write-Host "Source: $sourceDir"
Write-Host "Target: $targetDir"
Write-Host "Addin version: $($package.version)"
Write-Host ""
Write-Host "Key file hashes:"

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
Write-Host "Production entry:"
Write-Host (Join-Path $targetDir "dist/index.html")
Write-Host ""
Write-Host "If WPS is already open, refresh the task pane or restart WPS."
