@echo off
REM Script para ejecutar AutoHJR360
REM Autor: Sistema AutoHJR360

title AutoHJR360 - Sistema de Automatizacion

echo ====================================
echo AutoHJR360 - Sistema de Automatizacion
echo ====================================
echo.

REM Verificar si existe el ejecutable
if not exist "AutomationAPI.exe" (
    echo ERROR: No se encuentra AutomationAPI.exe
    echo Asegurate de estar en la carpeta correcta
    pause
    exit /b 1
)

REM Verificar si existe la configuracion
if not exist "appsettings.json" (
    echo ADVERTENCIA: No se encuentra appsettings.json
    echo Se usara la configuracion por defecto
    echo.
)

echo Iniciando servidor...
echo.
echo La aplicacion estara disponible en:
echo   http://localhost:5016
echo.
echo Presiona Ctrl+C para detener el servidor
echo ====================================
echo.

REM Ejecutar el servidor
AutomationAPI.exe

pause

