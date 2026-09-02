@echo off
chcp 65001 >nul
title IntoTheVoid 私服 - 一键安装 (建议以管理员身份运行)
echo ==========================================
echo   IntoTheVoid 私服一键安装
echo   建议以管理员身份运行本脚本！
echo ==========================================
echo.

:: 检查管理员权限（可选，仅为兼容旧逻辑；新版免改 hosts 不再强依赖）
net session >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] 已获得管理员权限
) else (
    echo [提示] 非管理员运行。新版免改 hosts/免装证书，通常无需管理员权限。
    echo        若后续需要写 hosts（兜底），请右键 - 以管理员身份运行。
)
echo.

:: ========== 1. 定位路径（无硬编码，自动探测）==========
:: 本文件位于 <发行包>\docs\，各组件同级：
::   ..\Server\          服务端发布版
::   ..\Client-Plugin\   客户端插件包
set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..") do set "PKG_ROOT=%%~fi"

set "SERVER_DIR=%PKG_ROOT%\Server"
if not exist "%SERVER_DIR%\IntoTheVoidServer.exe" (
    echo [错误] 未找到服务端：%SERVER_DIR%
    pause
    exit /b 1
)
echo [OK] 服务端目录：%SERVER_DIR%

:: 游戏根目录：优先环境变量 UCS_GAME_ROOT，其次在常见位置探测 IntoTheVoid.exe
set "GAME_ROOT="
if defined UCS_GAME_ROOT (
    if exist "%UCS_GAME_ROOT%\IntoTheVoid.exe" set "GAME_ROOT=%UCS_GAME_ROOT%"
)
if "%GAME_ROOT%"=="" for %%i in ("%PKG_ROOT%\..") do (
    if exist "%%~fi\IntoTheVoid.exe" set "GAME_ROOT=%%~fi"
)
if "%GAME_ROOT%"=="" for %%i in ("%PKG_ROOT%\..\..") do (
    if exist "%%~fi\IntoTheVoid.exe" set "GAME_ROOT=%%~fi"
)
if "%GAME_ROOT%"=="" (
    echo [提示] 未自动找到游戏根目录（未检测到 IntoTheVoid.exe）
    echo         请把发行包放到游戏主程序同级/上一级目录，或设置环境变量 UCS_GAME_ROOT
    echo         指向游戏根目录后重试。本脚本将只启动服务端。
    echo.
) else (
    echo [OK] 已找到游戏根目录：%GAME_ROOT%
)
echo.

:: ========== 2. 合并客户端插件到游戏根目录（如已找到）==========
if not "%GAME_ROOT%"=="" (
    echo [1/3] 合并客户端插件到游戏目录...
    if exist "%GAME_ROOT%\BepInEx\plugins\UseCustomServer.dll" (
        echo       检测到已安装插件，先核对版本...
    )
    xcopy /E /I /Y "%PKG_ROOT%\Client-Plugin\*" "%GAME_ROOT%\" >nul
    echo   [OK] 插件文件已合并到 %GAME_ROOT%
    echo.
) else (
    echo [1/3] 跳过插件合并（未定位游戏目录）。请手动把 Client-Plugin\ 下所有文件
    echo       拷贝到游戏根目录（与 IntoTheVoid.exe 同级）。
    echo.
)

:: ========== 3. 启动私服服务器 ==========
echo [2/3] 启动私服服务器...
taskkill /IM IntoTheVoidServer.exe /F >nul 2>&1
start "IntoTheVoid Private Server" /D "%SERVER_DIR%" "%SERVER_DIR%\IntoTheVoidServer.exe"
echo   [OK] 服务器已启动
echo.

echo [3/3] 可选：配置 hosts 兜底（仅 SDK 登录域；新版基本不需要）
echo       （如遇到 SDK 相关登录异常，可另行运行 update_hosts.bat）
echo.

echo ==========================================
echo   安装完成！
echo ==========================================
echo.
echo 服务器地址：
echo   HTTP:  http://127.0.0.1:8183
echo   HTTPS: https://cweb.jinzhangshu.com  (端口443)
echo   TCP:   127.0.0.1:30531
echo.
echo 管理面板（GM工具）：
echo   http://127.0.0.1:8183/admin/
echo.
echo 日志文件：%SERVER_DIR%\logs\server_YYYYMMDD.log
echo.
echo 提示：现在可以启动游戏客户端 IntoTheVoid.exe，输入任意账号密码即可登录。
echo.
pause
