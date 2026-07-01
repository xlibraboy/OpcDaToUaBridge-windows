@echo off
set REPO_ROOT=%~dp0\..\..
set DOTNET_EXE=C:\Program Files (x86)\dotnet\dotnet.exe
pushd "%REPO_ROOT%\publish"
"%DOTNET_EXE%" "%REPO_ROOT%\publish\OpcBridge.App.dll" 1>> "%REPO_ROOT%\publish\bridge-task-stdout.log" 2>> "%REPO_ROOT%\publish\bridge-task-stderr.log"
popd
