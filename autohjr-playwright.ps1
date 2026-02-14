param(
  [string]$Browser = "chromium"
)

$exePath = Join-Path $PSScriptRoot "AutomationAPI.exe"
if (!(Test-Path $exePath)) {
  Write-Host "ERROR: No se encuentra AutomationAPI.exe en esta carpeta."
  exit 1
}

Write-Host "===================================="
Write-Host "AutoHJR360 - Instalacion Playwright"
Write-Host "===================================="
Write-Host "Instalando navegador: $Browser"
Write-Host ""

& $exePath --install-playwright
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
  Write-Host ""
  Write-Host "ERROR: No se pudo instalar Playwright."
  exit $exitCode
}

Write-Host ""
Write-Host "Instalacion completada."
exit 0
