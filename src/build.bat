@echo off
rem Builds RGBUnify-Setup.exe one folder up (the installer, with the app embedded inside it).
rem Needs only the .NET Framework 4 compiler that ships with Windows.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set REFS=/r:System.Management.dll /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.ServiceProcess.dll /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll"
if not exist bin mkdir bin

"%CSC%" /nologo /target:exe /out:bin\IconGen.exe /r:System.Drawing.dll IconGen.cs AppIcon.cs Effects.cs OpenRgbClient.cs || exit /b 1
bin\IconGen.exe app.ico || exit /b 1
del bin\IconGen.exe

rem The app itself (embedded into the installer below).
"%CSC%" /nologo /target:winexe /optimize+ /out:bin\RGBUnify.exe /win32icon:app.ico /win32manifest:app.manifest %REFS% ^
  Program.cs MainForm.cs MainForm.Home.cs MainForm.Clean.cs MainForm.Settings.cs Theme.cs AppIcon.cs Engine.cs Effects.cs OpenRgbClient.cs Conflicts.cs Settings.cs VersionInfo.cs || exit /b 1

rem The installer / uninstaller.
"%CSC%" /nologo /target:winexe /optimize+ /out:..\RGBUnify-Setup.exe /win32icon:app.ico /win32manifest:app.manifest %REFS% ^
  /resource:bin\RGBUnify.exe,RGBUnify.exe /resource:License.txt,License.txt ^
  Setup.cs AppIcon.cs Engine.cs Effects.cs OpenRgbClient.cs Settings.cs VersionInfo.cs || exit /b 1

if exist ..\RGBUnify.exe del ..\RGBUnify.exe
echo Built ..\RGBUnify-Setup.exe
