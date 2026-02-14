@echo off
setlocal

set SCRIPT=%~dp0autohjr-playwright.ps1

if not exist "%SCRIPT%" (
  echo ERROR: No se encontro autohjr-playwright.ps1
  echo Asegurate de ejecutar este archivo desde la carpeta de AutoHJR360
  pause
  exit /b 1
)

powershell -ExecutionPolicy Bypass -File "%SCRIPT%"
if errorlevel 1 (
  echo.
  echo ERROR: Fallo la instalacion de Playwright.
  pause
  exit /b 1
)

echo.
echo Playwright instalado correctamente.
pause
exit /b 0
