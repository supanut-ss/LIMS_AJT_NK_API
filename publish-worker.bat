@echo off
echo ===================================================
echo Publishing LIMS AJT NK OCR Worker (Release Mode)...
echo ===================================================

set WORKER_DIR=.\publish\worker

if exist %WORKER_DIR% (
    echo Cleaning previous worker directory...
    rmdir /s /q %WORKER_DIR%
)

dotnet publish .\src\LIMS_AJT_NK_CallbackWorker\LIMS_AJT_NK_CallbackWorker.csproj -c Release -r win-x64 --self-contained false -o %WORKER_DIR%
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Publish failed! Please check the errors above.
    pause
    exit /b %ERRORLEVEL%
)

copy /y .\deploy\install.bat %WORKER_DIR%\install.bat >nul

echo.
echo ===================================================
echo [SUCCESS] Worker published! Run install.bat as Administrator.
echo Path: %CD%\publish\worker
echo ===================================================
pause
