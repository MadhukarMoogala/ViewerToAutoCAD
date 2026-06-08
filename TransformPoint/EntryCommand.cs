using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.IO;
using System.Text.Json;
using Viewport = Autodesk.AutoCAD.DatabaseServices.Viewport;

namespace TransformPoint
{
    // ── Markup data model (matches viewer.js JSON output) ──────────────────────
    public record MarkupPoint(double X, double Y);
    public record MarkupEntry(int Id, string Type, MarkupPoint[] Corners, bool Closed = true);
    public record MarkupCollection(MarkupEntry[] Markups);

    // ── Editor extensions for DCS/WCS transforms (interactive DRAWMARKUP) ─────
    public static class EditorExtension
    {
        public static Matrix3d DCS2WCS(this Editor ed)
        {
            if (ed == null) throw new ArgumentNullException(nameof(ed));
            bool tilemode = ed.Document.Database.TileMode;
            if (!tilemode) ed.SwitchToModelSpace();
            Matrix3d dcsToWcs;
            using (ViewTableRecord vtr = ed.GetCurrentView())
            {
                dcsToWcs =
                    Matrix3d.Rotation(-vtr.ViewTwist, vtr.ViewDirection, vtr.Target) *
                    Matrix3d.Displacement(vtr.Target - Point3d.Origin) *
                    Matrix3d.PlaneToWorld(vtr.ViewDirection);
            }
            if (!tilemode) ed.SwitchToPaperSpace();
            return dcsToWcs;
        }
    }

    public class EntryCommand
    {
        // ── Interactive: pick entity in paper space → copy to model space ───────
        [CommandMethod("DRAWMARKUP", CommandFlags.NoTileMode)]
        public void PointToModelSpace()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            Database db = HostApplicationServices.WorkingDatabase;
            PromptEntityResult res = ed.GetEntity("Pick markup entity in PS");
            if (res.Status != PromptStatus.OK) return;
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var ent = tr.GetObject(res.ObjectId, OpenMode.ForRead) as Entity;
            var copy = ent!.GetTransformedCopy(GetTransformationMatrix());
            copy.ColorIndex = 3;
            ms.AppendEntity(copy);
            tr.AddNewlyCreatedDBObject(copy, true);
            tr.Commit();
        }

        // ── Headless DA: read markups.json → draw polylines → save output.dwg ──
        [CommandMethod("IMPORTMARKUPS")]
        public void ImportMarkups()
        {
            var db = HostApplicationServices.WorkingDatabase;
            var workDir = Directory.GetCurrentDirectory();
            var jsonPath = Path.Combine(workDir, "markups.json");

            if (!File.Exists(jsonPath))
            {
                Application.DocumentManager.MdiActiveDocument?.Editor
                    .WriteMessage($"\nERROR: markups.json not found in {workDir}");
                return;
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<MarkupCollection>(File.ReadAllText(jsonPath), opts)!;

            // Ensure output opens in layout (paper space)
            db.TileMode = false;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Create or get APS_MARKUPS layer
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId layerId;
                if (lt.Has("APS_MARKUPS"))
                {
                    layerId = lt["APS_MARKUPS"];
                }
                else
                {
                    var ltr = new LayerTableRecord
                    {
                        Name       = "APS_MARKUPS",
                        Color      = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                         Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1), // Red
                        LineWeight = LineWeight.LineWeight050
                    };
                    layerId = lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }

                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (var markup in data.Markups)
                {
                    if (markup.Corners == null || markup.Corners.Length < 2) continue;
                    var pline = new Polyline();
                    pline.SetDatabaseDefaults();
                    pline.LayerId     = layerId;
                    pline.ColorIndex  = 256;         // ByLayer
                    pline.LineWeight  = LineWeight.ByLayer;
                    for (int i = 0; i < markup.Corners.Length; i++)
                        pline.AddVertexAt(i,
                            new Point2d(markup.Corners[i].X, markup.Corners[i].Y), 0, 0, 0);
                    pline.Closed = markup.Closed;
                    ms.AppendEntity(pline);
                    tr.AddNewlyCreatedDBObject(pline, true);
                }
                tr.Commit();
            }

            var outputPath = Path.Combine(workDir, "output.dwg");
            db.SaveAs(outputPath, DwgVersion.Current);
            Application.DocumentManager.MdiActiveDocument?.Editor
                .WriteMessage($"\nSaved {data.Markups.Length} markup(s) → {outputPath}");
        }

        // ── PSDCS → DCS → WCS matrix used by interactive DRAWMARKUP ────────────
        public static Matrix3d GetTransformationMatrix()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            if (ed == null) throw new ArgumentNullException(nameof(ed));
            Database db = HostApplicationServices.WorkingDatabase;
            if (db.TileMode)
                throw new Autodesk.AutoCAD.Runtime.Exception(ErrorStatus.NotInPaperspace);

            using var tr = db.TransactionManager.StartTransaction();
            Viewport vp = (Viewport)tr.GetObject(ed.CurrentViewportObjectId, OpenMode.ForRead);
            if (vp.Number == 1)
            {
                try
                {
                    ed.SwitchToModelSpace();
                    vp = (Viewport)tr.GetObject(ed.CurrentViewportObjectId, OpenMode.ForRead);
                    ed.SwitchToPaperSpace();
                }
                catch
                {
                    throw new Autodesk.AutoCAD.Runtime.Exception(
                        ErrorStatus.CannotChangeActiveViewport);
                }
            }
            Point3d viewCtr = new Point3d(vp.ViewCenter.X, vp.ViewCenter.Y, 0.0);
            Matrix3d DCSToPSDCS = Matrix3d.Scaling(vp.CustomScale, vp.CenterPoint) *
                                  Matrix3d.Displacement(viewCtr.GetVectorTo(vp.CenterPoint));
            Matrix3d PSDCSToDCS = DCSToPSDCS.Inverse();
            Matrix3d DCSToWCS = ed.DCS2WCS();
            var xform = DCSToWCS * PSDCSToDCS;
            tr.Commit();
            return xform;
        }
    }
}
