# MaydaySave 一键适配脚本（给装了 MelonLoader 模组管理器的玩家）
#
# 本压缩包默认就是 BepInEx 版（解压到游戏根目录即用）。
# 如果你已经装了模组管理器（游戏根有 version.dll + MelonLoader\），两边会抢 coreclr.dll，
# 双击「安装到模组管理器.bat」或直接跑本脚本，它会把冲突的 BepInEx 文件收进
# _旧版BepInEx_已停用\，并确认 MelonLoader 版模组已就位。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# 双击 .bat 时脚本可能被复制到别处，优先用游戏目录
if (-not (Test-Path (Join-Path $root 'MaydayAirways.exe'))) {
    $root = Split-Path -Parent $PSCommandPath
}

function Say($m) { Write-Host $m }
function Info($m) { Write-Host $m -ForegroundColor Cyan }
function Warn($m) { Write-Host $m -ForegroundColor Yellow }
function Ok($m) { Write-Host $m -ForegroundColor Green }
function Bad($m) { Write-Host $m -ForegroundColor Red }

Say ''
Say '==== MaydaySave 适配：切换到 MelonLoader / 模组管理器 ===='
if (-not (Test-Path (Join-Path $root 'MaydayAirways.exe'))) {
    Bad '这个目录里没有 MaydayAirways.exe。'
    Bad '请把本文件放在游戏根目录（和 MaydayAirways.exe 同一层）再运行。'
    exit 1
}
Say ('游戏目录：' + $root)
Say ''

$hasMelon = (Test-Path (Join-Path $root 'version.dll')) -and (Test-Path (Join-Path $root 'MelonLoader'))

if (-not $hasMelon) {
    Warn '没有检测到 MelonLoader（version.dll + MelonLoader 目录）。'
    Warn '如果你没装模组管理器，不需要运行这个脚本——本压缩包解压到游戏根目录就已经是可用状态。'
    Say ''
    $ans = Read-Host '要继续把 MelonLoader 版装到 Mods\MaydaySave 吗？(y/N)'
    if ($ans -notmatch '^[Yy]') { exit 0 }
}

# ---- 1. 收起冲突的 BepInEx / Doorstop 文件 ----
$park = Join-Path $root '_旧版BepInEx_已停用'
$conflicts = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'BepInEx')
$moved = @()
foreach ($name in $conflicts) {
    $p = Join-Path $root $name
    if (-not (Test-Path $p)) { continue }
    New-Item -ItemType Directory -Force -Path $park | Out-Null
    $dst = Join-Path $park $name
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    Move-Item $p $dst -Force
    $moved += $name
}

if ($moved.Count -gt 0) {
    Ok ('已收起：' + ($moved -join ', '))
    Say  ('       -> ' + $park)
} else {
    Info '没有需要收起的 BepInEx 文件。'
}

# ---- 2. 确认 MelonLoader 版模组就位 ----
$modDir = Join-Path $root 'Mods\MaydaySave'
$modDll = Join-Path $modDir 'MaydaySave.dll'
$modManifest = Join-Path $modDir 'manifest.json'

if (-not (Test-Path $modDll)) {
    # 压缩包里自带一份
    $src = Join-Path $root 'MelonLoader版\Mods\MaydaySave'
    if (Test-Path $src) {
        New-Item -ItemType Directory -Force -Path $modDir | Out-Null
        Copy-Item (Join-Path $src '*') $modDir -Force
        Info '已从压缩包安装 MelonLoader 版到 Mods\MaydaySave'
    } else {
        Bad '找不到 Mods\MaydaySave\MaydaySave.dll，也没有内嵌的 MelonLoader版 目录。'
        Bad '请重新解压完整压缩包后再运行。'
        exit 1
    }
}

if (-not (Test-Path $modManifest)) {
    Warn '缺少 Mods\MaydaySave\manifest.json，MelonLoader 可能不会加载这个子目录模组。'
}

# ---- 3. 去重：管理器导入的包和本地子目录只能留一份 ----
$managed = @(Get-ChildItem (Join-Path $root 'Mods') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'MaydayPkg.*' } |
    Where-Object { Get-ChildItem $_.FullName -Recurse -Filter 'MaydaySave.dll' -ErrorAction SilentlyContinue })

$localDll = Join-Path $root 'Mods\MaydaySave\MaydaySave.dll'
$hasLocal = Test-Path $localDll
$dupDir = Join-Path $root '_重复副本_可删'

if ($managed.Count -gt 0 -and $hasLocal) {
    New-Item -ItemType Directory -Force -Path $dupDir | Out-Null
    $dst = Join-Path $dupDir ('MaydaySave_' + (Get-Date -Format 'HHmmss'))
    Move-Item (Join-Path $root 'Mods\MaydaySave') $dst -Force
    Warn '检测到管理器已经导入过本 MOD（Mods\MaydayPkg.*）。'
    Warn ('为避免同名程序集被加载两次，已把本地副本挪到：' + $dst)
    Ok ('管理器托管中的副本：' + $managed[0].FullName)
}
elseif ($managed.Count -gt 0) {
    Info '管理器已经导入过本 MOD，无需再放本地副本。'
    Ok ('管理器托管中的副本：' + $managed[0].FullName)
}
else {
    Ok ('MelonLoader 版已就位：' + $modDll)
}
Say ''
Say '接下来：'
Say '  1. 启动游戏（或重启一次，让 MelonLoader 重新扫描 Mods）'
Say '  2. 热键 F8 打开存档界面；存档目录 UserData\MaydaySave\saves'
Say '  3. 想出现在「MOD 管理」列表里：用管理器「添加 MOD」选压缩包根目录的 MaydaySave-5.2.1.maydaymod'
Say ''
Say '想换回 BepInEx：删除 version.dll 与 MelonLoader\，把 _旧版BepInEx_已停用\ 里的东西挪回根目录。'
