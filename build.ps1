# Default build plays MP4 with the built-in WebView2 player and ships no mpv.
# -WithMpv also copies vendor\mpv (about 120 MB) so "MP4 player: mpv" can be chosen in AWall.
param([string]$OutputDirectory = "bin", [switch]$WithMpv)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$output = Join-Path $root $OutputDirectory
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
New-Item -ItemType Directory -Path "$output" -Force | Out-Null

# Layered sources (see docs\ARCHITECTURE.md): every .cs under these folders is compiled.
# .cs files directly in src\ are pre-restructure copies and are NOT compiled.
$layers = 'App', 'Core', 'Platform', 'Render', 'Features', 'UI', 'Diagnostics'
$sources = @()
foreach ($layer in $layers) {
  $dir = Join-Path $src $layer
  if (-not (Test-Path $dir)) { throw "Missing source folder: src\$layer" }
  $sources += Get-ChildItem -LiteralPath $dir -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object { $_.FullName }
}
$stale = @(Get-ChildItem -LiteralPath $src -Filter *.cs -File)
if ($stale.Count -gt 0) {
  Write-Host ("Note: src\ still has " + $stale.Count + " old top-level .cs file(s) (" + (($stale | ForEach-Object { $_.Name }) -join ', ') + "). They are not compiled; run tools\Finish-Restructure.ps1 to move them into the trash folder.")
}

& "$fx\csc.exe" /nologo /target:winexe /platform:x64 /win32manifest:"$src\App\AWall.manifest" /win32icon:"$root\assets\AWall.ico" /out:"$output\AWall.exe" /reference:"$fx\WPF\PresentationFramework.dll" /reference:"$fx\WPF\PresentationCore.dll" /reference:"$fx\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll /reference:"$root\vendor\webview2\Microsoft.Web.WebView2.Core.dll" /reference:"$root\vendor\webview2\Microsoft.Web.WebView2.Wpf.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
# AWallCmd.exe: console front end for the command pipe (AWall.exe --cmd does the same, but as a GUI exe
# PowerShell and cmd do not wait for it). src\Cli is not a layer, so it is not compiled into AWall.exe.
$cliSources = "$src\Cli\AWallCmd.cs", "$src\App\CommandPipe.cs", "$src\App\AppPaths.cs", "$src\Core\Commands\CommandBus.cs"
& "$fx\csc.exe" /nologo /target:exe /platform:x64 /win32icon:"$root\assets\AWall.ico" /out:"$output\AWallCmd.exe" /reference:System.Web.Extensions.dll /reference:System.Core.dll $cliSources
if ($LASTEXITCODE -ne 0) { throw 'AWallCmd build failed' }
Copy-Item -LiteralPath "$root\assets\AWall.ico" -Destination "$output\AWall.ico" -Force
Copy-Item -LiteralPath "$src\Platform\Set-LockImage.ps1" -Destination "$output\Set-LockImage.ps1" -Force
Copy-Item "$root\vendor\webview2\*.dll" -Destination "$output" -Force
New-Item -ItemType Directory -Path "$output\scene" -Force | Out-Null
Copy-Item "$src\Render\scene\*" -Destination "$output\scene" -Force
Copy-Item -LiteralPath "$root\vendor\webwallgl\webwallgl.global.min.js" -Destination "$output\scene" -Force
Copy-Item -LiteralPath "$root\vendor\webwallgl\LICENSE" -Destination "$output\scene\WebWallGL-LICENSE.txt" -Force
Copy-Item -LiteralPath "$root\vendor\mediabunny\LICENSE" -Destination "$output\scene\Mediabunny-LICENSE.txt" -Force
Copy-Item -LiteralPath "$root\vendor\webview2\LICENSE.txt" -Destination "$output\WebView2-LICENSE.txt" -Force
Copy-Item -LiteralPath "$root\THIRD-PARTY-NOTICES.md" -Destination "$output" -Force

if ($WithMpv) {
  if (-not (Test-Path "$root\vendor\mpv\mpv.exe")) { throw 'vendor\mpv\mpv.exe not found; cannot build -WithMpv' }
  New-Item -ItemType Directory -Path "$output\mpv" -Force | Out-Null
  Copy-Item "$root\vendor\mpv\*" -Destination "$output\mpv" -Force
  Write-Host "Included optional mpv backend in $output\mpv"
} elseif (Test-Path "$output\mpv") {
  Write-Host "Note: $output\mpv is left over from an older build (about 120 MB). AWall now plays MP4 with its built-in player; delete that folder to slim the build, or rebuild with -WithMpv to keep mpv selectable."
}

Write-Host "Built $output\AWall.exe"
