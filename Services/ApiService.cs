using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CzurWpfDemo.Services;

public class ApiService
{
    private static readonly HttpClient _client = new();
    //public const string BaseUrl = "http://10.100.104.104:9555/api/";
    public const string BaseUrl = "http://sud-upload-file.garant.uz/api/";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    // API javoblari logi — %LOCALAPPDATA%\CzurWpfDemo\api.log (Program Files'ga yozib bo'lmaydi; auth/ javoblari token sababli yozilmaydi)
    public static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CzurWpfDemo", "api.log");

    internal static void LogResponse(string method, string endpoint, int code, string body)
    {
        if (endpoint.StartsWith("auth/")) return;
        var snippet = body.Length > 50000 ? body[..50000] + "..." : body;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
            System.IO.File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {method} {endpoint} → {code}: {snippet}{Environment.NewLine}");
        }
        catch { }
    }

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

    public static async Task<T?> GetAsync<T>(string endpoint, Dictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + endpoint);
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
        var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    public static async Task<T?> PostAsync<T>(string endpoint, object body)
    {
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(BaseUrl + endpoint, content);
        var responseJson = await response.Content.ReadAsStringAsync();
        LogResponse("POST", endpoint + " | body: " + json, (int)response.StatusCode, responseJson);

        try
        {
            return JsonSerializer.Deserialize<T>(responseJson, _jsonOptions);
        }
        catch (JsonException)
        {
            // Backend kutilmagan javob qaytardi — haqiqiy javobni xatolik matnida ko'rsatamiz
            var snippet = responseJson.Length > 300 ? responseJson[..300] + "..." : responseJson;
            throw new InvalidOperationException(
                $"{endpoint} — HTTP {(int)response.StatusCode}, kutilmagan javob: {snippet}");
        }
    }

    public static async Task<T?> PutAsync<T>(string endpoint, object body)
    {
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PutAsync(BaseUrl + endpoint, content);
        var responseJson = await response.Content.ReadAsStringAsync();
        LogResponse("PUT", endpoint + " | body: " + json, (int)response.StatusCode, responseJson);

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
