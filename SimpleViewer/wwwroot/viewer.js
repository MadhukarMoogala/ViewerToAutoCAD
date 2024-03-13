/// import * as Autodesk from "@types/forge-viewer";
async function getAccessToken(callback) {
    try {
        const resp = await fetch('/api/auth/token');
        if (!resp.ok) {
            throw new Error(await resp.text());
        }
        const { access_token, expires_in } = await resp.json();
        callback(access_token, expires_in);
    } catch (err) {
        alert('Could not obtain access token. See the console for more details.');
        console.error(err);
    }
}
export function initViewer(container) {
    return new Promise(function (resolve, reject) {
        Autodesk.Viewing.Initializer({ env: 'AutodeskProduction', getAccessToken }, function () {
            const config = {
                extensions: [
                            'Autodesk.DocumentBrowser',
                            'Autodesk.Viewing.MarkupsCore',
                            'Autodesk.Viewing.MarkupsGui',
                            'MarkupSelector']
            };
            const viewer = new Autodesk.Viewing.GuiViewer3D(container, config);
            viewer.start();
            viewer.setTheme('light-theme');
            resolve(viewer);
        });
    });
}
export function loadModel(viewer, urn) {
    return new Promise(function (resolve, reject) {
        function onDocumentLoadSuccess(doc) {
            resolve(viewer.loadDocumentNode(doc, doc.getRoot().getDefaultGeometry()));
        }
        function onDocumentLoadFailure(code, message, errors) {
            reject({ code, message, errors });
        }
        viewer.setLightPreset(0);
        Autodesk.Viewing.Document.load('urn:' + urn, onDocumentLoadSuccess, onDocumentLoadFailure);
    });
}
const MARKUP_INFO = {
    ARROW: { id: 7, type: 'arrow' },
    CALLOUT: { id: 8, type: 'callout' },
    CIRCLE: { id: 4, type: 'ellipse' },
    CLOUD: { id: 11, type: 'cloud' },
    FREE_LINE: { id: 1, type: 'freehand' },
    HIGHLIGHT: { id: 9, type: 'highlight' },
    ICON: { id: 12, type: 'stamp' },
    POLYCLOUD: { id: 10, type: 'polycloud' },
    POLYLINE: { id: 6, type: 'polyline' },
    RECTANGLE: { id: 5, type: 'rectangle' },
    TEXT: { id: 2, type: 'label' }
};
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
                        case MARKUP_INFO.CALLOUT.type: {
                            alert("Yikes, callout not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.CIRCLE.type: {
                            alert("Yikes, circle not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.CLOUD.type: {
                            alert("Yikes, cloud not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.FREE_LINE.type: {
                            alert("Yikes, free line not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.HIGHLIGHT.type: {
                            alert("Yikes, highlight not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.ICON.type: {
                            alert("Yikes, icon not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.POLYCLOUD.type: {
                            alert("Yikes, polycloud not supported yet!");
                            break;
                        }
                        case MARKUP_INFO.POLYLINE.type: {
                            alert("Yikes, polyline not supported yet!");
                            break;
                        }
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