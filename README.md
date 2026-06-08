# ViewerToAutoCAD — APS Viewer Markups → AutoCAD DWG → PDF

Import rectangle markups drawn on an APS Viewer 2D sheet back into the source AutoCAD drawing, then export to PDF — all automated via APS Design Automation.

## Architecture

```
Browser (APS Viewer)
  └─ Draw rectangle markup
  └─ Select markup → pageToModelTransform → WCS corners
  └─ "Generate PDF" →

ASP.NET Core backend (SimpleViewer)
  ├─ POST /api/da/workitems
  │    ├─ Upload markups.json → OSS
  │    ├─ DA workitem 1: ImportMarkups
  │    │    accoreconsole /i input.dwg /al TransformPoint.bundle /s script
  │    │    IMPORTMARKUPS command → draws red polylines → saves output.dwg
  │    └─ DA workitem 2: PlotToPDF (script-only)
  │         _tilemode 0  -export _pdf _all result.pdf
  └─ Return signed URL → browser downloads PDF

AutoCAD Plugin (TransformPoint)
  ├─ DRAWMARKUP — interactive: picks entity, transforms PSDCS→DCS→WCS
  └─ IMPORTMARKUPS — headless DA: reads markups.json, draws polylines, SaveAs output.dwg
```

## Projects

| Project | Description |
|---------|-------------|
| `SimpleViewer` | ASP.NET Core 10 web app — APS backend + static JS frontend |
| `TransformPoint` | AutoCAD plugin (`net10.0-windows`) — runs interactively or headless in DA |

## Prerequisites

- .NET 10 SDK
- AutoCAD 2026 (for interactive `DRAWMARKUP` command)
- APS app credentials with `data:read data:write data:create bucket:create bucket:read code:all`

## Configuration

Copy `appsettings.Development.json.example` (or create manually):

```json
{
  "APS_CLIENT_ID": "your-client-id",
  "APS_CLIENT_SECRET": "your-client-secret",
  "APS_BUCKET": "your-bucket-key"
}
```

> `appsettings.Development.json` is git-ignored — never commit credentials.

## Quick Start

```powershell
# 1. Build the AutoCAD plugin + DA bundle zip
build-plugin.bat
# Output: TransformPoint\bin\Release\net10.0-windows\TransformPoint.bundle.zip

# 2. Start the web server (hot-reload)
server.bat
# Opens http://localhost:8080
```

## DA One-Time Setup

After first run, open `http://localhost:8080/admin.html`:

1. **Upload Bundle** — uploads `TransformPoint.bundle.zip`, registers `ImportMarkups` + `PlotToPDF` activities
2. **Delete All DA Resources** — resets everything (use when redeploying a changed plugin)

> Admin page is for developers only. End users see only the main UI.

## End-User Workflow

1. Open `http://localhost:8080`
2. Upload a DWG file (or select existing)
3. Wait for translation (Model Derivative → SVF + 2dviews:pdf pipeline)
4. In the viewer, enable Markups → draw rectangle(s) on the 2D sheet
5. Select a markup → click **Generate PDF**
6. Wait ~30–60 s — browser downloads the output PDF with markups drawn in model space

## Why `2dviews: pdf`?

The `advanced: { "2dviews": "pdf" }` translation option switches DWG Extractor from the legacy F2D pipeline to the modern PDF pipeline. This produces a stable `pageToModelTransform` matrix — critical for correct coordinate mapping. Without it, path-based markups (freehand, polyline) can shift due to viewport transform changes in newer F2D engine versions.

## Key Design Decisions

- **Coords in WCS before sending**: `pageToModelTransform` applied in the browser. The DA plugin just draws at the provided coordinates — no matrix math in the plugin.
- **Two chained DA activities**: `ImportMarkups` (custom bundle) → `PlotToPDF` (script-only). Backend polls wi1, auto-submits wi2 on success using `ConcurrentDictionary.TryUpdate` to prevent duplicate submission.
- **Corner order**: After `pageToModelTransform`, Y increases upward (AutoCAD WCS). `minY` = bottom, `maxY` = top.

## Console Logging

When running `server.bat` you'll see DA progress in the console:

```
[DA] Setup started — bundle size: 123456 bytes
[DA] Deploying TransformPoint bundle...
[DA] Bundle deployed — v3 alias=dev
[DA] ImportMarkups activity ready
[DA] PlotToPDF activity ready
[DA] Setup complete
[DA] Submitting ImportMarkups workitem for URN=dXJuOmFkc2sub…
[DA] ImportMarkups workitem submitted: abc123 → output=output_20260608120000.dwg
[DA] Workitem abc123 status: inprogress
[DA] Workitem abc123 status: success
[DA] ImportMarkups succeeded — submitting PlotToPDF
[DA] PlotToPDF workitem submitted: def456 → output=output_20260608120000.pdf
[DA] PlotToPDF succeeded — PDF ready: output_20260608120000.pdf
```

## Known Issues

- `IMPORTMARKUPS` command not recognized in DA accoreconsole — under active investigation (possible DLL load failure; check workitem report URL).
- APS SDK upgrade (`Autodesk.Forge 1.9.9` → `Autodesk.Authentication` + `Autodesk.OSS` + `Autodesk.ModelDerivative`) — pending.

## License

[MIT](http://opensource.org/licenses/MIT)

## Written by

Madhukar Moogala [@galakar](https://twitter.com/galakar), [APS Partner Development](http://aps.autodesk.com)
