using System.Text.Json;

namespace System.Web.Script.Serialization
{
    public sealed class JavaScriptSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        public string Serialize(object value) { return JsonSerializer.Serialize(value, Options); }
        public T Deserialize<T>(string input) { return JsonSerializer.Deserialize<T>(input, Options); }
    }
}
