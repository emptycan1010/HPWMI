@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ^
  /out:"%~dp0HpThermalMode.exe" /win32manifest:"%~dp0app.manifest" ^
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Management.dll ^
  "%~dp0HpThermalMode.cs"
if errorlevel 1 exit /b 1
echo Built: %~dp0HpThermalMode.exe
