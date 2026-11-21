# MaydaySave 卸载器
# 只删 mod 与加载器相关文件，不动游戏本体、不动存档。

$ErrorActionPreference = 'SilentlyContinue'
$root = $PSScriptRoot

function Info($m) { Write-Host $m -ForegroundColor Cyan }

Write-Host ''
Write-Host '==== MaydaySave 卸载器 ====' -ForegroundColor Cyan
Write-Host ('游戏目录：' + $root)
Write-Host ''

$removed = @()

# MelonLoader 版
$ml = Join-Path $root 'Mods\MaydaySave'
if (Test-Path $ml) { Remove-Item $ml -Recurse -Force; $removed += 'Mods\MaydaySave（MelonLoader 版）' }
$mlFlat = Join-Path $root 'Mods\MaydaySave.dll'
if (Test-Path $mlFlat) { Remove-Item $mlFlat -Force; $removed += 'Mods\MaydaySave.dll（MelonLoader 版）' }

# BepInEx 版
$bep = Join-Path $root 'BepInEx\plugins\MaydaySave.dll'
if (Test-Path $bep) { Remove-Item $bep -Force; $removed += 'BepInEx\plugins\MaydaySave.dll（BepInEx 版）' }

foreach ($f in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
    $p = Join-Path $root $f
    if (Test-Path $p) { Remove-Item $p -Force; $removed += $f }
}
$bepDir = Join-Path $root 'BepInEx'
if (Test-Path $bepDir) { Remove-Item $bepDir -Recurse -Force; $removed += 'BepInEx\' }
$dotnetDir = Join-Path $root 'dotnet'
if (Test-Path $dotnetDir) {
    # 只删 BepInEx 的 Doorstop 运行时，MelonLoader 也可能用 dotnet\，所以问一下
    $hasMelon = Test-Path (Join-Path $root 'MelonLoader')
    if ($hasMelon) {
        Write-Host '保留了 dotnet\（MelonLoader 也在用），如需彻底清理请手动删除。' -ForegroundColor Yellow
    } else {
        Remove-Item $dotnetDir -Recurse -Force
        $removed += 'dotnet\'
    }
}

Write-Host ''
if ($removed.Count -eq 0) {
    Write-Host '没有找到需要删除的文件。' -ForegroundColor Yellow
} else {
    Info '已删除：'
    foreach ($r in $removed) { Write-Host ('  ' + $r) }
}
Write-Host ''
Write-Host '存档与配置未动：'
Write-Host '  BepInEx\MaydaySave\saves\      （BepInEx 版）'
Write-Host '  UserData\MaydaySave\saves\     （MelonLoader 版）'
Write-Host ''
Write-Host '如果只想卸载存档 MOD、保留加载器，请手动只删上面列出的 MaydaySave.dll。'
