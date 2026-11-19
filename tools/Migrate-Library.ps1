# 把 library\wallpaper-engine\<工坊ID>\ 的旧结构整理成按类型分的目录：
#   library\wallpapers\pkg\<标题>.pkg  + <标题>.json（原 project.json，PKG 渲染需要）+ <标题>.jpg/.gif（预览图）
#   library\wallpapers\mp4\<标题>.mp4  + <标题>.jpg/.gif
# 不兼容的类型（web / application 等）和 Wallpaper Engine 自己的着色器缓存（shaders\blobsSM40）不迁移，最后随旧目录一起删除。
#
# 默认只预览，不改任何文件：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Migrate-Library.ps1
# 确认无误后执行：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Migrate-Library.ps1 -Apply
param([switch]$Apply)
$ErrorActionPreference = 'Stop'

$project = Split-Path $PSScriptRoot -Parent
$library = Join-Path $project 'library'
$source  = Join-Path $library 'wallpaper-engine'
$pkgDir  = Join-Path $library 'wallpapers\pkg'
$mp4Dir  = Join-Path $library 'wallpapers\mp4'
if (-not (Test-Path -LiteralPath $source)) { Write-Host "没有找到 $source，无需迁移。"; exit 0 }

$invalid = [IO.Path]::GetInvalidFileNameChars()
function Clean-Name([string]$text, [string]$fallback) {
    if ([string]::IsNullOrWhiteSpace($text)) { $text = $fallback }
    $chars = $text.ToCharArray() | ForEach-Object { if ($invalid -contains $_) { '_' } else { $_ } }
    $name = (-join $chars).Trim().TrimEnd('.')
    if ($name.Length -gt 80) { $name = $name.Substring(0, 80).Trim() }
    if ($name -eq '') { $name = $fallback }
    return $name
}

$used = @{}
foreach ($dir in @($pkgDir, $mp4Dir)) {
    if (Test-Path -LiteralPath $dir) { Get-ChildItem -LiteralPath $dir -File | ForEach-Object { $used[$_.FullName.ToLowerInvariant()] = $true } }
}
function Unique-Target([string]$folder, [string]$name, [string]$ext, [string]$id) {
    $path = Join-Path $folder ($name + $ext)
    if ($used.ContainsKey($path.ToLowerInvariant())) { $path = Join-Path $folder ("$name ($id)" + $ext) }
    $used[$path.ToLowerInvariant()] = $true
    return $path
}

$plan = @(); $skipped = @()
foreach ($dir in Get-ChildItem -LiteralPath $source -Directory) {
    $id = $dir.Name
    $pj = Join-Path $dir.FullName 'project.json'
    if (-not (Test-Path -LiteralPath $pj)) { $skipped += [pscustomobject]@{ID=$id; 类型='?'; 标题=''; 原因='没有 project.json'}; continue }
    $json  = Get-Content -LiteralPath $pj -Raw -Encoding UTF8 | ConvertFrom-Json
    $type  = ("$($json.type)").ToLowerInvariant(); if ($type -eq '') { $type = 'scene' }
    $title = Clean-Name "$($json.title)" $id
    $entry = if ($json.file) { Join-Path $dir.FullName $json.file } else { '' }
    $preview = Join-Path $dir.FullName $(if ($json.preview) { $json.preview } else { 'preview.jpg' })

    if ($type -eq 'video' -and $entry -and $entry.ToLowerInvariant().EndsWith('.mp4') -and (Test-Path -LiteralPath $entry)) {
        $target = Unique-Target $mp4Dir $title '.mp4' $id
        $plan += [pscustomobject]@{ID=$id; 类型='MP4'; File=$entry; Target=$target; Project=$null; Preview=$preview}
    }
    elseif ($type -eq 'scene') {
        $pkg = if ($entry -and $entry.ToLowerInvariant().EndsWith('.pkg') -and (Test-Path -LiteralPath $entry)) { $entry } else { Join-Path $dir.FullName 'scene.pkg' }
        if (-not (Test-Path -LiteralPath $pkg)) { $skipped += [pscustomobject]@{ID=$id; 类型=$type; 标题=$title; 原因='松散场景，没有 scene.pkg'}; continue }
        $target = Unique-Target $pkgDir $title '.pkg' $id
        $plan += [pscustomobject]@{ID=$id; 类型='PKG'; File=$pkg; Target=$target; Project=$pj; Preview=$preview}
    }
    else { $skipped += [pscustomobject]@{ID=$id; 类型=$type; 标题=$title; 原因='不兼容的类型'} }
}

Write-Host "`n将迁移 $($plan.Count) 个：" -ForegroundColor Cyan
$plan | Select-Object ID, 类型, @{n='新文件'; e={ $_.Target.Substring($library.Length + 1) }} | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "将删除 $($skipped.Count) 个不兼容素材：" -ForegroundColor Yellow
$skipped | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if (-not $Apply) { Write-Host '这只是预览，没有改动任何文件。确认后加 -Apply 运行。' -ForegroundColor Green; exit 0 }

New-Item -ItemType Directory -Force -Path $pkgDir, $mp4Dir | Out-Null
foreach ($p in $plan) {
    Move-Item -LiteralPath $p.File -Destination $p.Target
    $base = [IO.Path]::ChangeExtension($p.Target, $null).TrimEnd('.')
    if ($p.Project) { Copy-Item -LiteralPath $p.Project -Destination ($base + '.json') }
    if (Test-Path -LiteralPath $p.Preview) { Move-Item -LiteralPath $p.Preview -Destination ($base + [IO.Path]::GetExtension($p.Preview).ToLowerInvariant()) }
    Write-Host "已迁移 $($p.ID) -> $($p.Target.Substring($library.Length + 1))"
}
# 所有可用文件都已移出；剩下的只有不兼容素材、着色器缓存和空文件夹。
Remove-Item -LiteralPath $source -Recurse -Force
Write-Host "`n完成：已删除 $source（含 $($skipped.Count) 个不兼容素材）。" -ForegroundColor Green
