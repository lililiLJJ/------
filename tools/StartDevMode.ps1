param(
    [int]$WpsDebugPort = 3889
)

$ErrorActionPreference = "Stop"
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
$serviceProject = Join-Path $root "src/GeneratorService/GeneratorService.csproj"
$addinDir = Join-Path $root "src/WpsAddin"
$logDir = Join-Path $root "Logs"
$serviceLog = Join-Path $logDir "dev-generator-service.log"
$serviceErrorLog = Join-Path $logDir "dev-generator-service-error.log"

function Find-WpsJs {
    $candidates = @(
        (Join-Path $env:APPDATA "npm/wpsjs.cmd"),
        "C:/Users/ljj/AppData/Roaming/npm/wpsjs.cmd"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -ErrorAction SilentlyContinue)) {
            return $candidate
        }
    }

    $command = Get-Command "wpsjs.cmd" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

function Wait-GeneratorService {
    for ($index = 0; $index -lt 30; $index++) {
        try {
            Invoke-RestMethod -Uri "http://127.0.0.1:5188/api/health" -TimeoutSec 1 | Out-Null
            return $true
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    return $false
}

if (-not (Test-Path -LiteralPath $serviceProject)) {
    throw "GeneratorService project not found: $serviceProject"
}

if (-not (Test-Path -LiteralPath $addinDir)) {
    throw "WpsAddin directory not found: $addinDir"
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Write-Host "== Engineering Docs Dev Mode ==" -ForegroundColor Cyan
Write-Host "Workspace: $root"
Write-Host ""

Write-Host "Stopping installed/dev GeneratorService processes..." -ForegroundColor Yellow
Get-Process -Name "GeneratorService" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*GeneratorService.csproj*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Write-Host "Starting source GeneratorService..." -ForegroundColor Yellow
$serviceProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $serviceProject) `
    -WorkingDirectory $root `
    -WindowStyle Hidden `
    -RedirectStandardOutput $serviceLog `
    -RedirectStandardError $serviceErrorLog `
    -PassThru

if (-not (Wait-GeneratorService)) {
    Write-Host "GeneratorService did not become healthy in time." -ForegroundColor Red
    Write-Host "Stdout log: $serviceLog"
    Write-Host "Error log:  $serviceErrorLog"
    exit 1
}

Write-Host "GeneratorService is running from source. PID: $($serviceProcess.Id)" -ForegroundColor Green
Write-Host "Service URL: http://127.0.0.1:5188"
Write-Host "Service log: $serviceLog"
Write-Host ""

$wpsjs = Find-WpsJs
if (-not $wpsjs) {
    Write-Host "wpsjs.cmd was not found." -ForegroundColor Red
    Write-Host "Install it first, then rerun this script. Example: npm install -g wpsjs"
    exit 1
}

Write-Host "Starting WPS add-in debug from source..." -ForegroundColor Yellow
Write-Host "WPS debug port: $WpsDebugPort"
Write-Host "Close this window or press Ctrl+C to stop wpsjs debug."
Write-Host ""

Set-Location $addinDir
& $wpsjs debug -p $WpsDebugPort
