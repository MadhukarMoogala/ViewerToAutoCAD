using Autodesk.Forge;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SimpleViewer.Models
{
    public record WorkitemStatus(
        string Status,
        string? ReportUrl = null,
        string? PdfUrl = null);

    public partial class APS
    {
        private const string DA_BASE  = "https://developer.api.autodesk.com/da/us-east/v3";
        private const string OSS_V2   = "https://developer.api.autodesk.com/oss/v2";
        private const string DA_ENGINE = "Autodesk.AutoCAD+26_0";

        private Token? _daTokenCache;

        private async Task<Token> GetDaToken()
        {
            if (_daTokenCache == null || _daTokenCache.ExpiresAt < DateTime.UtcNow)
                _daTokenCache = await GetToken([
                    Scope.CodeAll,
                    Scope.BucketCreate, Scope.BucketRead,
                    Scope.DataRead, Scope.DataWrite, Scope.DataCreate
                ]);
            return _daTokenCache;
        }

        private HttpClient DaHttp(string accessToken)
        {
            var c = new HttpClient();
            c.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            return c;
        }

        // ── Nickname ──────────────────────────────────────────────────────────────
        public async Task<string> GetNickname()
        {
            var token = await GetDaToken();
            using var http = DaHttp(token.AccessToken);
            var resp = await http.GetAsync($"{DA_BASE}/forgeapps/me");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadAsStringAsync()).Trim('"');
        }

        // ── Bundle deployment ─────────────────────────────────────────────────────
        public async Task DeployBundle(Stream bundleZip)
        {
            var token = await GetDaToken();
            using var http = DaHttp(token.AccessToken);

            var create = new { id = "TransformPoint", engine = DA_ENGINE,
                               description = "Import APS Viewer markups into DWG" };
            var update = new { engine = DA_ENGINE,
                               description = "Import APS Viewer markups into DWG" };

            var resp = await http.PostAsync($"{DA_BASE}/appbundles", Json(create));
            if (resp.StatusCode == HttpStatusCode.Conflict)
                resp = await http.PostAsync($"{DA_BASE}/appbundles/TransformPoint/versions",
                                            Json(update));
            resp.EnsureSuccessStatusCode();

            var result      = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            var version     = result.GetProperty("version").GetInt32();
            var uploadParams = result.GetProperty("uploadParameters");
            var endpointUrl = uploadParams.GetProperty("endpointURL").GetString()!;

            // Multipart POST to AWS S3
            using var form = new MultipartFormDataContent();
            foreach (var prop in uploadParams.GetProperty("formData").EnumerateObject())
                form.Add(new StringContent(prop.Value.GetString()!), prop.Name);
            bundleZip.Position = 0;
            form.Add(new StreamContent(bundleZip), "file", "TransformPoint.bundle.zip");
            using var s3 = new HttpClient();
            (await s3.PostAsync(endpointUrl, form)).EnsureSuccessStatusCode();

            await UpsertAlias(http, "appbundles/TransformPoint", version);
        }

        // ── Activities ────────────────────────────────────────────────────────────
        public async Task EnsureImportMarkupsActivity()
        {
            var token    = await GetDaToken();
            var nickname = await GetNickname();
            using var http = DaHttp(token.AccessToken);

            var def = new
            {
                id          = "ImportMarkups",
                engine      = DA_ENGINE,
                appbundles  = new[] { $"{nickname}.TransformPoint+dev" },
                commandLine = new[]
                {
                    "$(engine.path)\\accoreconsole.exe" +
                    " /i \"$(args[inputDwg].path)\"" +
                    " /al \"$(appbundles[TransformPoint].path)\"" +
                    " /s \"$(settings[script].path)\""
                },
                settings = new Dictionary<string, object>
                {
                    ["script"] = new { value = "IMPORTMARKUPS\n_QUIT Y\n" }
                },
                parameters = new
                {
                    inputDwg   = new { verb = "get", required = true },
                    markupJson = new { verb = "get", required = true, localName = "markups.json" },
                    outputDwg  = new { verb = "put", required = true, localName = "output.dwg" }
                }
            };

            await UpsertActivity(http, "ImportMarkups", def);
        }

        public async Task EnsurePlotToPdfActivity()
        {
            var token = await GetDaToken();
            using var http = DaHttp(token.AccessToken);

            // Script mirrors Autodesk's own AutoCAD.PlotToPDF+prod system activity.
            // _tilemode 0 → paper space; -export _pdf _all → all layouts → result.pdf
            var def = new
            {
                id          = "PlotToPDF",
                engine      = DA_ENGINE,
                commandLine = new[]
                {
                    "$(engine.path)\\accoreconsole.exe" +
                    " /i \"$(args[inputDwg].path)\"" +
                    " /s \"$(settings[script].path)\"" +
                    " /suppressGraphics"
                },
                settings = new Dictionary<string, object>
                {
                    ["script"] = new { value = "_tilemode 0 -export _pdf _all result.pdf\n" }
                },
                parameters = new
                {
                    inputDwg  = new { verb = "get", required = true },
                    outputPdf = new { verb = "put", required = true, localName = "result.pdf" }
                }
            };

            await UpsertActivity(http, "PlotToPDF", def);
        }

        // ── Workitem submission ───────────────────────────────────────────────────
        public async Task<(string WorkitemId, string OutputKey)> SubmitImportMarkupsWorkitem(
            string urn, JsonElement markups)
        {
            var token    = await GetDaToken();
            var nickname = await GetNickname();

            // Decode viewer URN → OSS objectId
            var padding  = (4 - urn.Length % 4) % 4;
            var objectId = Encoding.UTF8.GetString(
                Convert.FromBase64String(urn + new string('=', padding)));

            // Upload markup JSON to bucket
            var markupJson = JsonSerializer.Serialize(new { markups });
            var markupKey  = $"markups_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            await UploadModel(markupKey, new MemoryStream(Encoding.UTF8.GetBytes(markupJson)));

            // Signed URLs
            var inputDwgUrl  = await GetSignedUrlByObjectId(objectId, "read");
            var markupUrl    = await GetSignedUrl(_bucket, markupKey, "read");
            var outputKey    = $"output_{DateTime.UtcNow:yyyyMMddHHmmss}.dwg";
            var outputUrl    = await GetSignedUrl(_bucket, outputKey, "write");

            var body = new
            {
                activityId = $"{nickname}.ImportMarkups+dev",
                arguments  = new
                {
                    inputDwg   = new { url = inputDwgUrl },
                    markupJson = new { url = markupUrl },
                    outputDwg  = new { verb = "put", url = outputUrl }
                }
            };

            using var http = DaHttp(token.AccessToken);
            var resp = await http.PostAsync($"{DA_BASE}/workitems", Json(body));
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"DA workitem POST {(int)resp.StatusCode}: {errBody}");
            }
            var id = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetString()!;
            return (id, outputKey);
        }

        public async Task<(string WorkitemId, string PdfKey)> SubmitPlotToPdfWorkitem(
            string outputDwgKey)
        {
            var token    = await GetDaToken();
            var nickname = await GetNickname();

            var inputUrl = await GetSignedUrl(_bucket, outputDwgKey, "read");
            var pdfKey   = Path.ChangeExtension(outputDwgKey, ".pdf");
            var pdfUrl   = await GetSignedUrl(_bucket, pdfKey, "write");

            var body = new
            {
                activityId = $"{nickname}.PlotToPDF+dev",
                arguments  = new
                {
                    inputDwg  = new { url = inputUrl },
                    outputPdf = new { verb = "put", url = pdfUrl }
                }
            };

            using var http = DaHttp(token.AccessToken);
            var resp = await http.PostAsync($"{DA_BASE}/workitems", Json(body));
            resp.EnsureSuccessStatusCode();
            var id = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetString()!;
            return (id, pdfKey);
        }

        public async Task<WorkitemStatus> GetWorkitemStatus(string workitemId)
        {
            var token = await GetDaToken();
            using var http = DaHttp(token.AccessToken);
            var resp = await http.GetAsync($"{DA_BASE}/workitems/{workitemId}");
            resp.EnsureSuccessStatusCode();
            var json      = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            var status    = json.GetProperty("status").GetString()!;
            var reportUrl = json.TryGetProperty("reportUrl", out var ru) ? ru.GetString() : null;
            return new WorkitemStatus(status, ReportUrl: reportUrl);
        }

        public async Task<string> GetPdfDownloadUrl(string pdfKey)
            => await GetSignedUrl(_bucket, pdfKey, "read");

        // Deletes ALL DA resources (bundles, activities, aliases) for this app.
        // Equivalent to Reset-DA in APS-Common.ps1.
        public async Task CleanupAllDaResources()
        {
            var token = await GetDaToken();
            using var http = DaHttp(token.AccessToken);
            var resp = await http.DeleteAsync($"{DA_BASE}/forgeapps/me");
            // 404 = already clean; both are acceptable
            if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                resp.EnsureSuccessStatusCode();
        }

        // ── Private helpers ───────────────────────────────────────────────────────
        private async Task UpsertActivity(HttpClient http, string activityId, object def)
        {
            var resp = await http.PostAsync($"{DA_BASE}/activities", Json(def));
            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                // Strip id from body for version update
                var json    = JsonDocument.Parse(JsonSerializer.Serialize(def)).RootElement;
                var noId    = json.EnumerateObject()
                                  .Where(p => p.Name != "id")
                                  .ToDictionary(p => p.Name, p => (object)p.Value);
                resp = await http.PostAsync(
                    $"{DA_BASE}/activities/{activityId}/versions", Json(noId));
            }
            resp.EnsureSuccessStatusCode();
            var ver = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("version").GetInt32();
            await UpsertAlias(http, $"activities/{activityId}", ver);
        }

        private async Task UpsertAlias(HttpClient http, string resource, int version)
        {
            var resp = await http.PostAsync(
                $"{DA_BASE}/{resource}/aliases", Json(new { id = "dev", version }));
            if (resp.StatusCode == HttpStatusCode.Conflict)
                resp = await http.PatchAsync(
                    $"{DA_BASE}/{resource}/aliases/dev", Json(new { version }));
            resp.EnsureSuccessStatusCode();
        }

        // objectId form: "urn:adsk.objects:os.object:bucket/key"
        private async Task<string> GetSignedUrlByObjectId(string objectId, string access)
        {
            var path  = objectId.Replace("urn:adsk.objects:os.object:", "");
            var slash = path.IndexOf('/');
            return await GetSignedUrl(path[..slash], path[(slash + 1)..], access);
        }

        private async Task<string> GetSignedUrl(string bucketKey, string objectKey, string access)
        {
            var token      = await GetInternalToken();
            var encodedKey = Uri.EscapeDataString(objectKey);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var resp = await http.PostAsync(
                $"{OSS_V2}/buckets/{bucketKey}/objects/{encodedKey}/signed" +
                $"?access={access}&minutesExpiration=60",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("signedUrl").GetString()!;
        }

        private static StringContent Json(object o)
            => new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    }
}
