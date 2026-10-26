@echo off
set "DOTNET_CLI_HOME=%~dp0..\external\dotnet-home"
set "APPDATA=%~dp0..\external\appdata"
set "LOCALAPPDATA=%~dp0..\external\appdata"
set "NUGET_PACKAGES=%~dp0..\external\dotnet-home\packages"
set "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=0"
set "DOTNET_NOLOGO=1"
dotnet build Probe\Probe.csproj --disable-build-servers -m:1 --nologo -v q
if errorlevel 1 exit /b 1
dotnet Probe\bin\Debug\net10.0\Probe.dll
