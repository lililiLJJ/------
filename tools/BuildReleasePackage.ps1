param(
    [string]$Version = "v1.1.0-license-tool-release"
)

$ErrorActionPreference = "Stop"

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
$licenseProject = Join-Path $root "src/LicenseTool/LicenseTool.csproj"

$generatorPublish = Join-Path $root "publish/GeneratorService"
$licensePublish = Join-Path $root "publish/LicenseTool"
$releaseRoot = Join-Path $root "publish/ReleasePackage"
$packageRoot = Join-Path $releaseRoot ("EngineeringDocs-WPS-" + $Version)
$clientRoot = Join-Path $packageRoot "Client"
$issuerRoot = Join-Path $packageRoot "Issuer"

Write-Host "== Publish GeneratorService ==" -ForegroundColor Cyan
dotnet publish $generatorProject -c Release -r win-x64 --self-contained true -o $generatorPublish

Write-Host "== Publish LicenseTool ==" -ForegroundColor Cyan
dotnet publish $licenseProject -c Release -o $licensePublish

if (Test-Path $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

Write-Host "== Assemble Client package ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $clientRoot | Out-Null
Copy-DirectoryContent -Source $generatorPublish -Destination (Join-Path $clientRoot "GeneratorService")
Copy-DirectoryContent -Source (Join-Path $root "src/WpsAddin") -Destination (Join-Path $clientRoot "WpsAddin")
Copy-DirectoryContent -Source (Join-Path $root "Templates") -Destination (Join-Path $clientRoot "Templates")
Copy-DirectoryContent -Source (Join-Path $root "KnowledgeBase") -Destination (Join-Path $clientRoot "KnowledgeBase")
Copy-DirectoryContent -Source (Join-Path $root "docs") -Destination (Join-Path $clientRoot "Docs")
New-Item -ItemType Directory -Force -Path (Join-Path $clientRoot "Keys"), (Join-Path $clientRoot "Export"), (Join-Path $clientRoot "Logs") | Out-Null
Copy-Item (Join-Path $root "Keys/license-public.pem") (Join-Path $clientRoot "Keys/license-public.pem") -Force
Copy-Item (Join-Path $root "config.example.json") (Join-Path $clientRoot "config.example.json") -Force

Remove-Item -LiteralPath (Join-Path $clientRoot "GeneratorService/appsettings.Development.json") -ErrorAction SilentlyContinue
Get-ChildItem (Join-Path $clientRoot "GeneratorService") -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

$clientStartScript = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================
echo   Start GeneratorService
echo ========================================
echo.
echo Package root:
echo %CD%
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
New-Utf8File -Path (Join-Path $clientRoot "1-Start-GeneratorService.cmd") -Content $clientStartScript

$clientReadme = @'
# Client Package

This folder is for the customer.

Main content:

- GeneratorService/
- WpsAddin/
- Templates/
- KnowledgeBase/
- Docs/
- Keys/license-public.pem
- 1-Start-GeneratorService.cmd

Suggested order:

1. Start GeneratorService
2. Load WpsAddin/manifest.xml in WPS
3. Open the license panel and copy machine code
4. Paste the activation code from the issuer
5. Start generating documents
'@
New-Utf8File -Path (Join-Path $clientRoot "README.md") -Content $clientReadme

Write-Host "== Assemble Issuer package ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $issuerRoot, (Join-Path $issuerRoot "LicenseTool"), (Join-Path $issuerRoot "Keys") | Out-Null
Copy-Item (Join-Path $licensePublish "LicenseTool.exe") (Join-Path $issuerRoot "LicenseTool/LicenseTool.exe") -Force
Copy-DirectoryContent -Source (Join-Path $root "docs") -Destination (Join-Path $issuerRoot "Docs")

$issuerKeyScript = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================
echo   Generate RSA Key Pair
echo ========================================
echo.
".\LicenseTool\LicenseTool.exe" --generate-keypair
echo.
pause
'@
New-Utf8File -Path (Join-Path $issuerRoot "1-Generate-RSA-Keys.cmd") -Content $issuerKeyScript

$issuerCodeScript = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================
echo   Create Activation Code
echo ========================================
echo.
".\LicenseTool\LicenseTool.exe"
echo.
pause
'@
New-Utf8File -Path (Join-Path $issuerRoot "2-Create-Activation-Code.cmd") -Content $issuerCodeScript

$issuerReadme = @'
# Issuer Package

This folder is for the issuer only.

Main content:

- LicenseTool/LicenseTool.exe
- 1-Generate-RSA-Keys.cmd
- 2-Create-Activation-Code.cmd
- Docs/

Notes:

- The private key will be created at Keys/license-private.pem
- Never send the private key to the customer
- Send the final Base64 activation code text to the customer
'@
New-Utf8File -Path (Join-Path $issuerRoot "README.md") -Content $issuerReadme

$packageReadme = @"
# Release Package

Version: $Version

This package contains:

- Client/
- Issuer/

Client is for the customer.
Issuer is for the authorization side only.
"@
New-Utf8File -Path (Join-Path $packageRoot "README.md") -Content $packageReadme

Write-Host "== Done ==" -ForegroundColor Green
Write-Host "Release package:" $packageRoot
