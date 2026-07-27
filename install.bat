@echo off
setlocal EnableDelayedExpansion

set BINARY_NAME=ce
set INSTALL_DIR=%LOCALAPPDATA%\Programs\code-edit

:: When run from an extracted release zip the pre-built binary sits alongside
:: this script. When run from the repo root, build it first.
if exist "%~dp0CodeEdit.Presentation.exe" (
    set SOURCE_EXE=%~dp0CodeEdit.Presentation.exe
    goto :install
)

set PROJECT=%~dp0src\CodeEdit.Presentation
set PUBLISH_DIR=%~dp0publish\win-x64

echo Building %BINARY_NAME%...
dotnet publish "%PROJECT%" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:DebugType=none ^
  --output "%PUBLISH_DIR%"

if errorlevel 1 (
    echo dotnet publish failed.
    exit /b 1
)
set SOURCE_EXE=%PUBLISH_DIR%\CodeEdit.Presentation.exe

:install
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /y "%SOURCE_EXE%" "%INSTALL_DIR%\%BINARY_NAME%.exe"

:: Add INSTALL_DIR to user PATH if not already present
set "KEY=HKCU\Environment"
for /f "tokens=2*" %%A in ('reg query "%KEY%" /v Path 2^>nul') do set "CURRENT_PATH=%%B"

echo !CURRENT_PATH! | findstr /i /c:"%INSTALL_DIR%" >nul 2>&1
if errorlevel 1 (
    if defined CURRENT_PATH (
        reg add "%KEY%" /v Path /t REG_EXPAND_SZ /d "!CURRENT_PATH!;%INSTALL_DIR%" /f >nul
    ) else (
        reg add "%KEY%" /v Path /t REG_EXPAND_SZ /d "%INSTALL_DIR%" /f >nul
    )
    echo Added to user PATH: %INSTALL_DIR%
    echo Restart your terminal for PATH changes to take effect.
)

echo Installed: %INSTALL_DIR%\%BINARY_NAME%.exe
endlocal
