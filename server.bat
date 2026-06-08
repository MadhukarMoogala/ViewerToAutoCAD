@echo off
echo Starting SimpleViewer with hot-reload...
echo Open http://localhost:8080
echo Admin / DA setup: http://localhost:8080/admin.html
echo.
dotnet watch --project SimpleViewer\SimpleViewer.csproj run --environment Development
