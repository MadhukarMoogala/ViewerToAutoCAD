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
        public record BucketObject(string name, string urn);
        private readonly APS _aps = aps;
        [HttpGet()]
        public async Task<IEnumerable<BucketObject>> GetModels()
        {
            var objects = await _aps.GetObjects();
            return from o in objects
                   select new BucketObject(o.ObjectKey, APS.Base64Encode(o.ObjectId));
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
        public async Task<BucketObject> UploadAndTranslateModel([FromForm] UploadModelForm form)
        {
            if(form.File == null)
                throw new Exception("No file uploaded");
            using var stream = new MemoryStream();
            await form.File.CopyToAsync(stream);
            stream.Position = 0;
            var obj = await _aps.UploadModel(form.File.FileName, stream);
            var job = await _aps.TranslateModel(obj.ObjectId, form.Entrypoint ?? string.Empty);
            return new BucketObject(obj.ObjectKey, job.Urn);
        }
    }
}
