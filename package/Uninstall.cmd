@echo off
rem Removes NosCrosshair, its shortcuts and its saved settings.
setlocal
set "DEST=%LOCALAPPDATA%\Programs\NosCrosshair"

choice /m "Uninstall NosCrosshair and delete its settings"
if errorlevel 2 exit /b 0

taskkill /im NosCrosshair.exe /f >nul 2>&1
powershell -NoProfile -Command ^
  "foreach ($d in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {" ^
  "  Remove-Item -LiteralPath (Join-Path $d 'NosCrosshair.lnk') -ErrorAction SilentlyContinue }"
rmdir /s /q "%APPDATA%\NosCrosshair" 2>nul
echo NosCrosshair has been uninstalled.
timeout /t 3 >nul 2>&1

rem This file lives inside the folder being deleted, so remove the folder as the very last step.
cd /d "%TEMP%"
(goto) 2>nul & rmdir /s /q "%DEST%"
