#!/usr/bin/env bash
# Compila MojarraTube y su instalador desde Linux o WSL (necesita .NET 8 SDK con soporte de Windows y NSIS)
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/MojarraTube.csproj)
rm -rf dist/app
dotnet publish src/MojarraTube.csproj -c Release -o dist/app
makensis -V2 -DVERSION="$VERSION" -DSRC="$PWD/dist/app" -DOUTFILE="$PWD/dist/MojarraTube-Setup-$VERSION.exe" installer/MojarraTube.nsi
ls -lh "dist/MojarraTube-Setup-$VERSION.exe"
