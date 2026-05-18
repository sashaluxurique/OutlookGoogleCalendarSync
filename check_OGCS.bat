@echo off
setlocal enabledelayedexpansion

set "TASK_NAME=OGCS_Watchdog"
set "PROCESS_NAME=OutlookGoogleCalendarSync.exe"
set "APP_PATH=%LOCALAPPDATA%\OutlookGoogleCalendarSync\OutlookGoogleCalendarSync.exe"
set "INSTALL_DIR=%LOCALAPPDATA%\OGCS_Watchdog"
set "INSTALL_PATH=%INSTALL_DIR%\check_OGCS.bat"
set "LAUNCHER_PATH=%INSTALL_DIR%\check_OGCS.vbs"
set "LOG_FILE=%INSTALL_DIR%\watchdog.log"
set "MAX_LOG_KB=512"

if /i "%~1"=="install"   goto install
if /i "%~1"=="uninstall" goto uninstall
goto check

:install
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /y "%~f0" "%INSTALL_PATH%" >nul
> "%LAUNCHER_PATH%" echo Set s = CreateObject("WScript.Shell")
>> "%LAUNCHER_PATH%" echo s.Run "cmd /c ""%INSTALL_PATH%""", 0, False
schtasks /create /tn "%TASK_NAME%" /tr "wscript.exe \"%LAUNCHER_PATH%\"" /sc minute /mo 5 /it /f
if errorlevel 1 (
    echo Install failed.
    exit /b 1
)
echo Installed. Task "%TASK_NAME%" runs every 5 min while logged on (hidden).
exit /b 0

:uninstall
schtasks /delete /tn "%TASK_NAME%" /f
echo Uninstalled. Script + log left at "%INSTALL_DIR%".
exit /b 0

:check
call :rotate_log
if not exist "%APP_PATH%" (
    call :log "ERROR: executable missing at %APP_PATH%"
    exit /b 1
)
tasklist /fi "IMAGENAME eq %PROCESS_NAME%" /nh | find /i "%PROCESS_NAME%" >nul
if not errorlevel 1 exit /b 0
call :log "OGCS not running. Launching."
start "" "%APP_PATH%"
exit /b 0

:log
echo [%DATE% %TIME%] %~1>> "%LOG_FILE%"
exit /b 0

:rotate_log
if not exist "%LOG_FILE%" exit /b 0
for %%A in ("%LOG_FILE%") do set /a "FILE_KB=%%~zA / 1024"
if !FILE_KB! gtr %MAX_LOG_KB% (
    move /y "%LOG_FILE%" "%LOG_FILE%.old" >nul
    echo [%DATE% %TIME%] Log rotated.>> "%LOG_FILE%"
)
exit /b 0
