param(
    [string]$Version = "v1.0.0-desktop"
)

$ErrorActionPreference = "Stop"
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function New-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Copy-DirectoryContent {
    param(
        [string]$Source,
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item (Join-Path $Source "*") $Destination -Recurse -Force
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir

$generatorProject = Join-Path $root "src/GeneratorService/GeneratorService.csproj"
$desktopProject = Join-Path $root "src/DesktopLauncher/DesktopLauncher.csproj"
$licenseProject = Join-Path $root "src/LicenseTool/LicenseTool.csproj"
$wpsAddinRoot = Join-Path $root "src/WpsAddin"
$frontendDist = Join-Path $wpsAddinRoot "dist"

$generatorPublish = Join-Path $root "publish/GeneratorService"
$desktopPublish = Join-Path $root "publish/DesktopLauncher"
$licensePublish = Join-Path $root "publish/LicenseTool"
$releaseRoot = Join-Path $root "publish/ReleasePackage"
$packageRoot = Join-Path $releaseRoot ("EngineeringDocs-Desktop-" + $Version)
$clientRoot = Join-Path $packageRoot "Client"
$issuerRoot = Join-Path $packageRoot "Issuer"

Write-Host "== Build frontend ==" -ForegroundColor Cyan
Push-Location $wpsAddinRoot
try {
    npm run build
}
finally {
    Pop-Location
}

Write-Host "== Publish GeneratorService ==" -ForegroundColor Cyan
dotnet publish $generatorProject -c Release -r win-x64 --self-contained true -o $generatorPublish

Write-Host "== Publish DesktopLauncher ==" -ForegroundColor Cyan
dotnet publish $desktopProject -c Release -r win-x64 --self-contained true -o $desktopPublish

Write-Host "== Publish LicenseTool ==" -ForegroundColor Cyan
dotnet publish $licenseProject -c Release -o $licensePublish

if (Test-Path $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

Write-Host "== Assemble Desktop Client package ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $clientRoot | Out-Null
Copy-DirectoryContent -Source $desktopPublish -Destination $clientRoot
Copy-DirectoryContent -Source $generatorPublish -Destination (Join-Path $clientRoot "GeneratorService")
Copy-DirectoryContent -Source $frontendDist -Destination (Join-Path $clientRoot "WebClient/dist")
Copy-DirectoryContent -Source (Join-Path $root "Templates") -Destination (Join-Path $clientRoot "Templates")
Copy-DirectoryContent -Source (Join-Path $root "KnowledgeBase") -Destination (Join-Path $clientRoot "KnowledgeBase")
Copy-DirectoryContent -Source (Join-Path $root "docs") -Destination (Join-Path $clientRoot "Docs")
New-Item -ItemType Directory -Force -Path (Join-Path $clientRoot "Keys"), (Join-Path $clientRoot "Export"), (Join-Path $clientRoot "Logs"), (Join-Path $clientRoot "Projects") | Out-Null
Copy-Item (Join-Path $root "Keys/license-public.pem") (Join-Path $clientRoot "Keys/license-public.pem") -Force
Copy-Item (Join-Path $root "config.example.json") (Join-Path $clientRoot "config.example.json") -Force

Remove-Item -LiteralPath (Join-Path $clientRoot "GeneratorService/appsettings.Development.json") -ErrorAction SilentlyContinue
Get-ChildItem (Join-Path $clientRoot "GeneratorService") -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

$clientStartScript = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
".\EngineeringDocsDesktop.exe"
'@
New-Utf8File -Path (Join-Path $clientRoot "1-Start-Desktop.cmd") -Content $clientStartScript

$clientServiceScript = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================
echo   Start GeneratorService
echo ========================================
echo.
echo Service URL:
echo http://127.0.0.1:5188
echo.
echo Press Ctrl+C to stop the service.
echo.
".\GeneratorService\GeneratorService.exe"
echo.
pause
'@
New-Utf8File -Path (Join-Path $clientRoot "2-Start-Service-Only.cmd") -Content $clientServiceScript

$clientReadme = @'
# Engineering Docs Desktop Client

This folder is for the customer.

Start the software by double-clicking:

```text
EngineeringDocsDesktop.exe
```

or:

```text
1-Start-Desktop.cmd
```

The desktop launcher starts the local `GeneratorService` automatically and then opens `WebClient/dist/index.html`. WPS is not required as the host.

Generated `.xlsx` files are still opened by the user's system default spreadsheet application.
'@
New-Utf8File -Path (Join-Path $clientRoot "README.md") -Content $clientReadme

Write-Host "== Assemble Issuer package ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $issuerRoot, (Join-Path $issuerRoot "LicenseTool"), (Join-Path $issuerRoot "Keys") | Out-Null
Copy-Item (Join-Path $licensePublish "LicenseTool.exe") (Join-Path $issuerRoot "LicenseTool/LicenseTool.exe") -Force
Copy-DirectoryContent -Source (Join-Path $root "docs") -Destination (Join-Path $issuerRoot "Docs")

$issuerReadme = @'
# Issuer Package

This folder is for the issuer only.

- Keep private keys here.
- Never send the private key to the customer.
- Send only the final Base64 activation code text to the customer.
'@
New-Utf8File -Path (Join-Path $issuerRoot "README.md") -Content $issuerReadme

$packageReadme = @"
# Desktop Release Package

Version: $Version

This package contains:

- Client/
- Issuer/

Client is for the customer.
Issuer is for the authorization side only.
"@
New-Utf8File -Path (Join-Path $packageRoot "README.md") -Content $packageReadme

Write-Host "== Done ==" -ForegroundColor Green
Write-Host "Desktop release package:" $packageRoot
