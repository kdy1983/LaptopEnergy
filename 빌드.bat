@echo off
chcp 65001 > nul
cd /d "%~dp0"

echo CPU Power Tray 빌드를 시작합니다.
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

if errorlevel 1 (
    echo.
    echo 빌드에 실패했습니다. .NET 8 SDK가 설치되어 있는지 확인하세요.
    pause
    exit /b 1
)

echo.
echo 빌드 완료:
echo bin\Release\net8.0-windows\win-x64\publish\CpuPowerTray.exe
pause
