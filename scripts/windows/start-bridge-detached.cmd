@echo off
cd /d C:\Users\xlibr\Documents\OpcDaToUaBridge\publish
REM If something is already listening on 8080, do nothing (prevents duplicate processes).
powershell -NoProfile -Command "if((Test-NetConnection -ComputerName 127.0.0.1 -Port 8080 -WarningAction SilentlyContinue).TcpTestSucceeded){exit 1}" >nul 2>&1
if errorlevel 1 goto :eof
start "" "C:\Program Files (x86)\dotnet\dotnet.exe" OpcBridge.App.dll
