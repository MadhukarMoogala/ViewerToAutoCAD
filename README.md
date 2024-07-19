# Importing Markup geometries from APS Viewer to AutoCAD 2D Drawing

The objective of this project is to understand how to import custom graphics drawn on APS Viewer back to the AutoCAD base drawing. The scope of this work is limited to AutoCAD 2D drawings. This workflow can be automated using APS Design Automation. However, the project does not focus on the DA part at the moment. The essential bells and whistles required to create the Drawing Review System are provided.

In a nutshell, you need a 
- APS Viewer for viewing the drawing in a cloud-based environment.
- A Markup extension to annotate the drawing in the Viewer.
- APS Design Automation to add these markup annotations as AutoCAD entities onto the actual Master drawing (.dwg).

#### Terminology

- Base drawing
  
  - Any 2D drawing that is translated using APS Model Derivative service for Viewing on APS Viewer.
  - Any 2D drawing that has well defined units either metric or inches.

- DWG Extractor
  
  - The Engine behind the Model Derivative service that processes and converts it to Viewable (SVF) for 3D models and PDF for 2D drawings.

- Job Payload
  
  - Any data sent in a request to Model Derivative service API.

- LMV
  
  - Large Model Viewer, commericial name is APS Viewer.

- Coordinate System in AutoCAD
  
  - PSDCS - Paper Space Display Coordinate System
  - DCS - Display Cooridnate System
  - WCS - World Cooridnate System
  - UCS - User Cooridnate System

#### Intial Setup

- Create a basic APS Viewer based on this [Simple Viewer | Autodesk Platform Services Tutorials](https://tutorials.autodesk.io/tutorials/simple-viewer/)

#### Workflow

- First create a custom extension to draw Markups on LMV Viewer, currently the code cover only `Rectangle ` markup, later this can be extended to other markups as well, add this code to `viewer.js`
  
  - We need to transform the markup coordinates from LMV Canvas to AutoCAD paperspace.
  
  - We need to construct the `rectangle` corner points from `markup`, there are various `markup types` for `rectangle` the markup tool extension provides us position and size, `position` is centered, `size` is `width` X `height`.
  
  - Apply the `pageToModelTransform` function on retrieved corner points.
  
  ```js
  class MarkupSelector extends Autodesk.Viewing.Extension {
      constructor(viewer, options) {
          super(viewer, options); // Calls the parent constructor
      }
      getRectangleCoordinates(centerX, centerY, width, height) {
          if (centerX === undefined || centerY === undefined || width <= 0 || height <= 0) {
              throw new Error("Invalid arguments: x, y, width, and height must be positive numbers.");
          }
          const bottomLeft = {
              x: centerX - width / 2,
              y: centerY - height / 2,
          };
          const bottomRight = {
              x: centerX + width / 2,
              y: centerY - height / 2,
          };
          const topLeft = {
              x: centerX - width / 2,
              y: centerY + height / 2,
          };
          const topRight = {
              x: centerX + width / 2,
              y: centerY + height / 2,
          };
          // Return the coordinates of the four corners of the rectangle in CCW.
          /*
          TL───────────────────────────TR
          │                            │ 
          │    ┌─────────────────┐     │ 
          │    │                 │     │ 
          │    │     x,y         │     │ 
          │    ▼                 │     │ 
          │                      │     │ 
          │     ─────────────────┘     │ 
          │                            │ 
          BL───────────────────────────BR
          */
          return [bottomLeft, bottomRight,topRight,topLeft];
      }
      async load() {
          alert('MarkupSelector is loaded!');
          await Autodesk.Viewing.EventUtils.waitUntilGeometryLoaded(this.viewer);
          let markupext = this.viewer.getExtension('Autodesk.Viewing.MarkupsCore');
          // Fired whenever the drawing tool changes. 
          // For example, when the Arrow drawing tool changes into the Rectangle drawing tool.
          markupext.addEventListener('EVENT_EDITMODE_CHANGED', async (ev) => {
              const editTool = ev.target;         
              if (editTool) {
                  const type = editTool.type;
                  if (type !== MARKUP_INFO.RECTANGLE.type) {
                      switch (type) {
                          case MARKUP_INFO.ARROW.type: {
                              alert("Yikes, arrow not supported yet!");
                              break;
                          }
                          /*...*/
                          default:
                              alert(`Unknown markup type: ${type}`);
                      }
                      return;
                  }           
                  // Fired when a markup is selected.
                  markupext.addEventListener('EVENT_MARKUP_SELECTED', async (ev) => {
                      const markup = ev.markup;
                      console.log(markup);
                      const rect = this.getRectangleCoordinates(markup.position.x,
                          markup.position.y,
                          markup.size.x,
                          markup.size.y);
                      //apply LMV viewport coordinates to model coordinates.                   
                      //This is the viewport id, need to figure out, how to get it from the viewer, 
                      // I'm assuming it's the last one, but it's not guaranteed.
                      const vports = this.viewer.model.getData().viewports;
                      const vpId = vports && Object.keys(vports).length > 0 ? Number(Object.keys(vports).pop()) : undefined;
                      const xform = this.viewer.model.getPageToModelTransform(vpId);
                      const modelCoords = rect.map((pt) => {
                          const modelPt = new THREE.Vector3(pt.x, pt.y, 0).applyMatrix4(xform);
                          return { x: modelPt.x, y: modelPt.y };
                      });
                      // Find minimum and maximum X, Y
                      let minX = modelCoords[0].x;
                      let maxX = modelCoords[0].x;
                      let minY = modelCoords[0].y;
                      let maxY = modelCoords[0].y;
                      for (const pt of modelCoords) {
                          minX = Math.min(minX, pt.x);
                          maxX = Math.max(maxX, pt.x);
                          minY = Math.min(minY, pt.y);
                          maxY = Math.max(maxY, pt.y);
                      }
                      // Identify corners
                      const bottomLeft = [minX, maxY];
                      const topRight = [maxX, minY];
                      console.log(bottomLeft, topRight);
                  });
              }
          });      
          return true;
      }
      async unload() {
          alert('MarkupSelector is now unloaded!');
          return true;
      };
  }
  Autodesk.Viewing.theExtensionManager.registerExtension("MarkupSelector", MarkupSelector);
  ```

- We need to allow the Model Translation service to use `advanced` option in Job Payload, this advanced option has two switches `legacy` and `pdf` .
  
  - `legagcy` switch uses old translation pipeline to convert AutoCAD drawing to `F2D` [probably it meant `Forge 2D` ].
  
  - `pdf` switch uses modern translation pipeline to convert AutoCAD drawing to `pdf` which smoother and faster , only textual properties are exported, for more information refer the blog post [DWG translation optimizations](https://aps.autodesk.com/blog/model-derivative-dwg-translation-optimizations)

- As of today `forge-api-dotnet` SDK doesn't have API property to add this new switch loader `pdf`, let's implement a custom JobPayload.
  
  ```cs
   [DataContract]
   public class JobDwgPdfOutputPayloadAdvanced(JobDwgPdfOutputPayloadAdvanced.Views2DEnum? views2D = null) : IEquatable<JobDwgPdfOutputPayloadAdvanced>, IJobPayloadItemAdvanced
   {
       /// <summary>
       /// An option to be specified when the input file type is DWG.
       /// </summary>
       [JsonConverter(typeof(StringEnumConverter))]
       public enum Views2DEnum
       {
           /// <summary>
           /// Enum Legacy for "legacy"
           /// </summary>
           [EnumMember(Value = "legacy")]
           Legacy,
           /// <summary>
           /// Enum Modern for "modern"
           /// </summary>
           [EnumMember(Value = "pdf")]
           PDF
       }
       /// <summary>
       /// An option to be specified when the input file type is DWG.
       /// </summary>
       [DataMember(Name = "2dviews", EmitDefaultValue = false)]
       public Views2DEnum? Views2D { get; set; } = views2D;
       public bool Equals(JobDwgPdfOutputPayloadAdvanced? other)
       {
           if (other == null)
               return false;
           return
               (
                   Views2D == other.Views2D ||
                   Views2D != null &&
                   Views2D.Equals(other.Views2D)
               );
       }
       bool IJobPayloadItemAdvanced.Equals(object obj)
       {
           return Equals(obj as JobDwgPdfOutputPayloadAdvanced);
       }
       int IJobPayloadItemAdvanced.GetHashCode()
       {
           // credit: http://stackoverflow.com/a/263416/677735
           unchecked // Overflow is fine, just wrap
           {
               int hash = 41;
               // Suitable nullity checks etc, of course :)
               if (Views2D != null)
                   hash = hash * 59 + Views2D.GetHashCode();
               return hash;
           }
       }
       string IJobPayloadItemAdvanced.ToJson()
       {
           return JsonConvert.SerializeObject(this, Formatting.Indented);
       }
       string IJobPayloadItemAdvanced.ToString()
       {
           var sb = new StringBuilder();
           sb.Append("class JobDwgPdfOutputPayloadAdvanced {\n");
           sb.Append("  2dViews: ").Append(Views2D).Append('\n');
           sb.Append("}\n");
           return sb.ToString();
       }
       public override bool Equals(object? obj)
       {
           return Equals(obj as JobDwgPdfOutputPayloadAdvanced);
       }
       public override int GetHashCode()
       {
           throw new NotImplementedException();
       }
   }
  ```

- Add custom payload to `TranslateModel` method.
  
  ```cs
  public async Task<Job> TranslateModel(string objectId, string rootFilename)
  {
      var token = await GetInternalToken();
      var api = new DerivativesApi();
      api.Configuration.AccessToken = token.AccessToken;
      var formats = new List<JobPayloadItem> {
      new JobPayloadItem (
          JobPayloadItem.TypeEnum.Svf,
          [JobPayloadItem.ViewsEnum._2d,
          JobPayloadItem.ViewsEnum._3d],
          new JobDwgPdfOutputPayloadAdvanced(views2D:JobDwgPdfOutputPayloadAdvanced.Views2DEnum.PDF))
      };
      var payload = new JobPayload(
      new JobPayloadInput(Base64Encode(objectId)),
      new JobPayloadOutput(formats));
      if (!string.IsNullOrEmpty(rootFilename))
      {
          payload.Input.RootFilename = rootFilename;
          payload.Input.CompressedUrn = true;
      }
      var job = (await api.TranslateAsync(payload)).ToObject<Job>();
      return job;
  }
  ```

- After drawing the `rectangle` from the corner points in PaperSpace, we need to draw the `rectangle` entity in `Model Space`.
  
  - Transforming a `point` from `Paper Space` to `Model Space` is a two step process.
  
  - First, we need to construct a transformation matrix from PSDCS to DCS.
  
  - Next, we need to construct a transformation matrix from DCS to WCS.
  
  - Product of  `PSDCSToDCS` X `DCSToWCS` gives the final matrix.
  
  ```cs
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
  ```

#### Video :


https://github.com/MadhukarMoogala/ViewerToAutoCAD/assets/6602398/13335ba8-52af-4429-91eb-273e3ff53bea



## License

This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT)

## Written by

Madhukar Moogala  [@galakar](https://twitter.com/galakar), [APS Partner Development](http://aps.autodesk.com)
