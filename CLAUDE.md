# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## DA Best Practices Reference

**Always read `D:\Projects\Appbundles\AutomationApps\aps-da\` before writing any DA code.**

That project (`APS-Common.ps1`, `Setup-DA.ps1`, `Test-DA.ps1`) is the authoritative reference for:
- Correct bundle zip structure (`Compress-Archive -Path $BundleDir` — archives the **folder itself**, not its contents. ZipDirectory/zip-contents breaks DA bundle lookup)
- AppBundle upload flow (register → upload zip to AWS S3 via multipart form → set alias)
- Activity definition shape (commandLine, settings.script, parameters with verb/localName)
- WorkItem submission and polling pattern
- OSS signed URL flow (3-step S3 for uploads; `/signed?access=read|write` for workitem args)
- DA engine strings: `Autodesk.AutoCAD+26_0` for AutoCAD 2026 (NOT `+27_0`)
- PackageContents.xml: `SeriesMin="R26.0" SeriesMax="R26.0"` (not `R26`)

Skipping this reference causes avoidable bugs. Read it first.

---

## Project Overview

Demo: import markup geometries drawn on APS Viewer back into an AutoCAD 2D drawing, then export to PDF via Design Automation. Two-project solution:

- **SimpleViewer** — ASP.NET Core 10 web app (APS backend + static JS frontend)
- **TransformPoint** — AutoCAD plugin (`net10.0-windows`, `AutoCAD.NET.Core 26.*`), runs interactively (`DRAWMARKUP`) or headless in DA (`IMPORTMARKUPS`)

## Build Commands

```powershell
# Start web app with hot-reload (preferred)
server.bat

# Build TransformPoint plugin + produce DA bundle zip
build-plugin.bat
# Output: TransformPoint\bin\Release\net10.0-windows\TransformPoint.bundle.zip

# Web app only
dotnet build SimpleViewer/SimpleViewer.csproj

# Run web app (no hot-reload)
dotnet run --project SimpleViewer/SimpleViewer.csproj --environment Development
```

## Required Configuration

`appsettings.Development.json` (already present — do not commit to public repos):
```
APS_CLIENT_ID
APS_CLIENT_SECRET
APS_BUCKET
```

## Architecture

### Full Data Flow

```
1. Upload DWG → OSS bucket
2. Translate → Model Derivative (SVF, 2dviews:pdf pipeline)
3. View in APS Viewer → draw rectangle markups
4. Select markup → EVENT_MARKUP_SELECTED → pageToModelTransform → corners in WCS
5. "Generate PDF" button →
   POST /api/da/workitems { urn, markups[] }
     → upload markups.json to OSS
     → DA workitem 1: ImportMarkups
         accoreconsole /i input.dwg /al TransformPoint.bundle /s script
         IMPORTMARKUPS reads markups.json → draws polylines in model space → saves output.dwg
         DA PUTs output.dwg → OSS bucket (signed write URL)
     → DA workitem 2: PlotToPDF (script-only, no bundle)
         accoreconsole /i output.dwg /s script /suppressGraphics
         _tilemode 0 -export _pdf _all result.pdf
         DA PUTs result.pdf → OSS bucket (signed write URL)
     → return signed read URL for result.pdf
6. Browser downloads PDF
```

### SimpleViewer Backend

- `Models/APS.cs` — partial class, holds credentials, `_bucket`
- `Models/APS.Auth.cs` — 2-legged OAuth, token caching (public + internal + DA scopes)
- `Models/APS.Oss.cs` — bucket ensure, upload, list objects
- `Models/APS.Deriv.cs` — translation with `2dviews:pdf`; `JobDwgPdfOutputPayloadAdvanced` custom class (Forge SDK lacks this natively); `?pdf=false` toggle for SVF-only mode
- `Models/APS.DA.cs` — DA service: bundle deploy, activity ensure, workitem submit/poll, signed URLs
- `Controllers/AuthController.cs` — `GET /api/auth/token`
- `Controllers/ModelsController.cs` — `GET/POST /api/models`, translation status; `?pdf=false` for SVF-only translation
- `Controllers/DesignAutomationController.cs` — `POST /api/da/setup`, `POST /api/da/workitems`, `GET /api/da/workitems/{id}`, `DELETE /api/da/cleanup`

### Frontend

- `wwwroot/viewer.js` — `initViewer`, `loadModel`, `MarkupSelector` extension; exports `getMarkups()` / `clearMarkups()`
- `wwwroot/main.js` — model list/upload, DA button wiring, workitem polling
- `wwwroot/index.html` — header with Upload + Generate PDF buttons
- `wwwroot/admin.html` — one-time DA setup page (bundle upload + activity registration + cleanup); **not for end users**

### TransformPoint Plugin

- `EntryCommand.cs`:
  - `DRAWMARKUP` — interactive, picks entity in paper space, transforms PSDCS→DCS→WCS to model space
  - `IMPORTMARKUPS` — headless DA command, reads `markups.json` from working dir, draws red polylines at WCS corners, `SaveAs output.dwg`
- `PackageContents.xml` — DA bundle manifest
- Post-build target uses `Compress-Archive -Path $BundleDir` (PowerShell) to create `TransformPoint.bundle.zip` with the bundle folder at zip root — required by DA

## DA Setup (one-time)

1. `build-plugin.bat` → produces `TransformPoint.bundle.zip`
2. Go to `http://localhost:8080/admin.html`
3. Upload zip → deploys bundle + registers `ImportMarkups` + `PlotToPDF` activities
4. To reset: "Delete all DA resources" button → calls `DELETE /forgeapps/me`

## Key Design Decisions

**`2dviews: pdf` translation**: Switches DWG Extractor from legacy F2D to modern PDF pipeline. Stable `pageToModelTransform` — critical for correct markup coordinate export. Customer-facing fix for coordinate shift bugs.

**Markup coords in WCS**: `pageToModelTransform` applied in the browser before sending to DA. `IMPORTMARKUPS` just draws at the provided coordinates — no matrix math in the plugin.

**Two DA activities**: `ImportMarkups` (custom bundle) → `PlotToPDF` (script-only, no bundle). Chained automatically by the backend poll loop using `ConcurrentDictionary` with atomic `TryUpdate`.

**Corner labeling**: After `pageToModelTransform`, Y increases upward (AutoCAD WCS). `minY` = bottom, `maxY` = top. Old code had this inverted.

**`EVENT_MARKUP_SELECTED` placement**: Must be registered directly in `load()`, not nested inside `EVENT_EDITMODE_CHANGED`. Nesting means it only fires after a tool switch, not on initial tool selection.

## Known Issues / Pending Work

- `IMPORTMARKUPS` command not recognized in DA accoreconsole — under active investigation. Suspect DLL load failure; check full workitem report for details.
- APS SDK upgrade (`Autodesk.Forge 1.9.9` → `Autodesk.Authentication` + `Autodesk.OSS` + `Autodesk.ModelDerivative`) — not yet done.
- Step-by-step workflow UI with OSS bucket panel (see output.dwg before triggering PDF) — planned.
- Markup types beyond rectangle not yet implemented.

## NuGet Dependencies

- `Autodesk.Forge 1.9.9` — legacy SDK, pending upgrade
- `AutoCAD.NET.Core 26.*` — TransformPoint plugin, `ExcludeAssets=runtime` (DA engine provides DLLs)
