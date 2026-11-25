@echo off
rem Builds the Release version of the GUI and the CLI into Setup\publish (input of Mpbkpu.iss)
setlocal
set "ROOT=%~dp0.."
set "OUT=%~dp0publish"
set "RID=win-x64"

if exist "%OUT%" rmdir /s /q "%OUT%"

echo Publishing GUI...
dotnet publish "%ROOT%\MpbkpuGUI\MpbkpuGUI.csproj" -c Release -r %RID% --self-contained true -o "%OUT%" || goto :error

echo Publishing CLI...
dotnet publish "%ROOT%\Mpbkpu\Mpbkpu.csproj" -c Release -r %RID% --self-contained true -o "%OUT%" || goto :error

echo.
echo Build completed: %OUT%
exit /b 0

:error
echo.
echo Build FAILED
exit /b 1
