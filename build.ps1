param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,                      # 游戏根目录，例如 "C:\SteamLibrary\steamapps\common\Mayday Airways Demo"
    [string]$OutDir = (Join-Path $PSScriptRoot "dist"),
    [string]$PluginName = "MaydaySave.dll"
)

# 编译 MaydaySave.dll
# 依赖：.NET SDK（csc 位于 sdk\10.0.100\Roslyn\bincore\csc.dll，可按需修改 $sdk）
#      + 游戏目录下 BepInEx 生成的 interop 程序集（首次运行游戏后才会出现）

$ErrorActionPreference = "Stop"

$interop = Join-Path $GameDir 'BepInEx\interop'
$core    = Join-Path $GameDir 'BepInEx\core'
$runtime = Join-Path $GameDir 'dotnet'
$src     = Join-Path $PSScriptRoot 'src\MaydaySave'
$sdk     = 'C:\Program Files\dotnet\sdk\10.0.100'
$csc     = Join-Path $sdk 'Roslyn\bincore\csc.dll'

foreach ($p in @($csc, $interop, $core, $runtime, $src)) {
    if (-not (Test-Path $p)) { throw "not found: $p" }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# CoreCLR 引用程序集（排除原生库）
$nativeNames = '^(coreclr|clrjit|clretwrc|clrgc|mscordaccore|mscordbi|mscorrc|dbgshim|createdump|hostfxr|hostpolicy|msquic|System\.IO\.Compression\.Native|Microsoft\.DiaSymReader\.Native)'
$runtimeRefs = Get-ChildItem $runtime -Filter *.dll | Where-Object { $_.Name -notmatch $nativeNames }

$refs = New-Object System.Collections.Generic.List[string]
foreach ($d in $runtimeRefs) { $refs.Add($d.FullName) }

$extra = @(
    (Join-Path $core 'BepInEx.Core.dll'),
    (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'),
    (Join-Path $core 'BepInEx.Unity.Common.dll'),
    (Join-Path $core 'BepInEx.Preloader.Core.dll'),
    (Join-Path $core 'Il2CppInterop.Runtime.dll'),
    (Join-Path $core 'Il2CppInterop.Common.dll'),
    (Join-Path $core '0Harmony.dll'),
    (Join-Path $interop 'Assembly-CSharp.dll'),
    (Join-Path $interop 'Assembly-CSharp-firstpass.dll'),
    (Join-Path $interop 'Il2Cppmscorlib.dll'),
    (Join-Path $interop 'UnityEngine.dll'),
    (Join-Path $interop 'UnityEngine.CoreModule.dll'),
    (Join-Path $interop 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $interop 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $interop 'Il2CppSystem.dll'),
    (Join-Path $interop 'Il2CppSystem.Core.dll'),
    (Join-Path $interop 'Mirror.dll')
)
foreach ($e in $extra) {
    if (Test-Path $e) { $refs.Add($e) } else { Write-Host "missing ref: $e" }
}

$rsp = Join-Path $env:TEMP 'maydaysave_refs.rsp'
$lines = @(
    '-nologo', '-target:library', '-nostdlib+', '-langversion:latest', '-unsafe+',
    '-nowarn:CS0169,CS0649,CS0414,CS8632',
    "-out:`"$(Join-Path $OutDir $PluginName)`""
)
$lines += ($refs | ForEach-Object { "-r:`"$_`"" })
Set-Content -Path $rsp -Value $lines -Encoding UTF8

$sources = Get-ChildItem $src -Filter *.cs -Recurse | ForEach-Object { "`"$($_.FullName)`"" }
Write-Host "compiling $($sources.Count) files with $($refs.Count) refs"
& dotnet exec $csc "@$rsp" @sources
if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }
Write-Host "built: $(Join-Path $OutDir $PluginName)"
