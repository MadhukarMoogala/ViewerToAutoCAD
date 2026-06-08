import { initViewer, loadModel, getMarkups, clearMarkups } from './viewer.js';

initViewer(document.getElementById('preview')).then(viewer => {
    const urn = window.location.hash?.substring(1);
    setupModelSelection(viewer, urn);
    setupModelUpload(viewer);
    setupDaControls(viewer);
});

// ── Model selection ───────────────────────────────────────────────────────────
async function setupModelSelection(viewer, selectedUrn) {
    const dropdown = document.getElementById('models');
    dropdown.innerHTML = '';
    try {
        const resp = await fetch('/api/models');
        if (!resp.ok) throw new Error(await resp.text());
        const models = await resp.json();
        dropdown.innerHTML = models.map(m =>
            `<option value=${m.urn} ${m.urn === selectedUrn ? 'selected' : ''}>${m.name}</option>`
        ).join('\n');
        dropdown.onchange = () => onModelSelected(viewer, dropdown.value);
        if (dropdown.value) onModelSelected(viewer, dropdown.value);
    } catch (err) {
        alert('Could not list models. See the console for more details.');
        console.error(err);
    }
}

async function onModelSelected(viewer, urn) {
    if (window.onModelSelectedTimeout) {
        clearTimeout(window.onModelSelectedTimeout);
        delete window.onModelSelectedTimeout;
    }
    window.location.hash = urn;
    try {
        const resp = await fetch(`/api/models/${urn}/status`);
        if (!resp.ok) throw new Error(await resp.text());
        const status = await resp.json();
        switch (status.status) {
            case 'n/a':
                showNotification('Model has not been translated.');
                break;
            case 'inprogress':
                showNotification(`Model is being translated (${status.progress})...`);
                window.onModelSelectedTimeout = setTimeout(onModelSelected, 5000, viewer, urn);
                break;
            case 'failed':
                showNotification(`Translation failed. <ul>${status.messages.map(
                    msg => `<li>${JSON.stringify(msg)}</li>`).join('')}</ul>`);
                break;
            default:
                clearNotification();
                clearMarkups(); // Clear stale markups when switching models
                loadModel(viewer, urn);
        }
    } catch (err) {
        alert('Could not load model. See the console for more details.');
        console.error(err);
    }
}

// ── Model upload ──────────────────────────────────────────────────────────────
async function setupModelUpload(viewer) {
    const upload = document.getElementById('upload');
    const input  = document.getElementById('input');
    const models = document.getElementById('models');
    upload.onclick = () => input.click();
    input.onchange = async () => {
        const file = input.files[0];
        const data = new FormData();
        data.append('model-file', file);
        if (file.name.endsWith('.zip')) {
            const entrypoint = window.prompt('Enter the main design filename inside the zip.');
            data.append('model-zip-entrypoint', entrypoint);
        }
        upload.disabled = true;
        models.disabled = true;
        showNotification(`Uploading model <em>${file.name}</em>. Do not reload the page.`);
        try {
            const resp = await fetch('/api/models', { method: 'POST', body: data });
            if (!resp.ok) throw new Error(await resp.text());
            const model = await resp.json();
            setupModelSelection(viewer, model.urn);
        } catch (err) {
            alert(`Could not upload model ${file.name}. See the console for more details.`);
            console.error(err);
        } finally {
            clearNotification();
            upload.disabled = false;
            models.disabled = false;
            input.value = '';
        }
    };
}

// ── Design Automation controls ────────────────────────────────────────────────
function setupDaControls(viewer) {
    // Generate PDF — collect markups → ImportMarkups → PlotToPDF
    const genPdfBtn = document.getElementById('generate-pdf');
    genPdfBtn.onclick = async () => {
        const markups = getMarkups();
        if (markups.length === 0) {
            alert('No markups collected. Draw rectangle markups and click each to select it.');
            return;
        }
        const urn = document.getElementById('models').value;
        if (!urn) {
            alert('No model selected.');
            return;
        }

        genPdfBtn.disabled = true;
        showNotification(`Submitting ${markups.length} markup(s) to Design Automation...`);
        try {
            // Submit workitem chain
            const submitResp = await fetch('/api/da/workitems', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ urn, markups })
            });
            if (!submitResp.ok) throw new Error(await submitResp.text());
            const { jobId } = await submitResp.json();

            // Poll until done
            const pdfUrl = await pollDaJob(jobId);
            clearNotification();

            // Show download link
            const overlay = document.getElementById('overlay');
            overlay.innerHTML = `
                <div class="notification">
                    PDF ready!&nbsp;
                    <a href="${pdfUrl}" target="_blank" download>Download PDF</a>
                    &nbsp;&nbsp;
                    <button onclick="document.getElementById('overlay').style.display='none'">
                        Close
                    </button>
                </div>`;
            overlay.style.display = 'flex';
            clearMarkups(); // Reset for next round
        } catch (err) {
            alert(`Generate PDF failed: ${err.message}`);
            console.error(err);
            clearNotification();
        } finally {
            genPdfBtn.disabled = false;
        }
    };
}

async function pollDaJob(jobId) {
    return new Promise((resolve, reject) => {
        const STATUS_LABELS = {
            importing: 'Importing markups into DWG...',
            plotting:  'Plotting DWG to PDF via Design Automation...'
        };
        const interval = setInterval(async () => {
            try {
                const resp = await fetch(`/api/da/workitems/${jobId}`);
                if (!resp.ok) throw new Error(await resp.text());
                const data = await resp.json();
                const { status, pdfUrl, reportUrl } = data;

                if (STATUS_LABELS[status]) {
                    showNotification(STATUS_LABELS[status]);
                } else if (status === 'success') {
                    clearInterval(interval);
                    resolve(pdfUrl);
                } else if (status === 'failed') {
                    clearInterval(interval);
                    const report = reportUrl ? ` <a href="${reportUrl}" target="_blank">View report</a>` : '';
                    const msg    = data.error ?? 'DA job failed.';
                    reject(new Error(msg + (reportUrl ? ` Report: ${reportUrl}` : '')));
                    // Also surface in notification with clickable link
                    showNotification(`❌ ${msg}${report}`);
                }
            } catch (err) {
                clearInterval(interval);
                reject(err);
            }
        }, 5000);
    });
}

// ── Notification helpers ──────────────────────────────────────────────────────
function showNotification(message) {
    const overlay = document.getElementById('overlay');
    overlay.innerHTML = `<div class="notification">${message}</div>`;
    overlay.style.display = 'flex';
}

function clearNotification() {
    const overlay = document.getElementById('overlay');
    overlay.innerHTML = '';
    overlay.style.display = 'none';
}
