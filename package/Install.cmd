@echo off
rem Installs NosCrosshair for the current user (no admin needed) and adds Start menu + Desktop shortcuts.
setlocal
set "DEST=%LOCALAPPDATA%\Programs\NosCrosshair"

if not exist "%~dp0NosCrosshair.exe" (
    echo NosCrosshair.exe was not found next to Install.cmd.
    echo Right-click the zip, choose "Extract All...", then run Install.cmd from the extracted folder.
    pause
    exit /b 1
)

taskkill /im NosCrosshair.exe /f >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0NosCrosshair.exe" "%DEST%\" >nul || goto :failed
copy /y "%~dp0Uninstall.cmd" "%DEST%\" >nul || goto :failed

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Unblock-File -LiteralPath (Join-Path $env:DEST 'NosCrosshair.exe');" ^
  "$s = New-Object -ComObject WScript.Shell;" ^
  "foreach ($d in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {" ^
  "  $l = $s.CreateShortcut((Join-Path $d 'NosCrosshair.lnk'));" ^
  "  $l.TargetPath = Join-Path $env:DEST 'NosCrosshair.exe';" ^
  "  $l.WorkingDirectory = $env:DEST;" ^
  "  $l.Save() }"
if errorlevel 1 goto :failed

echo.
echo NosCrosshair installed to %DEST%
echo Shortcuts added to the Start menu and Desktop. Starting it now...
start "" "%DEST%\NosCrosshair.exe"
timeout /t 3 >nul 2>&1
exit /b 0

:failed
echo.
echo Install failed. Close NosCrosshair if it is open and try again.
pause
exit /b 1
