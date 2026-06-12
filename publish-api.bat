@echo off
echo ===================================================
echo Publishing LIMS AJT NK API (Release Mode)...
echo ===================================================

REM Define output directory
set PUBLISH_DIR=.\publish

REM Clean old publish directory if exists
if exist %PUBLISH_DIR% (
    echo Cleaning previous publish directory...
    rmdir /s /q %PUBLISH_DIR%
)

REM Run dotnet publish
echo Running dotnet publish...
dotnet publish LIMS_AJT_NK_API.csproj -c Release -o %PUBLISH_DIR%

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Publish failed! Please check the errors above.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo [SUCCESS] API published successfully!
echo Path: %CD%\publish
echo ===================================================
pause
