@echo off
setlocal
pushd "%~dp0"
set "compiler=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" (
    echo The 64-bit .NET Framework compiler was not found.
    exit /b 1
)
if not exist "dist" mkdir "dist"
set "kind=winexe"
if /i "%~1"=="console" set "kind=exe"
"%compiler%" /nologo /optimize+ /platform:x64 /target:%kind% /out:dist\Windows-PinchZoom.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll PinchZoom.cs StandardInput.cs Screenshot.cs
set "result=%errorlevel%"
popd
exit /b %result%
