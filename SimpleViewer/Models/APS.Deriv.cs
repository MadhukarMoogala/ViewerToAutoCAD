using Autodesk.Forge.Model;
using Autodesk.Forge;
using System.Transactions;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using System.Text;
namespace SimpleViewer.Models
{
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
    public record TranslationStatus(string Status, string Progress, IEnumerable<string>? Messages);
    public partial class APS
    {
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes).TrimEnd('=');
        }
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
        public async Task<TranslationStatus> GetTranslationStatus(string urn)
        {
            var token = await GetInternalToken();
            var api = new DerivativesApi();
            api.Configuration.AccessToken = token.AccessToken;
            var json = (await api.GetManifestAsync(urn)).ToJson();
            var messages = new List<string>();
            foreach (var message in json.SelectTokens("$.derivatives[*].messages[?(@.type == 'error')].message"))
            {
                if (message.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    messages.Add((string)message);
            }
            foreach (var message in json.SelectTokens("$.derivatives[*].children[*].messages[?(@.type == 'error')].message"))
            {
                if (message.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    messages.Add((string)message);
            }
            return new TranslationStatus((string)json["status"], (string)json["progress"], messages);
        }
    }
}
