@echo off
chcp 65001 >nul
title IntoTheVoid 私服 - Hosts 兜底配置 (请以管理员身份运行)
echo ==========================================
echo   Hosts 兜底配置（可选）
echo   说明：新版登录链不依赖 hosts；仅在 TapTap SDK 登录域异常时用
echo   请以管理员身份运行本脚本！
echo ==========================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 需要管理员权限！
    echo 请右键本文件 - 以管理员身份运行
    pause
    exit /b 1
)

set "HOSTS=C:\Windows\System32\drivers\etc\hosts"
echo. >> "%HOSTS%"
echo # === IntoTheVoid Private Server (optional fallback) === >> "%HOSTS%"
for %%d in (
    cweb.jinzhangshu.com
    tapcweb.jinzhangshu.com
    query.jinzhangshu.com
    official.jinzhangshu.com
    tap.jinzhangshu.com
    pre.query.jinzhangshu.com
    pre.web.jinzhangshu.com
    package.jinzhangshu.com
    accounts.tapapis.cn
    accounts.tapapis.com
    tapsdk.tapapis.cn
    tapsdk.tapapis.com
    open.tapapis.cn
    open.tapapis.com
    www.taptap.com
    tapdb.cn
    api.tapdb.cn
    www.tapdb.cn
    accounts.taptap.cn
    tapsdk.taptap.cn
    www.taptap.cn
    tds-moment.taptap.cn
    dispatch.taptap.cn
) do (
    findstr /C:"127.0.0.1 %%d" "%HOSTS%" >nul 2>&1
    if errorlevel 1 >> "%HOSTS%" echo 127.0.0.1 %%d
)
echo   [OK] Hosts 已追加本地回环条目
echo.
echo 提示：如后续不再需要，删除 hosts 中 "IntoTheVoid Private Server" 标记段即可。
pause
