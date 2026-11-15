param([string]$OutputDirectory = "bin")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$output = Join-Path $root $OutputDirectory
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
New-Item -ItemType Directory -Path "$output" -Force | Out-Null
& "$fx\csc.exe" /nologo /target:winexe /platform:x64 /win32manifest:"$root\src\AWall.manifest" /out:"$output\AWall.exe" /reference:"$fx\WPF\PresentationFramework.dll" /reference:"$fx\WPF\PresentationCore.dll" /reference:"$fx\WPF\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll /reference:"$root\vendor\webview2\Microsoft.Web.WebView2.Core.dll" /reference:"$root\vendor\webview2\Microsoft.Web.WebView2.Wpf.dll" "$root\src\AWall.cs" "$root\src\ScenePlayer.cs" "$root\src\MpvPlayer.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath "$root\src\Set-LockImage.ps1" -Destination "$output\Set-LockImage.ps1" -Force
Copy-Item "$root\vendor\webview2\*.dll" -Destination "$output" -Force
New-Item -ItemType Directory -Path "$output\scene" -Force | Out-Null
Copy-Item "$root\src\scene\*" -Destination "$output\scene" -Force
Copy-Item -LiteralPath "$root\vendor\webwallgl\webwallgl.global.min.js" -Destination "$output\scene" -Force
Copy-Item -LiteralPath "$root\vendor\webwallgl\LICENSE" -Destination "$output\scene\WebWallGL-LICENSE.txt" -Force
Copy-Item -LiteralPath "$root\vendor\mediabunny\LICENSE" -Destination "$output\scene\Mediabunny-LICENSE.txt" -Force
Copy-Item -LiteralPath "$root\vendor\webview2\LICENSE.txt" -Destination "$output\WebView2-LICENSE.txt" -Force
Copy-Item -LiteralPath "$root\THIRD-PARTY-NOTICES.md" -Destination "$output" -Force



New-Item -ItemType Directory -Path "$output\mpv" -Force | Out-Null
Copy-Item "$root\vendor\mpv\*" -Destination "$output\mpv" -Force

Write-Host "Built $output\AWall.exe"
