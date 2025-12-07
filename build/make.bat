@echo off
setlocal

echo ------------------------------------------------------------------------------------------------
echo Please install Visual Studio 2022 following this guide (only Step 1, 2 and 3 are required):
echo   https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk
echo Also please make sure that the ".NET desktop development" workload is selected during install.
echo Then start this bat file from "x64 Native Tools Command Prompt for VS 2022".
echo ------------------------------------------------------------------------------------------------

set /p "inf2catPath=Please enter the full path to the inf2cat.exe file (defaults to C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x86\inf2cat.exe): "
if "%inf2catPath%"=="" set "inf2catPath=C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x86\inf2cat.exe"
if not exist "%inf2catPath%" exit /b 1

set /p "sha1TpCp=Please enter the SHA1 Thumbprint for signing the Control Panel exe: "
set /p "sha1TpDr=Please enter the SHA1 Thumbprint for signing the drivers: "

msbuild ..\AmtPtpDeviceUsbUm\MagicTrackpad2PtpDevice.vcxproj /p:Configuration=Release /p:Platform=ARM64
if %errorlevel% neq 0 exit /b %errorlevel%

msbuild ..\AmtPtpDeviceUsbUm\MagicTrackpad2PtpDevice.vcxproj /p:Configuration=Release /p:Platform=x64
if %errorlevel% neq 0 exit /b %errorlevel%

msbuild ..\AmtPtpHidFilter\AmtPtpHidFilter.vcxproj /p:Configuration=Release /p:Platform=ARM64
if %errorlevel% neq 0 exit /b %errorlevel%

msbuild ..\AmtPtpHidFilter\AmtPtpHidFilter.vcxproj /p:Configuration=Release /p:Platform=x64
if %errorlevel% neq 0 exit /b %errorlevel%

msbuild ..\AmtPtpControlPanel\AmtPtpControlPanel.csproj /p:Configuration=Release /p:Platform=AnyCPU
if %errorlevel% neq 0 exit /b %errorlevel%

rmdir result /S /Q
mkdir result
mkdir result\AMD64
mkdir result\ARM64
copy AmtPtpDevice_AMD64.inf result\AMD64\AmtPtpDevice.inf
copy AmtPtpDevice_ARM64.inf result\ARM64\AmtPtpDevice.inf
copy ..\AmtPtpControlPanel\bin\Release\AmtPtpControlPanel.exe result
copy ..\AmtPtpDeviceUsbUm\build\AmtPtpDeviceUsbUm\x64\Release\AmtPtpDeviceUsbUm.dll result\AMD64
copy ..\AmtPtpDeviceUsbUm\build\AmtPtpDeviceUsbUm\ARM64\Release\AmtPtpDeviceUsbUm.dll result\ARM64
copy ..\AmtPtpHidFilter\build\AmtPtpHidFilter\x64\Release\AmtPtpHidFilter.sys result\AMD64
copy ..\AmtPtpHidFilter\build\AmtPtpHidFilter\ARM64\Release\AmtPtpHidFilter.sys result\ARM64
mkdir result\pdb\AMD64
mkdir result\pdb\ARM64
copy ..\AmtPtpDeviceUsbUm\build\AmtPtpDeviceUsbUm\x64\Release\AmtPtpDeviceUsbUm.pdb result\pdb\AMD64
copy ..\AmtPtpDeviceUsbUm\build\AmtPtpDeviceUsbUm\ARM64\Release\AmtPtpDeviceUsbUm.pdb result\pdb\ARM64
copy ..\AmtPtpHidFilter\build\AmtPtpHidFilter\x64\Release\AmtPtpHidFilter.pdb result\pdb\AMD64
copy ..\AmtPtpHidFilter\build\AmtPtpHidFilter\ARM64\Release\AmtPtpHidFilter.pdb result\pdb\ARM64

signtool sign /v /fd sha256 /sha1 "%sha1TpCp%" /t http://timestamp.digicert.com result\AmtPtpControlPanel.exe
if %errorlevel% neq 0 exit /b %errorlevel%

signtool sign /v /fd sha256 /sha1 "%sha1TpDr%" /t http://timestamp.digicert.com result\AMD64\AmtPtpDeviceUsbUm.dll
if %errorlevel% neq 0 exit /b %errorlevel%

signtool sign /v /fd sha256 /sha1 "%sha1TpDr%" /t http://timestamp.digicert.com result\ARM64\AmtPtpDeviceUsbUm.dll
if %errorlevel% neq 0 exit /b %errorlevel%

signtool sign /v /fd sha256 /sha1 "%sha1TpDr%" /t http://timestamp.digicert.com result\AMD64\AmtPtpHidFilter.sys
if %errorlevel% neq 0 exit /b %errorlevel%

signtool sign /v /fd sha256 /sha1 "%sha1TpDr%" /t http://timestamp.digicert.com result\ARM64\AmtPtpHidFilter.sys
if %errorlevel% neq 0 exit /b %errorlevel%

"%inf2catPath%" /driver:result\AMD64 /os:10_X64
if %errorlevel% neq 0 exit /b %errorlevel%

"%inf2catPath%" /driver:result\ARM64 /os:10_RS3_ARM64
if %errorlevel% neq 0 exit /b %errorlevel%

signtool sign /v /fd sha256 /sha1 "%sha1TpDr%" /t http://timestamp.digicert.com result\AMD64\AmtPtpDevice.cat
if %errorlevel% neq 0 exit /b %errorlevel%

signtool sign /v /fd sha256 /sha1 "%sha1TpDr%" /t http://timestamp.digicert.com result\ARM64\AmtPtpDevice.cat
if %errorlevel% neq 0 exit /b %errorlevel%

echo ---------
echo Success!
echo ---------
exit /b 0

endlocal
