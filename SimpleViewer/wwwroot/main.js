import { initViewer, loadModel, getMarkups, clearMarkups, removeMarkup } from './viewer.js';

// ── State ─────────────────────────────────────────────────────────
let _viewer  = null;
let _urn     = null;
let _pollTimer = null;

// ── Boot ──────────────────────────────────────────────────────────
initViewer(document.getElementById('viewer')).then(viewer => {
    _viewer = viewer;
    const hashUrn = window.location.hash?.substring(1) || null;
    loadOssTree(hashUrn);
    wireUpload();
    wireDa();
    window.addEventListener('markup:updated', () => renderMarkups());
});

// ══════════════════════════════════════════════════════════════════
// OSS Tree
// ══════════════════════════════════════════════════════════════════

async function loadOssTree(selectUrn) {
    const tree  = document.getElementById('oss-tree');
    const btn   = document.getElementById('refresh-btn');
    btn.classList.add('spin');
    tree.innerHTML = '<div class="sb-loading"><div class="spinner"></div><span>Loading…</span></div>';

    try {
        const [br, mr] = await Promise.all([
            fetch('/api/models/bucket'),
            fetch('/api/models')
        ]);
        if (!br.ok || !mr.ok) throw new Error('Bucket request failed');
        const { name: bucket } = await br.json();
        const objects = await mr.json(); // [{ name, urn, size }]
        renderTree(bucket, objects);

        // Only restore from hash if it points to a DWG — never auto-pick
        const target = selectUrn && objects.find(o => o.urn === selectUrn && isDwg(o.name))?.urn;
        if (target) openFile(target);
    } catch (err) {
        tree.innerHTML = `<div class="sb-empty">⚠ ${err.message}</div>`;
    } finally {
        btn.classList.remove('spin');
    }
}

function renderTree(bucket, objects) {
    const tree = document.getElementById('oss-tree');
    const sorted = [...objects].sort((a, b) => typeRank(a.name) - typeRank(b.name));

    const rows = sorted.map(o => treeRow(o)).join('');
    tree.innerHTML = `
        <div class="tree-root">
            <div class="tree-bucket-row">
                <span class="tree-bucket-icon">🪣</span>
                <span class="tree-bucket-name" title="${bucket}">${bucket}</span>
            </div>
            ${rows.length
                ? `<div class="tree-children" id="tree-ch">${rows}</div>`
                : '<div class="sb-empty">No files. Upload a DWG to begin.</div>'}
        </div>`;
}

function treeRow(obj) {
    const ext  = obj.name.split('.').pop().toLowerCase();
    const icon = ext === 'dwg' ? '📐' : ext === 'pdf' ? '📄' : ext === 'json' ? '📋' : '📎';
    const can  = ext === 'dwg';
    const act  = obj.urn === _urn;
    return `<div class="tree-file${can ? ' clickable' : ''}${act ? ' active' : ''}" data-urn="${obj.urn}">
        <span class="f-ico">${icon}</span>
        <span class="f-name" title="${obj.name}">${obj.name}</span>
        <span class="f-meta">
            <span class="f-size">${fmtSize(obj.size)}</span>
        </span>
    </div>`;
}

function typeRank(name) {
    const l = name.toLowerCase();
    if (l.endsWith('.dwg'))  return 0;
    if (l.endsWith('.pdf'))  return 1;
    if (l.endsWith('.json')) return 2;
    return 3;
}

function isDwg(name) { return name.toLowerCase().endsWith('.dwg'); }

function fmtSize(b) {
    if (!b || b <= 0) return '';
    if (b < 1024)        return `${b} B`;
    if (b < 1048576)     return `${(b / 1024).toFixed(1)} KB`;
    return `${(b / 1048576).toFixed(1)} MB`;
}

// Delegated click on tree
document.getElementById('oss-tree').addEventListener('click', e => {
    const row = e.target.closest('.tree-file.clickable');
    if (row) openFile(row.dataset.urn);
});

// ══════════════════════════════════════════════════════════════════
// Model Selection + Translation Polling
// ══════════════════════════════════════════════════════════════════

async function openFile(urn) {
    if (_pollTimer) { clearTimeout(_pollTimer); _pollTimer = null; }
    _urn = urn;
    window.location.hash = urn;
    highlightActive(urn);

    try {
        const r = await fetch(`/api/models/${urn}/status`);
        if (!r.ok) throw new Error(await r.text());
        const { status, progress } = await r.json();

        setTransBadge(urn, status);

        if (status === 'n/a') {
            await fetch(`/api/models/${urn}/translate`, { method: 'POST' });
            setViewerMsg('Starting translation…');
            _pollTimer = setTimeout(() => openFile(urn), 4000);
        } else if (status === 'inprogress') {
            setViewerMsg(`Translating… ${progress ?? ''}`);
            _pollTimer = setTimeout(() => openFile(urn), 4000);
        } else if (status === 'failed') {
            setViewerMsg('Translation failed. Check the Admin page.');
        } else {
            clearViewerMsg();
            clearMarkups();
            loadModel(_viewer, urn);
        }
    } catch (err) {
        setViewerMsg(`Error: ${err.message}`);
    }
}

function highlightActive(urn) {
    document.querySelectorAll('.tree-file').forEach(el => {
        el.classList.toggle('active', el.dataset.urn === urn);
    });
}

function setTransBadge(urn, status) {
    const row = document.querySelector(`.tree-file[data-urn="${urn}"]`);
    if (!row) return;
    let b = row.querySelector('.tr-badge');
    if (status === 'inprogress') {
        if (!b) { b = document.createElement('span'); row.querySelector('.f-meta')?.prepend(b); }
        b.className = 'tr-badge prog'; b.textContent = '…';
    } else if (status === 'failed') {
        if (!b) { b = document.createElement('span'); row.querySelector('.f-meta')?.prepend(b); }
        b.className = 'tr-badge fail'; b.textContent = 'ERR';
    } else if (b) {
        b.remove();
    }
}

function setViewerMsg(msg) {
    let el = document.getElementById('viewer-msg');
    if (!el) {
        el = document.createElement('div');
        el.id = 'viewer-msg';
        Object.assign(el.style, {
            position: 'absolute', inset: '0',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: 'rgba(7,10,16,.7)',
            backdropFilter: 'blur(3px)',
            color: '#f5f5f5', fontFamily: "ArtifaktElement, system-ui, sans-serif",
            fontSize: '14px', fontWeight: '500',
            zIndex: '5', pointerEvents: 'none'
        });
        document.getElementById('viewer').appendChild(el);
    }
    el.textContent = msg;
    el.style.display = 'flex';
}

function clearViewerMsg() {
    const el = document.getElementById('viewer-msg');
    if (el) el.style.display = 'none';
}

// ══════════════════════════════════════════════════════════════════
// Upload
// ══════════════════════════════════════════════════════════════════

function wireUpload() {
    const btn   = document.getElementById('upload-btn');
    const input = document.getElementById('file-input');

    document.getElementById('refresh-btn').addEventListener('click', () => loadOssTree(_urn));
    btn.addEventListener('click', () => input.click());

    document.getElementById('clear-bucket-btn').addEventListener('click', async () => {
        if (!confirm('Delete ALL files in the bucket? This cannot be undone.')) return;
        const clearBtn = document.getElementById('clear-bucket-btn');
        clearBtn.disabled = true;
        try {
            const r = await fetch('/api/models/bucket', { method: 'DELETE' });
            if (!r.ok) throw new Error(await r.text());
            _urn = null;
            window.location.hash = '';
            clearMarkups();
            await loadOssTree(null);
        } catch (err) {
            alert(`Clear failed: ${err.message}`);
        } finally {
            clearBtn.disabled = false;
        }
    });

    input.addEventListener('change', async () => {
        const file = input.files[0];
        if (!file) return;
        const fd = new FormData();
        fd.append('model-file', file);
        if (file.name.endsWith('.zip')) {
            const ep = window.prompt('Main filename inside zip:');
            if (!ep) return;
            fd.append('model-zip-entrypoint', ep);
        }
        btn.disabled = true;
        btn.textContent = 'Uploading…';
        try {
            const r = await fetch('/api/models', { method: 'POST', body: fd });
            if (!r.ok) throw new Error(await r.text());
            const m = await r.json();
            await loadOssTree(m.urn);
        } catch (err) {
            alert(`Upload failed: ${err.message}`);
        } finally {
            btn.disabled = false;
            btn.textContent = '↑ Upload DWG';
            input.value = '';
        }
    });
}

// ══════════════════════════════════════════════════════════════════
// Markup List
// ══════════════════════════════════════════════════════════════════

function renderMarkups() {
    const markups = getMarkups();
    const list    = document.getElementById('markup-list');
    const badge   = document.getElementById('markup-badge');
    const genBtn  = document.getElementById('generate-pdf-btn');

    badge.textContent = markups.length;
    badge.classList.toggle('zero', markups.length === 0);
    genBtn.disabled = markups.length === 0;

    if (markups.length === 0) {
        list.innerHTML = '<div class="sb-empty">Draw rectangles in the viewer,<br>then click each to capture.</div>';
        return;
    }

    list.innerHTML = markups.map(m => `
        <div class="mu-row">
            <span class="mu-ico">▭</span>
            <span class="mu-lbl">${ucfirst(m.type)}</span>
            <span class="mu-id">#${m.id}</span>
            <button class="mu-rm" data-id="${m.id}" title="Remove">✕</button>
        </div>`).join('');
}

// Delegated remove
document.getElementById('markup-list').addEventListener('click', e => {
    const btn = e.target.closest('.mu-rm');
    if (btn) removeMarkup(Number(btn.dataset.id));
});

function ucfirst(s) { return s ? s[0].toUpperCase() + s.slice(1) : s; }

// ══════════════════════════════════════════════════════════════════
// Design Automation
// ══════════════════════════════════════════════════════════════════

function wireDa() {
    document.getElementById('generate-pdf-btn').addEventListener('click', runDa);
    document.getElementById('da-close-btn').addEventListener('click', hideDaPanel);
}

async function runDa() {
    const markups = getMarkups();
    if (!markups.length) return;
    if (!_urn) { alert('Select a model first.'); return; }

    document.getElementById('generate-pdf-btn').disabled = true;
    showDaPanel();
    setDaPhase(0, 'Uploading markups…');

    try {
        const r = await fetch('/api/da/workitems', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ urn: _urn, markups })
        });
        if (!r.ok) throw new Error(await r.text());
        const { jobId } = await r.json();

        setDaPhase(1, 'Importing markups into DWG…');
        const pdfUrl = await pollDaJob(jobId);

        setDaSuccess(pdfUrl);
        clearMarkups();
        await loadOssTree(_urn);
    } catch (err) {
        setDaError(err.message, err.reportUrl);
    } finally {
        document.getElementById('generate-pdf-btn').disabled = false;
    }
}

async function pollDaJob(jobId) {
    return new Promise((resolve, reject) => {
        const t = setInterval(async () => {
            try {
                const r = await fetch(`/api/da/workitems/${jobId}`);
                if (!r.ok) throw new Error(await r.text());
                const d = await r.json();
                if (d.status === 'importing') setDaPhase(1, 'Importing markups into DWG…');
                if (d.status === 'plotting')  setDaPhase(2, 'Plotting DWG to PDF…');
                if (d.status === 'success')   { clearInterval(t); resolve(d.pdfUrl); }
                if (d.status === 'failed') {
                    clearInterval(t);
                    const e = Object.assign(new Error(d.error ?? 'DA job failed'), { reportUrl: d.reportUrl });
                    reject(e);
                }
            } catch (err) { clearInterval(t); reject(err); }
        }, 5000);
    });
}

// ── DA Panel helpers ──────────────────────────────────────────────

function showDaPanel() {
    const p = document.getElementById('da-panel');
    document.getElementById('da-actions').innerHTML = '';
    setSteps(-1);
    p.classList.add('visible');
    // Allow APS Viewer to resize after panel opens
    setTimeout(() => _viewer?.resize(), 380);
}

function hideDaPanel() {
    document.getElementById('da-panel').classList.remove('visible');
    setTimeout(() => _viewer?.resize(), 380);
}

function setDaPhase(step, text) {
    document.getElementById('da-txt').textContent = text;
    const fill = document.getElementById('da-fill');
    fill.className = 'da-fill ind'; // indeterminate shimmer
    fill.style.width = '';
    setSteps(step);
}

function setDaSuccess(pdfUrl) {
    document.getElementById('da-txt').textContent = 'PDF generated successfully.';
    const fill = document.getElementById('da-fill');
    fill.className = 'da-fill ok';
    fill.style.width = '100%';
    setSteps(4);
    document.getElementById('da-actions').innerHTML = `
        <a class="btn-dl" href="${pdfUrl}" target="_blank" download>↓ Download PDF</a>`;
}

function setDaError(msg, reportUrl) {
    document.getElementById('da-txt').textContent = msg.substring(0, 80);
    const fill = document.getElementById('da-fill');
    fill.className = 'da-fill err';
    fill.style.width = '100%';
    if (reportUrl) {
        document.getElementById('da-actions').innerHTML =
            `<a class="btn-rpt" href="${reportUrl}" target="_blank">View Report</a>`;
    }
}

function setSteps(activeIdx) {
    for (let i = 0; i < 4; i++) {
        const el = document.getElementById(`da-step-${i}`);
        if (!el) continue;
        el.className = i < activeIdx ? 'da-step done' : i === activeIdx ? 'da-step active' : 'da-step';
    }
}
