# 生成 MelonLoader 版本的 MaydaySave.dll
# 原理：MelonLoader 的 Il2CppInterop 生成程序集会给游戏命名空间加 "Il2Cpp" 前缀
#       （Il2CppMayday.* / Il2CppTMPro.* / Il2CppMirror.*），UnityEngine 保持原样。
#       这里把共享源码复制到临时目录做改名，再针对 MelonLoader 的程序集编译。

param(
    [Parameter(Mandatory = $true)][string]$GameDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$shared = Join-Path $root 'src\MaydaySave'
$mlSrc = Join-Path $root 'src\MaydaySaveML'
$work = Join-Path $env:TEMP 'maydaysave_ml_build'
$outDir = Join-Path $root 'dist_ml'

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work, $outDir | Out-Null

Write-Host '[1/4] 复制并改名共享源码'
$sharedFiles = @('GameHost.cs', 'SaveData.cs', 'SaveGame.cs', 'SaveManager.cs', 'SaveUI.cs', 'Snapshot.cs', 'SettingsUI.cs')
foreach ($f in $sharedFiles) {
    $text = Get-Content (Join-Path $shared $f) -Raw -Encoding UTF8
    $text = [regex]::Replace($text, '\bMayday\.', 'Il2CppMayday.')
    $text = [regex]::Replace($text, '\bTMPro\.', 'Il2CppTMPro.')
    $text = [regex]::Replace($text, '\bMirror\.', 'Il2CppMirror.')
    [System.IO.File]::WriteAllText((Join-Path $work $f), $text, (New-Object System.Text.UTF8Encoding($false)))
}
$mlPlugin = Get-Content (Join-Path $mlSrc 'Plugin.cs') -Raw -Encoding UTF8
$mlPlugin = [regex]::Replace($mlPlugin, '\bMayday\.', 'Il2CppMayday.')
[System.IO.File]::WriteAllText((Join-Path $work 'Plugin.cs'), $mlPlugin, (New-Object System.Text.UTF8Encoding($false)))

Write-Host '[2/4] 收集参考程序集'
$lines = @('-nologo', '-target:library', '-nostdlib+', '-langversion:latest', '-unsafe+',
    '-nowarn:CS0169,CS0649,CS0414,CS8632',
    ('-out:"' + (Join-Path $outDir 'MaydaySave.dll') + '"'))

$runtime = Get-ChildItem (Join-Path $GameDir 'dotnet\shared\Microsoft.NETCore.App') -Directory |
    Sort-Object Name -Descending | Select-Object -First 1
$runtimeDir = $runtime.FullName
Write-Host ("  runtime: " + $runtimeDir)

Get-ChildItem $runtimeDir -Filter *.dll | Where-Object {
    $_.Name -match '^(System\.|Microsoft\.|netstandard\.dll$|mscorlib\.dll$)' -and
    $_.Name -notmatch 'Native|api-ms|mscordaccore|mscordbi|mscorrc|msquic|coreclr|clrjit|hostpolicy|dbgshim|clretwrc|DiaSymReader'
} | ForEach-Object { $lines += ('-r:"' + $_.FullName + '"') }

$mlNet = Join-Path $GameDir 'MelonLoader\net6'
Get-ChildItem $mlNet -Filter *.dll | Where-Object {
    $_.Name -in @('MelonLoader.dll', 'Il2CppInterop.Runtime.dll', 'Il2CppInterop.Common.dll')
} | ForEach-Object { $lines += ('-r:"' + $_.FullName + '"') }

$il2cppAsm = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'
if (-not (Test-Path $il2cppAsm)) {
    throw "找不到 $il2cppAsm，请先启动一次游戏让 MelonLoader 生成程序集"
}
Get-ChildItem $il2cppAsm -Filter *.dll | ForEach-Object { $lines += ('-r:"' + $_.FullName + '"') }

$rsp = Join-Path $work 'refs.rsp'
Set-Content -Path $rsp -Value $lines -Encoding UTF8

Write-Host '[3/4] 编译'
$csc = Join-Path (Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName 'Roslyn\bincore\csc.dll'
$sources = Get-ChildItem $work -Filter *.cs | ForEach-Object { $_.FullName }
$args = @('exec', $csc, "@$rsp") + $sources
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

Write-Host '[4/4] 完成'
Get-Item (Join-Path $outDir 'MaydaySave.dll') | Select-Object Name, Length, LastWriteTime | Format-List
