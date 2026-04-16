using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CzurWpfDemo.Services;

public class ApiService
{
    private static readonly HttpClient _client = new();
    // dotnet publish CzurWpfDemo.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ./publish/CzurWpfDemo // .exe yaratish uchun
    //public const string BaseUrl = "http://10.100.104.104:9505/api/"; // local
    public const string BaseUrl = "http://sud-upload-file.garant.uz/api/";// production

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static void SetToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task<T?> GetAsync<T>(string endpoint)
    {
        var response = await _client.GetAsync(BaseUrl + endpoint);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    public static async Task<T?> PostAsync<T>(string endpoint, object body)
    {
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(BaseUrl + endpoint, content);
        var responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<T>(responseJson, _jsonOptions);
    }

    public static async Task<T?> PutAsync<T>(string endpoint, object body)
    {
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PutAsync(BaseUrl + endpoint, content);
        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseJson, _jsonOptions);
    }

    public static async Task<bool> PostMultipartAsync(string endpoint, MultipartFormDataContent content)
    {
        try
        {
            var response = await _client.PostAsync(BaseUrl + endpoint, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<T?> PostMultipartAsync<T>(string endpoint, MultipartFormDataContent content)
    {
        try
        {
            var response = await _client.PostAsync(BaseUrl + endpoint, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<T>(responseJson, _jsonOptions);
        }
        catch
        {
            return default;
        }
    }
}
