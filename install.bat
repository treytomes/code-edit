@echo off
setlocal EnableDelayedExpansion

set BINARY_NAME=ce
set PROJECT=%~dp0src\CodeEdit.Presentation
set PUBLISH_DIR=%~dp0publish\win-x64
set INSTALL_DIR=%LOCALAPPDATA%\Programs\code-edit

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

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /y "%PUBLISH_DIR%\CodeEdit.Presentation.exe" "%INSTALL_DIR%\%BINARY_NAME%.exe"

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
