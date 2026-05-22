@echo off
setlocal
chcp 65001 >nul

cd /d "%~dp0..\.."
set "ROOT=%CD%"
set "PUBLISHED_EXE=%ROOT%\publish\LicenseTool\LicenseTool.exe"
set "PROJECT_FILE=%ROOT%\src\LicenseTool\LicenseTool.csproj"

echo ========================================
echo   LicenseTool - Generate RSA Key Pair
echo ========================================
echo.
echo Project root:
echo %ROOT%
echo.

if exist "%PUBLISHED_EXE%" (
    echo Found published tool. Generating key pair...
    "%PUBLISHED_EXE%" --generate-keypair
) else (
    echo Published LicenseTool.exe not found. Falling back to source mode...
    dotnet run --project "%PROJECT_FILE%" -- --generate-keypair
)

echo.
echo Please confirm:
echo 1. Keep Keys\license-private.pem on your own computer only
echo 2. Keys\license-public.pem can be shipped with the client
echo.
if /i "%CODEX_NO_PAUSE%"=="1" goto :eof
pause
