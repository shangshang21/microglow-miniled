param([string]$Destination = (Join-Path $PSScriptRoot '..\..\build\MicroGlow'))
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(64,64)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)
$brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(225,235,216))
$graphics.FillEllipse($brush,2,2,60,60)
$pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(71,104,75),3)
$graphics.DrawEllipse($pen,22,22,20,20)
for($i=0;$i -lt 8;$i++){ $angle=$i*[Math]::PI/4; $graphics.DrawLine($pen,[single](32+16*[Math]::Cos($angle)),[single](32+16*[Math]::Sin($angle)),[single](32+23*[Math]::Cos($angle)),[single](32+23*[Math]::Sin($angle))) }
$icon=[Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconStream=[IO.File]::Create((Join-Path $destination 'MicroGlow.ico'))
$icon.Save($iconStream);$iconStream.Dispose();$graphics.Dispose();$bitmap.Dispose();$brush.Dispose();$pen.Dispose();$icon.Dispose()
$references = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/out:' + (Join-Path $destination 'MicroGlow.exe')),('/win32icon:' + (Join-Path $destination 'MicroGlow.ico')),('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest'))) + $references + @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'MicroGlow build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\MainWindow.xaml') -Destination (Join-Path $destination 'MainWindow.xaml') -Force
Write-Output (Join-Path $destination 'MicroGlow.exe')
