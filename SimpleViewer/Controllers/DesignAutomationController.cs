using Microsoft.AspNetCore.Mvc;
using SimpleViewer.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SimpleViewer.Controllers
{
    [Route("api/da")]
    [ApiController]
    public class DesignAutomationController(APS aps) : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, DaJob> _jobs = new();

        private record DaJob(
            string Wi1Id,
            string OutputKey,
            string? Wi2Id    = null,
            string? PdfKey   = null);

        // DELETE /api/da/cleanup
        // Wipes ALL DA resources (bundles, activities, aliases) for this APS app.
        // Use only during development to start fresh.
        [HttpDelete("cleanup")]
        public async Task<IActionResult> Cleanup()
        {
            await aps.CleanupAllDaResources();
            _jobs.Clear();
            return Ok(new { message = "All DA resources deleted. Run Setup DA to redeploy." });
        }

        // POST /api/da/setup  (form: bundleZip)
        // Run once: deploy TransformPoint bundle + register both activities.
        [HttpPost("setup")]
        public async Task<IActionResult> Setup(IFormFile bundleZip)
        {
            if (bundleZip == null || bundleZip.Length == 0)
                return BadRequest("bundleZip file is required.");

            await using var stream = bundleZip.OpenReadStream();
            await aps.DeployBundle(stream);
            await aps.EnsureImportMarkupsActivity();
            await aps.EnsurePlotToPdfActivity();
            return Ok(new { message = "Bundle and activities deployed." });
        }

        // POST /api/da/workitems
        // Body: { urn: string, markups: [...] }
        // Returns: { jobId: string }
        [HttpPost("workitems")]
        public async Task<IActionResult> SubmitWorkitem([FromBody] JsonElement body)
        {
            if (!body.TryGetProperty("urn", out var urnEl) ||
                !body.TryGetProperty("markups", out var markupsEl))
                return BadRequest("Body must contain 'urn' and 'markups'.");

            var urn = urnEl.GetString()!;
            var (wi1Id, outputKey) = await aps.SubmitImportMarkupsWorkitem(urn, markupsEl);
            _jobs[wi1Id] = new DaJob(wi1Id, outputKey);
            return Ok(new { jobId = wi1Id });
        }

        // GET /api/da/workitems/{jobId}
        // Polls wi1; when wi1 succeeds auto-submits wi2 (PlotToPDF);
        // when wi2 succeeds returns signed PDF download URL.
        [HttpGet("workitems/{jobId}")]
        public async Task<IActionResult> GetWorkitem(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return NotFound(new { error = "Job not found." });

            // Phase 1 — ImportMarkups workitem
            if (job.Wi2Id == null)
            {
                var wi1Status = await aps.GetWorkitemStatus(job.Wi1Id);

                if (wi1Status.Status is "pending" or "inprogress")
                    return Ok(new { status = "importing" });

                if (wi1Status.Status == "failed")
                    return Ok(new { status = "failed", reportUrl = wi1Status.ReportUrl });

                // wi1 succeeded — atomically claim the transition to plotting
                var pending = job with { Wi2Id = "pending" };
                if (!_jobs.TryUpdate(jobId, pending, job))
                    return Ok(new { status = "plotting" }); // another request already claimed it

                try
                {
                    var (wi2Id, pdfKey) = await aps.SubmitPlotToPdfWorkitem(job.OutputKey);
                    _jobs.TryUpdate(jobId, pending with { Wi2Id = wi2Id, PdfKey = pdfKey }, pending);
                    return Ok(new { status = "plotting" });
                }
                catch (Exception ex) when (ex.Message.Contains("404") ||
                                           ex.InnerException?.Message.Contains("404") == true)
                {
                    // output.dwg not found — IMPORTMARKUPS likely failed silently
                    // (bundle not deployed, or command error). Check report for details.
                    _jobs.TryUpdate(jobId, pending with { Wi2Id = "failed" }, pending);
                    return Ok(new
                    {
                        status    = "failed",
                        error     = $"output.dwg not found in bucket after ImportMarkups. " +
                                    $"Likely cause: DA Setup not run, or IMPORTMARKUPS command failed. " +
                                    $"Check the workitem report for details.",
                        reportUrl = wi1Status.ReportUrl
                    });
                }
            }

            // Phase 2 — PlotToPDF workitem
            if (job.Wi2Id == "pending")
                return Ok(new { status = "plotting" });

            var wi2Status = await aps.GetWorkitemStatus(job.Wi2Id);

            if (wi2Status.Status is "pending" or "inprogress")
                return Ok(new { status = "plotting" });

            if (wi2Status.Status == "failed")
                return Ok(new { status = "failed", reportUrl = wi2Status.ReportUrl });

            // Both workitems succeeded — return signed PDF download URL
            var pdfUrl = await aps.GetPdfDownloadUrl(job.PdfKey!);
            return Ok(new { status = "success", pdfUrl });
        }
    }
}
