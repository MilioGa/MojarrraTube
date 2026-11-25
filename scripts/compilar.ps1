# Compila MojarraTube y su instalador en Windows (necesita .NET 8 SDK y NSIS: winget install Microsoft.DotNet.SDK.8 NSIS.NSIS)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$version = ([xml](Get-Content src\MojarraTube.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (Test-Path dist\app) { Remove-Item dist\app -Recurse -Force }
dotnet publish src\MojarraTube.csproj -c Release -o dist\app
$makensis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $makensis) { throw 'No encuentro NSIS (winget install NSIS.NSIS)' }
& $makensis -V2 "-DVERSION=$version" "-DSRC=$PWD\dist\app" "-DOUTFILE=$PWD\dist\MojarraTube-Setup-$version.exe" installer\MojarraTube.nsi
Get-Item "dist\MojarraTube-Setup-$version.exe"
