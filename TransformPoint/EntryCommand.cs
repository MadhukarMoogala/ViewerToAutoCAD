using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Viewport = Autodesk.AutoCAD.DatabaseServices.Viewport;

namespace TransformPoint
{
    public static class EditorExtension
    {
        /// <summary>
        /// Gets current UCS to WCS transformation matrix.
        /// </summary>

        public static Matrix3d UCS2WCS(this Editor ed)
        {
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));
            return ed.CurrentUserCoordinateSystem;
        }

        /// <summary>
        /// Gets WCS to UCS transformation matrix.
        /// </summary>
        
        public static Matrix3d WCS2UCS(this Editor ed)
        {
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));
            return ed.CurrentUserCoordinateSystem.Inverse();
        }

        /// <summary>
        /// Viewport DCS to WCS transformation matrix.
        /// </summary>
        public static Matrix3d DCS2WCS(this Editor ed)
        {
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));
            Matrix3d dcsToWcs = new Matrix3d();
            bool tilemode = ed.Document.Database.TileMode;
            if (!tilemode)
                ed.SwitchToModelSpace();
            using (ViewTableRecord vtr = ed.GetCurrentView())
            {
                dcsToWcs =
                    Matrix3d.Rotation(-vtr.ViewTwist, vtr.ViewDirection, vtr.Target) *
                    Matrix3d.Displacement(vtr.Target - Point3d.Origin) *
                    Matrix3d.PlaneToWorld(vtr.ViewDirection);
            }
            if (!tilemode)
                ed.SwitchToPaperSpace();
            return dcsToWcs;
        }
    }
    public class EntryCommand
    {
        [CommandMethod("DRAWMARKUP", CommandFlags.NoTileMode)]
        public void PointToModelSpace()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            Database db = HostApplicationServices.WorkingDatabase;
            PromptEntityResult res = ed.GetEntity("Pick markup entity in PS");
            if (res.Status != PromptStatus.OK) return;       
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms =
                    (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var ent = tr.GetObject(res.ObjectId, OpenMode.ForRead) as Entity;
                var entCopy = ent.GetTransformedCopy(GetTranformationMatrix());
                entCopy.ColorIndex = 3;
                ms.AppendEntity(entCopy);
                tr.AddNewlyCreatedDBObject(entCopy, true);
                tr.Commit();
            }
        }
        public static Matrix3d GetTranformationMatrix()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));
            Database db = HostApplicationServices.WorkingDatabase;
            if (db.TileMode)
                throw new Autodesk.AutoCAD.Runtime.Exception(ErrorStatus.NotInPaperspace);
            Matrix3d xform = new Matrix3d();
            //Converts viewport DCS to WCS via paperspace DCS.
            // DCS : Display Coordinate System
            // WCS : World Coordinate System

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Viewport vp =
                    (Viewport)tr.GetObject(ed.CurrentViewportObjectId, OpenMode.ForRead);
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
                        throw new Autodesk.AutoCAD.Runtime.Exception(ErrorStatus.CannotChangeActiveViewport);
                    }
                }
                Point3d viewCtr = new Point3d(vp.ViewCenter.X, vp.ViewCenter.Y, .0);
                //Step 1: Transform the point from PSDCS to DCS
                Matrix3d DCSToPSDCS = Matrix3d.Scaling(vp.CustomScale, vp.CenterPoint) *
                                      Matrix3d.Displacement(viewCtr.GetVectorTo(vp.CenterPoint));
                Matrix3d PSDCSToDCS = DCSToPSDCS.Inverse();               
                //Step 2 = Transform the point from DCS to WCS
                Matrix3d DCSToWCS = ed.DCS2WCS();
                xform = DCSToWCS * PSDCSToDCS;
                tr.Commit();            }
            return xform;
        }
    }
}
