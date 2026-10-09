# Builds NosCrosshair.exe with the C# compiler that ships with Windows.
# Pass -Install to also copy it to %LOCALAPPDATA%\Programs\NosCrosshair with shortcuts.
param([switch]$Install)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = '/reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll'
Push-Location $here
try {
    # First pass without an icon, used only to draw the icon itself.
    & $csc /nologo /target:winexe /optimize $refs /out:NosCrosshair.exe NosCrosshair.cs
    if ($LASTEXITCODE) { throw 'compile failed' }
    Start-Process -Wait -FilePath .\NosCrosshair.exe -ArgumentList '--write-icon', "$here\NosCrosshair.ico"
    & $csc /nologo /target:winexe /optimize $refs /win32icon:NosCrosshair.ico /out:NosCrosshair.exe NosCrosshair.cs
    if ($LASTEXITCODE) { throw 'compile failed' }
    Write-Host 'Built NosCrosshair.exe'

    if ($Install) {
        $dest = Join-Path $env:LOCALAPPDATA 'Programs\NosCrosshair'
        Get-Process NosCrosshair -ErrorAction SilentlyContinue | Stop-Process -Force
        New-Item -ItemType Directory -Force $dest | Out-Null
        Copy-Item .\NosCrosshair.exe $dest -Force
        $shell = New-Object -ComObject WScript.Shell
        foreach ($dir in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
            $lnk = $shell.CreateShortcut((Join-Path $dir 'NosCrosshair.lnk'))
            $lnk.TargetPath = Join-Path $dest 'NosCrosshair.exe'
            $lnk.WorkingDirectory = $dest
            $lnk.Save()
        }
        Write-Host "Installed to $dest (Start menu + Desktop shortcuts)"
    }
} finally { Pop-Location }
