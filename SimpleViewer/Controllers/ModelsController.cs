using Autodesk.Forge.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using SimpleViewer.Models;
namespace SimpleViewer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ModelsController(APS aps) : ControllerBase
    {
        public record BucketObject(string name, string urn, long size);
        private readonly APS _aps = aps;

        [HttpGet("bucket")]
        public IActionResult GetBucket() => Ok(new { name = _aps.BucketName });

        [HttpDelete("bucket")]
        public async Task<IActionResult> ClearBucket()
        {
            await _aps.ClearBucket();
            return Ok(new { message = "Bucket cleared." });
        }

        [HttpGet()]
        public async Task<IEnumerable<BucketObject>> GetModels()
        {
            var objects = await _aps.GetObjects();
            return from o in objects
                   select new BucketObject(o.ObjectKey, APS.Base64Encode(o.ObjectId), Convert.ToInt64(o.Size ?? 0));
        }
        [HttpPost("{urn}/translate")]
        public async Task<IActionResult> StartTranslation(string urn, [FromQuery] bool pdf = true)
        {
            var padding  = (4 - urn.Length % 4) % 4;
            var objectId = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(urn + new string('=', padding)));
            var job = await _aps.TranslateModel(objectId, string.Empty, usePdfPipeline: pdf);
            return Ok(new { urn = job.Urn });
        }

        [HttpGet("{urn}/status")]
        public async Task<TranslationStatus> GetModelStatus(string urn)
        {
            try
            {
                var status = await _aps.GetTranslationStatus(urn);
                return status;
            }
            catch (ApiException ex)
            {
                if (ex.ErrorCode == 404)
                    return new TranslationStatus("n/a", "", new List<string>());
                else
                    throw;
            }
        }
        public class UploadModelForm
        {
            [FromForm(Name = "model-zip-entrypoint")]
            public string? Entrypoint { get; set; }
            [FromForm(Name = "model-file")]
            public IFormFile? File { get; set; }
        }
        [HttpPost()]
        public async Task<BucketObject> UploadAndTranslateModel([FromForm] UploadModelForm form, [FromQuery] bool pdf = true)
        {
            if(form.File == null)
                throw new Exception("No file uploaded");
            using var stream = new MemoryStream();
            await form.File.CopyToAsync(stream);
            stream.Position = 0;
            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var objectName = $"in_{ts}_{form.File.FileName}";
            var obj = await _aps.UploadModel(objectName, stream);
            var job = await _aps.TranslateModel(obj.ObjectId, form.Entrypoint ?? string.Empty, usePdfPipeline: pdf);
            return new BucketObject(obj.ObjectKey, job.Urn, Convert.ToInt64(obj.Size ?? 0));
        }
    }
}
