using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CzurWpfDemo.Services;

public class ApiService
{
    private static readonly HttpClient _client = new();
    public const string BaseUrl = "http://10.100.104.104:9505/api/";

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
}
