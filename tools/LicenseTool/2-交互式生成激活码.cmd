@echo off
setlocal
chcp 65001 >nul

cd /d "%~dp0..\.."
set "ROOT=%CD%"
set "PUBLISHED_EXE=%ROOT%\publish\LicenseTool\LicenseTool.exe"
set "PROJECT_FILE=%ROOT%\src\LicenseTool\LicenseTool.csproj"

echo ========================================
echo   LicenseTool - Interactive Activation Code
echo ========================================
echo.
echo Prepare these values first:
echo 1. Customer machine code hash
echo 2. License type: trial / month / year / permanent
echo 3. Expire date: yyyy-MM-dd ^(leave blank for permanent^)
echo 4. Modules: generate,batch,ai
echo.

if exist "%PUBLISHED_EXE%" (
    echo Found published tool. Starting interactive mode...
    "%PUBLISHED_EXE%"
) else (
    echo Published LicenseTool.exe not found. Falling back to source mode...
    dotnet run --project "%PROJECT_FILE%"
)

echo.
echo Reminder:
echo - Send the final Base64 activation code text to the customer
echo - Never send the private key file to the customer
echo.
if /i "%CODEX_NO_PAUSE%"=="1" goto :eof
pause
