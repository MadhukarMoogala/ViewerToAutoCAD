// Accumulated markups from all EVENT_MARKUP_SELECTED events this session
const _markups = [];
export function getMarkups()  { return [..._markups]; }
export function clearMarkups() { _markups.length = 0; }

async function getAccessToken(callback) {
    try {
        const resp = await fetch('/api/auth/token');
        if (!resp.ok) throw new Error(await resp.text());
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
                    'MarkupSelector'
                ]
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
    ARROW:     { type: 'arrow' },
    CALLOUT:   { type: 'callout' },
    CIRCLE:    { type: 'ellipse' },
    CLOUD:     { type: 'cloud' },
    FREE_LINE: { type: 'freehand' },
    HIGHLIGHT: { type: 'highlight' },
    ICON:      { type: 'stamp' },
    POLYCLOUD: { type: 'polycloud' },
    POLYLINE:  { type: 'polyline' },
    RECTANGLE: { type: 'rectangle' },
    TEXT:      { type: 'label' }
};

class MarkupSelector extends Autodesk.Viewing.Extension {
    constructor(viewer, options) {
        super(viewer, options);
    }

    // Returns the 4 corner points of a rectangle markup in LMV canvas space.
    // position is the center; size is width × height.
    getRectangleCorners(centerX, centerY, width, height) {
        return [
            { x: centerX - width / 2, y: centerY - height / 2 }, // BL
            { x: centerX + width / 2, y: centerY - height / 2 }, // BR
            { x: centerX + width / 2, y: centerY + height / 2 }, // TR
            { x: centerX - width / 2, y: centerY + height / 2 }  // TL
        ];
    }

    async load() {
        await Autodesk.Viewing.EventUtils.waitUntilGeometryLoaded(this.viewer);
        const markupExt = this.viewer.getExtension('Autodesk.Viewing.MarkupsCore');

        // Register directly — no dependency on tool switching.
        // Fires whenever any markup is selected; we filter by type below.
        markupExt.addEventListener('EVENT_MARKUP_SELECTED', (ev) => {
            const markup = ev.markup;
            if (!markup) return;

            const supported = [MARKUP_INFO.RECTANGLE.type, MARKUP_INFO.POLYLINE.type, MARKUP_INFO.FREE_LINE.type];
            if (!supported.includes(markup.type)) {
                console.warn(`MarkupSelector: '${markup.type}' not yet supported for DA export.`);
                return;
            }

            // Resolve viewport transform (vpId=0 = paper/scale-only; last key = model viewport with full PSDCS→WCS)
            const vports = this.viewer.model.getData().viewports;
            const vpKeys = vports ? Object.keys(vports) : [];
            const vpId   = vpKeys.length > 0 ? Number(vpKeys[vpKeys.length - 1]) : undefined;
            const xform  = this.viewer.model.getPageToModelTransform(vpId);

            const applyXform = pt => {
                const v = new THREE.Vector3(pt.x, pt.y, 0).applyMatrix4(xform);
                return { x: v.x, y: v.y };
            };

            let corners, closed;

            if (markup.type === MARKUP_INFO.RECTANGLE.type) {
                const raw = this.getRectangleCorners(
                    markup.position.x, markup.position.y,
                    markup.size.x,     markup.size.y);
                const pts = raw.map(applyXform);
                let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
                for (const pt of pts) {
                    minX = Math.min(minX, pt.x); maxX = Math.max(maxX, pt.x);
                    minY = Math.min(minY, pt.y); maxY = Math.max(maxY, pt.y);
                }
                corners = [
                    { x: minX, y: minY }, { x: maxX, y: minY },
                    { x: maxX, y: maxY }, { x: minX, y: maxY }
                ];
                closed = true;
            } else {
                // polyline / freehand — use markup.locations
                // freehand stores locations relative to markup.position when isAbsoluteCoords is false
                let pts = markup.locations.map(p => ({ x: p.x, y: p.y }));
                if (!markup.isAbsoluteCoords) {
                    pts = pts.map(p => ({ x: p.x + markup.position.x, y: p.y + markup.position.y }));
                }
                corners = pts.map(applyXform);
                closed  = markup.closed ?? false;
            }

            const markupData = { id: markup.id, type: markup.type, corners, closed };
            const idx = _markups.findIndex(m => m.id === markup.id);
            if (idx >= 0) _markups[idx] = markupData;
            else          _markups.push(markupData);

            console.log(`✓ Markup #${markup.id} collected (${_markups.length} total)`);
        });

        return true;
    }

    async unload() {
        return true;
    }
}

Autodesk.Viewing.theExtensionManager.registerExtension('MarkupSelector', MarkupSelector);
