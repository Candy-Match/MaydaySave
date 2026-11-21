@echo off
chcp 65001 >nul
title MaydaySave - 安装到模组管理器
echo.
echo 如果你已经装了 MelonLoader 模组管理器，双击本文件即可：
echo   - 把和 MelonLoader 冲突的 BepInEx 文件收进 _旧版BepInEx_已停用\
echo   - 确认 MelonLoader 版存档模组已放到 Mods\MaydaySave\
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0安装.ps1"
echo.
pause
