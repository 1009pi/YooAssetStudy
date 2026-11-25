@echo off
title CDNServer
chcp 65001 >nul
cd /d "%~dp0"
node server.js
echo.
echo Server exited. Press any key to close this window . . .
pause >nul
