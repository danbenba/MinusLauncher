 @echo off
title FortniteDownloader - Build BCD
dotnet clean
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
echo.
echo Build Finished.
echo.
pause
