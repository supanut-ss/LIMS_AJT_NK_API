@echo off
setlocal

REM Installs / updates the LIMS AJT NK OCR Worker Windows Service.
REM Run as Administrator from the published worker folder (next to the worker exe).

set SERVICE_NAME=LimsAjtNkOcrWorker
set DISPLAY_NAME=LIMS AJT NK OCR Worker
set TARGET_DIR=C:\Services\LIMS-OCR-Worker
set SOURCE_DIR=%~dp0

net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Please run this script as Administrator.
    pause
    exit /b 1
)

if not exist "%SOURCE_DIR%\LIMS_AJT_NK_CallbackWorker.exe" (
    echo [ERROR] Worker files not found next to install.bat
    pause
    exit /b 1
)

REM Stop existing service before copying files (remember if it was running)
set WAS_RUNNING=0
sc.exe query %SERVICE_NAME% >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    sc.exe query %SERVICE_NAME% | find "RUNNING" >nul 2>&1
    if not errorlevel 1 set WAS_RUNNING=1
    echo Stopping existing service...
    sc.exe stop %SERVICE_NAME% >nul 2>&1
    timeout /t 5 /nobreak >nul
)

REM Copy files but keep the existing environment config
echo Copying worker files to %TARGET_DIR% ...
robocopy "%SOURCE_DIR:~0,-1%" "%TARGET_DIR%" /E /XF appsettings.json appsettings.Production.json install.bat /NFL /NDL /NJH /NJS
if %ERRORLEVEL% GEQ 8 (
    echo [ERROR] Copy failed.
    pause
    exit /b 1
)
if not exist "%TARGET_DIR%\appsettings.json" copy "%SOURCE_DIR%\appsettings.json" "%TARGET_DIR%\appsettings.json" >nul

REM Create the service if it does not exist yet
sc.exe query %SERVICE_NAME% >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Creating service %SERVICE_NAME% ...
    REM Register only: start type is Manual and the service is NOT started.
    sc.exe create %SERVICE_NAME% binPath= "\"%TARGET_DIR%\LIMS_AJT_NK_CallbackWorker.exe\"" start= demand DisplayName= "%DISPLAY_NAME%"
    sc.exe failure %SERVICE_NAME% reset= 86400 actions= restart/60000/restart/60000/restart/60000
    echo.
    echo Service registered but NOT started.
    echo NOTE: Set the Log On account in services.msc to a domain service account
    echo       that can access the UNC path. When ready, start it with:
    echo       sc.exe start %SERVICE_NAME%
    echo       and set the start type to Automatic if needed:
    echo       sc.exe config %SERVICE_NAME% start= auto
) else (
    REM Existing service: only restart it if it was running before the update.
    if "%WAS_RUNNING%"=="1" (
        echo Restarting service...
        sc.exe start %SERVICE_NAME%
    ) else (
        echo Service was not running; leaving it stopped.
    )
)

echo.
pause
