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

$clientSilentStartScript = @'
$ErrorActionPreference = "Stop"
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$clientRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$serviceExe = Join-Path $clientRoot "GeneratorService/GeneratorService.exe"

try {
    Invoke-RestMethod -Uri "http://127.0.0.1:5188/api/health" -TimeoutSec 2 | Out-Null
    exit 0
} catch {
}

Start-Process -FilePath $serviceExe -WorkingDirectory $clientRoot -WindowStyle Hidden
'@
New-Utf8File -Path (Join-Path $clientRoot "Start-GeneratorService-Silent.ps1") -Content $clientSilentStartScript

$clientInstallScript = @'
$ErrorActionPreference = "Stop"
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$clientRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$addinName = "engineering-docs-wps-addin"
$addinType = "et"
$addinVersion = "0.1.0"
$taskName = "EngineeringDocsGeneratorService"

$wpsAddonsRoot = Join-Path $env:APPDATA "kingsoft/wps/jsaddons"
$targetAddinDir = Join-Path $wpsAddonsRoot "$addinName`_$addinVersion"
$publishXmlPath = Join-Path $wpsAddonsRoot "publish.xml"
$sourceAddinDir = Join-Path $clientRoot "WpsAddin"
$runnerPath = Join-Path $clientRoot "Start-GeneratorService-Silent.ps1"
$serviceExe = Join-Path $clientRoot "GeneratorService/GeneratorService.exe"
$configPath = Join-Path $clientRoot "config.json"
$configExamplePath = Join-Path $clientRoot "config.example.json"
$startupDir = [Environment]::GetFolderPath("Startup")
$startupScript = Join-Path $startupDir "$taskName.vbs"

if (-not (Test-Path $sourceAddinDir)) {
    throw "WpsAddin directory not found: $sourceAddinDir"
}

if (-not (Test-Path $serviceExe)) {
    throw "GeneratorService.exe not found: $serviceExe"
}

if (-not (Test-Path $configPath) -and (Test-Path $configExamplePath)) {
    Copy-Item -LiteralPath $configExamplePath -Destination $configPath -Force
}

New-Item -ItemType Directory -Force -Path $wpsAddonsRoot | Out-Null
if (Test-Path $targetAddinDir) {
    Remove-Item -LiteralPath $targetAddinDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $targetAddinDir | Out-Null
Copy-Item -Path (Join-Path $sourceAddinDir "*") -Destination $targetAddinDir -Recurse -Force

if (Test-Path $publishXmlPath) {
    try {
        [xml]$publishXml = Get-Content -Raw -Encoding UTF8 $publishXmlPath
    } catch {
        $publishXml = New-Object System.Xml.XmlDocument
        $publishXml.AppendChild($publishXml.CreateElement("jsplugins")) | Out-Null
    }
} else {
    $publishXml = New-Object System.Xml.XmlDocument
    $publishXml.AppendChild($publishXml.CreateElement("jsplugins")) | Out-Null
}

if ($null -eq $publishXml.DocumentElement -or $publishXml.DocumentElement.Name -ne "jsplugins") {
    $publishXml = New-Object System.Xml.XmlDocument
    $publishXml.AppendChild($publishXml.CreateElement("jsplugins")) | Out-Null
}

$existingNodes = @($publishXml.DocumentElement.SelectNodes("*[@name='$addinName']"))
foreach ($node in $existingNodes) {
    $publishXml.DocumentElement.RemoveChild($node) | Out-Null
}

$addinNode = $publishXml.CreateElement("jsplugin")
$attributes = @{
    name = $addinName
    type = $addinType
    url = "$addinName`_$addinVersion"
    version = $addinVersion
    enable = "enable_dev"
    install = "null"
    customDomain = ""
}

foreach ($key in $attributes.Keys) {
    $attribute = $publishXml.CreateAttribute($key)
    $attribute.Value = $attributes[$key]
    $addinNode.Attributes.Append($attribute) | Out-Null
}
$publishXml.DocumentElement.AppendChild($addinNode) | Out-Null
$publishXml.Save($publishXmlPath)

try {
    $action = New-ScheduledTaskAction `
        -Execute "powershell.exe" `
        -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$runnerPath`"" `
        -WorkingDirectory $clientRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 12)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Description "Engineering Docs GeneratorService auto start" -Force | Out-Null
    if (Test-Path $startupScript) {
        Remove-Item -LiteralPath $startupScript -Force
    }
} catch {
    $escapedRunnerPath = $runnerPath.Replace("""", """""")
    $startupContent = @"
Set shell = CreateObject("WScript.Shell")
shell.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""$escapedRunnerPath""", 0, False
"@
    [System.IO.File]::WriteAllText($startupScript, $startupContent, [System.Text.UTF8Encoding]::new($false))
}

& $runnerPath

Write-Host ""
Write-Host "Installation completed." -ForegroundColor Green
Write-Host "Reopen WPS Spreadsheets and use the Engineering Docs add-in from the ribbon."
Write-Host "If WPS is already open, close it completely and open it again."
Write-Host ""
Read-Host "Press Enter to exit"
'@
New-Utf8File -Path (Join-Path $clientRoot "Install-Client.ps1") -Content $clientInstallScript

$clientInstallCmd = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Client.ps1"
'@
New-Utf8File -Path (Join-Path $clientRoot "1-Install-Client.cmd") -Content $clientInstallCmd

$clientUninstallScript = @'
$ErrorActionPreference = "Stop"
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$addinName = "engineering-docs-wps-addin"
$addinVersion = "0.1.0"
$taskName = "EngineeringDocsGeneratorService"
$wpsAddonsRoot = Join-Path $env:APPDATA "kingsoft/wps/jsaddons"
$targetAddinDir = Join-Path $wpsAddonsRoot "$addinName`_$addinVersion"
$publishXmlPath = Join-Path $wpsAddonsRoot "publish.xml"
$startupDir = [Environment]::GetFolderPath("Startup")
$startupScript = Join-Path $startupDir "$taskName.vbs"

Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

if (Test-Path $startupScript) {
    Remove-Item -LiteralPath $startupScript -Force
}

if (Test-Path $targetAddinDir) {
    Remove-Item -LiteralPath $targetAddinDir -Recurse -Force
}

if (Test-Path $publishXmlPath) {
    try {
        [xml]$publishXml = Get-Content -Raw -Encoding UTF8 $publishXmlPath
        $existingNodes = @($publishXml.DocumentElement.SelectNodes("*[@name='$addinName']"))
        foreach ($node in $existingNodes) {
            $publishXml.DocumentElement.RemoveChild($node) | Out-Null
        }
        $publishXml.Save($publishXmlPath)
    } catch {
        Write-Warning "Failed to update publish.xml. Please check manually: $publishXmlPath"
    }
}

Get-Process -Name "GeneratorService" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host ""
Write-Host "Uninstall completed." -ForegroundColor Green
Write-Host "If WPS is already open, close it completely and open it again."
Write-Host ""
Read-Host "Press Enter to exit"
'@
New-Utf8File -Path (Join-Path $clientRoot "Uninstall-Client.ps1") -Content $clientUninstallScript

$clientUninstallCmd = @'
@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Client.ps1"
'@
New-Utf8File -Path (Join-Path $clientRoot "2-Uninstall-Client.cmd") -Content $clientUninstallCmd

$clientReadme = @'
# Client Package

This folder is for the customer.

Main content:

- 1-Install-Client.cmd
- 2-Uninstall-Client.cmd
- GeneratorService/
- WpsAddin/
- Templates/
- KnowledgeBase/
- Docs/
- Keys/license-public.pem

Suggested order:

1. Double-click 1-Install-Client.cmd
2. Reopen WPS Spreadsheets
3. Open the license panel and copy machine code
4. Paste the activation code from the issuer
5. Start generating documents

After installation, GeneratorService starts silently at Windows logon, and WPS loads the add-in automatically.
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
