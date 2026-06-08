using Autodesk.Forge.Client;
using Autodesk.Forge.Model;
using Autodesk.Forge;
namespace SimpleViewer.Models
{
    public partial class APS
    {
        private async Task EnsureBucketExists(string bucketKey)
        {
            var token = await GetInternalToken();
            var api = new BucketsApi();
            api.Configuration.AccessToken = token.AccessToken;
            try
            {
                await api.GetBucketDetailsAsync(bucketKey);
            }
            catch (ApiException e)
            {
                if (e.ErrorCode == 404)
                {
                    _logger.LogInformation("[OSS] Bucket {Key} not found — creating", bucketKey);
                    await api.CreateBucketAsync(new PostBucketsPayload(bucketKey, null, PostBucketsPayload.PolicyKeyEnum.Persistent), "US");
                    _logger.LogInformation("[OSS] Bucket {Key} created", bucketKey);
                }
                else
                {
                    throw;
                }
            }
        }
        public async Task<ObjectDetails> UploadModel(string objectName, Stream content)
        {
            await EnsureBucketExists(_bucket);
            _logger.LogInformation("[OSS] Uploading {Name} ({Size} bytes)", objectName, content.Length);
            var token = await GetInternalToken();
            var api = new ObjectsApi();
            api.Configuration.AccessToken = token.AccessToken;
            var results = await api.uploadResources(_bucket, new List<UploadItemDesc> {
            new UploadItemDesc(objectName, content)
        });
            if (results[0].Error)
            {
                throw new Exception(results[0].completed.ToString());
            }
            else
            {
                var json = results[0].completed.ToJson();
                ObjectDetails obj = json.ToObject<ObjectDetails>();
                var size = Convert.ToInt64(obj.Size ?? 0);
                _logger.LogInformation("[OSS] Uploaded {Name} → {Size} bytes", obj.ObjectKey, size);
                return obj;
            }
        }
        public async Task ClearBucket()
        {
            var objects = await GetObjects();
            _logger.LogInformation("[OSS] Clearing bucket — {Count} object(s)", objects.Count());
            var token = await GetInternalToken();
            var api = new ObjectsApi();
            api.Configuration.AccessToken = token.AccessToken;
            foreach (var obj in objects)
            {
                _logger.LogInformation("[OSS] Deleting {Key}", (string)obj.ObjectKey);
                await api.DeleteObjectAsync(_bucket, obj.ObjectKey);
            }
            _logger.LogInformation("[OSS] Bucket cleared");
        }

        public async Task<IEnumerable<ObjectDetails>> GetObjects()
        {
            const int PageSize = 64;
            await EnsureBucketExists(_bucket);
            var token = await GetInternalToken();
            var api = new ObjectsApi();
            api.Configuration.AccessToken = token.AccessToken;
            var results = new List<ObjectDetails>();
            var response = (await api.GetObjectsAsync(_bucket, PageSize)).ToObject<BucketObjects>();
            results.AddRange(response.Items);
            while (!string.IsNullOrEmpty(response.Next))
            {
                var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(response.Next).Query);
                response = (await api.GetObjectsAsync(_bucket, PageSize, null, queryParams["startAt"])).ToObject<BucketObjects>();
                results.AddRange(response.Items);
            }
            _logger.LogInformation("[OSS] Listed {Count} object(s) in {Bucket}", results.Count, _bucket);
            return results;
        }
    }
}
