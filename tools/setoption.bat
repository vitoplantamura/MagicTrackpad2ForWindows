@echo off
setlocal EnableExtensions
title Magic Trackpad 2 - set driver option

set "SCRIPT=%~nx0"
set "OPTION=%~1"
set "VALUE=%~2"

if "%OPTION%"=="" goto usage
if "%VALUE%"=="" goto usage

rem Writing to HKLM requires an elevated prompt.
net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: This script must be run from an elevated ^(Administrator^) Command Prompt.
    echo        Right-click "Command Prompt" and choose "Run as administrator".
    echo.
    exit /b 1
)

echo.
echo Setting %OPTION% = %VALUE% for both drivers...

call :setkey "HKLM\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters" "Bluetooth filter (AmtPtpHidFilter)"
call :setkey "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters" "USB driver (AmtPtpDeviceUsbUm)"

echo.
echo Done. Reconnect the trackpad (or restart the driver) for the change to take effect.
exit /b 0

:setkey
reg add "%~1" /v "%OPTION%" /t REG_DWORD /d "%VALUE%" /f >nul
if errorlevel 1 (
    echo   [FAILED] %~2
) else (
    echo   [OK]     %~2
)
exit /b 0

:usage
echo.
echo Usage: %SCRIPT% ^<OptionName^> ^<Value^>
echo.
echo Sets a DWORD option in the "Parameters" registry key of both drivers:
echo   * AmtPtpHidFilter   - Bluetooth Magic Trackpad 2
echo   * AmtPtpDeviceUsbUm - USB Magic Trackpad 2
echo.
echo Boolean flags (set to 1 to enable, 0 to disable):
echo   InvertMovement        Invert up/down and left/right (upside-down pad)
echo   AllowNonAdminAccess   Let non-admin apps open the haptic pipe/device
echo   ButtonDisabled        Disable the physical click button
echo   IgnoreButtonFinger    Ignore the finger that is clicking
echo   IgnoreNearFingers     Ignore fingers hovering just above the pad
echo   PalmRejection         Reject palm touches
echo.
echo Other numeric DWORD options:
echo   FeedbackClick         e.g. 0x060617 (medium click)
echo   FeedbackRelease       e.g. 0x020218
echo   StopPressure          e.g. 0xffffffff to disable
echo   StopSize              e.g. 0xffffffff to disable
echo.
echo Examples:
echo   %SCRIPT% InvertMovement 1
echo   %SCRIPT% AllowNonAdminAccess 1
echo   %SCRIPT% FeedbackClick 0x040415
echo.
exit /b 1
