@echo off
rem Builds the Release version of the GUI and the CLI into Setup\publish (input of Mpbkpu.iss)
setlocal
set "ROOT=%~dp0.."
set "OUT=%~dp0publish"
set "RID=win-x64"

if exist "%OUT%" rmdir /s /q "%OUT%"

rem A clean build: a stale intermediate assembly without the compiled XAML of Avalonia makes the GUI crash at startup
echo Cleaning...
rem The clean of a runtime needs its restore (a build without runtime replaces the assets file)
dotnet restore "%ROOT%\MpbkpuGUI\MpbkpuGUI.csproj" -r %RID% || goto :error
dotnet restore "%ROOT%\Mpbkpu\Mpbkpu.csproj" -r %RID% || goto :error
dotnet clean "%ROOT%\MpbkpuGUI\MpbkpuGUI.csproj" -c Release -r %RID% -v q || goto :error
dotnet clean "%ROOT%\Mpbkpu\Mpbkpu.csproj" -c Release -r %RID% -v q || goto :error

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
