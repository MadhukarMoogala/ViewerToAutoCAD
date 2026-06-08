@echo off
echo Building TransformPoint AutoCAD plugin...
dotnet build TransformPoint\TransformPoint.csproj -c Release
echo.
echo Bundle zip:
echo   TransformPoint\bin\Release\net10.0-windows\TransformPoint.bundle.zip
echo.
echo Upload this zip at http://localhost:8080/admin.html to deploy to Design Automation.
pause
