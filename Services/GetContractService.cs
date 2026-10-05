using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class GetContractService
{
    // 1C server (Flutter'dagi baseUrlApp bilan bir xil)
    private const string BaseUrl1C = "http://api.garant.uz:8081/Garant/hs/IntegrationWithMobileApplications/Basic/"; // global
    // private const string BaseUrl1C = "http://10.100.109.120:8080/Garant4/hs/IntegrationWithMobileApplications/Basic/"; // local

    // GetTicket uchun token
    private const string TicketToken = "m4MC0ck4Ku7Ul4L2hHy9Yj3Jx9Xi3IQq6tT7l4Lw";

    private static readonly HttpClient _client1C = CreateClient1C();

    private static HttpClient CreateClient1C()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl1C) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "TW9iaWxlQXBwOk1vYmlsZUFwcDE=");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // 1C dan har safar yangi ticket olish — javob: { "massage": { "ticket": "..." } }
    public static async Task<string> GetTicketAsync()
    {
        var body     = new StringContent(JsonSerializer.Serialize(new { token = TicketToken }), Encoding.UTF8, "application/json");
        var response = await _client1C.PostAsync("GetTicket/", body);
        var json     = await response.Content.ReadAsStringAsync();
        ApiService.LogResponse("POST", "1C GetTicket", (int)response.StatusCode, json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var ticket = doc.RootElement.GetProperty("massage").GetProperty("ticket").GetString();
            if (!string.IsNullOrEmpty(ticket)) return ticket;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }

        var snippet = json.Length > 300 ? json[..300] + "..." : json;
        throw new InvalidOperationException($"1C GetTicket — HTTP {(int)response.StatusCode}, ticket olinmadi: {snippet}");
    }

    // Barcode raqami bo'yicha shartnomani tekshirish — Laravel get/contract (ishlagan versiyadagidek).
    // Laravel 1C dan shartnomani olib o'z bazasiga yozadi — shundan keyin get/all uni topadi va contract_id to'g'ri bo'ladi.
    public static async Task<GetContractResponse?> ValidateAsync(string documentNumber)
        => await ApiService.PostAsync<GetContractResponse>(
            "get/contract", new { document_number = documentNumber });

    // 1C GetContractData ni to'g'ridan-to'g'ri chaqirish (Flutter'dagidek) — shartnoma Laravel bazasiga yozilmaydi, hozir ishlatilmaydi
    // Javob: { "massage": { ...shartnoma... } } yoki { "massage": "Документ не найден" }
    public static async Task<GetContractResponse?> Validate1CAsync(string documentNumber)
    {
        var ticket = await GetTicketAsync();
        var payload = JsonSerializer.Serialize(new { ticket, document_number = documentNumber, status = 1 });
        var response = await _client1C.PostAsync("GetContractData/", new StringContent(payload, Encoding.UTF8, "application/json"));
        var json     = await response.Content.ReadAsStringAsync();
        ApiService.LogResponse("POST", "1C GetContractData | body: " + payload, (int)response.StatusCode, json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var massage = doc.RootElement.GetProperty("massage");
            if (massage.ValueKind == JsonValueKind.Object)
            {
                // 1C haqiqiy document_number qaytaradi (barcode'dan farq qiladi) — get/all shu raqam bilan qidiriladi
                var client = massage.TryGetProperty("Client", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
                return new GetContractResponse
                {
                    Status  = true,
                    Resoult = new GetContractDetail
                    {
                        DocumentNumber = Str(massage, "document_number") ?? documentNumber,
                        Branch         = Str(massage, "branch") ?? string.Empty,
                        Date           = Str(massage, "date"),
                        Name           = Str(client, "name") ?? string.Empty,
                        Guid           = Str(client, "GUID") ?? string.Empty,
                        DateOfBirth    = Str(client, "date_of_birth"),
                        Adres          = Str(client, "adres") ?? string.Empty,
                        DocType        = Str(client, "doc_type") ?? string.Empty,
                        TelNumberSms   = Str(client, "tel_number_sms") ?? string.Empty,
                        TelNumber      = Str(client, "tel_number") ?? string.Empty,
                        Pinfl          = Str(client, "pinfl")
                    }
                };
            }

            return new GetContractResponse { Status = false, Message = massage.ToString() };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            var snippet = json.Length > 300 ? json[..300] + "..." : json;
            throw new InvalidOperationException($"1C GetContractData — HTTP {(int)response.StatusCode}, kutilmagan javob: {snippet}");
        }
    }

    // JSON obyektdan string maydonni xavfsiz o'qish
    private static string? Str(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // 1C ma'lumotidan ContractItem yig'ish (Laravel get/all da yozuv bo'lmaganda)
    public static ContractItem ToContractItem(GetContractDetail d) => new()
    {
        DocumentNumber = d.DocumentNumber,
        Name           = d.Name,
        Guid           = d.Guid,
        DateOfBirth    = d.DateOfBirth,
        Adres          = d.Adres,
        DocType        = d.DocType,
        TelNumberSms   = d.TelNumberSms,
        TelNumber      = d.TelNumber,
        Date           = d.Date
    };

    // constant_details bilan to'liq shartnoma ma'lumotlarini olish
    public static async Task<ContractDetailsResponse?> SearchAllAsync(string documentNumber)
        => await ApiService.PostAsync<ContractDetailsResponse>(
            "get/all?perPage=100&page=1", new GetAllRequest { Search = documentNumber });
}
