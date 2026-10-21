param([string]$PackageDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\MicroGlowOSD'
$executable = Join-Path $installDirectory 'MicroGlow.exe'
foreach ($name in @('MicroGlow.exe','MainWindow.xaml','MicroGlow.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $name))) { throw "Package file missing: $name" }
}
Get-Process MicroGlow -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -eq $executable -or $_.Path -eq (Join-Path $PackageDirectory 'MicroGlow.exe')) {
        Stop-Process -Id $_.Id
        $_.WaitForExit(5000) | Out-Null
    } else { throw 'Another MicroGlow build is running. Close it before installing.' }
}
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
foreach ($name in @('MicroGlow.exe','MainWindow.xaml','MicroGlow.ico','使用说明.md')) {
    $source = Join-Path $PackageDirectory $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $installDirectory $name) -Force }
}
$shortcutShell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))) {
    $shortcut = $shortcutShell.CreateShortcut((Join-Path $folder '微光 OSD.lnk'))
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.IconLocation = (Join-Path $installDirectory 'MicroGlow.ico') + ',0'
    $shortcut.Description = '微光：屏幕亮度、声音与色彩控制'
    $shortcut.Save()
}
Start-Process -FilePath $executable -WindowStyle Normal
Write-Output $executable
