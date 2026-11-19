# One-time cleanup after the src\ restructure (docs\ARCHITECTURE.md).
# Moves the old top-level sources (src\*.cs, src\scene, src\AWall.manifest, src\Set-LockImage.ps1)
# into the project's trash folder. Never deletes anything.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\Finish-Restructure.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$trashName = [string][char]0x5783 + [char]0x573E
$target = Join-Path (Join-Path $root $trashName) ('src-before-restructure-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

foreach ($required in 'App\AWall.manifest', 'App\Program.cs', 'Platform\Set-LockImage.ps1', 'Render\scene\index.html', 'UI\MainWindow\MainWindow.cs') {
  if (-not (Test-Path -LiteralPath (Join-Path $src $required))) { throw "src\$required is missing; the old files were not moved." }
}

$old = @()
$old += Get-ChildItem -LiteralPath $src -Filter *.cs -File
foreach ($name in 'AWall.manifest', 'Set-LockImage.ps1', 'scene') {
  $path = Join-Path $src $name
  if (Test-Path -LiteralPath $path) { $old += Get-Item -LiteralPath $path }
}
if ($old.Count -eq 0) { Write-Host 'Nothing to move.'; return }

New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($item in $old) {
  Move-Item -LiteralPath $item.FullName -Destination $target
  Write-Host ('Moved ' + $item.Name)
}
Write-Host ('Old files are in ' + $target)
