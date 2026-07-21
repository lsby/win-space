@echo off
echo ==========================================
echo       WinSpace Build Script
echo ==========================================

echo [1/3] Stopping existing instances...
taskkill /IM win-space.exe /F >NUL 2>NUL

echo [2/3] Building project...
dotnet build -c Debug

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Build failed. Please check the logs above.
    pause
    exit /b %errorlevel%
)

echo.
echo SUCCESS: Build passed!
echo [3/3] Starting application...
echo.

start "" "bin\Debug\net9.0-windows\win-space.exe"
