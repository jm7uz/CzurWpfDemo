using System.Text.Json.Serialization;

namespace CzurWpfDemo.Models;

public class GetContractRequest
{
    public string Ticket { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public int Status { get; set; } = 1;
}

public class GetContractResponse
{
    public bool Status { get; set; }
    public string? Message { get; set; }
    public ApiError? Error { get; set; }
    public GetContractDetail? Resoult { get; set; }
}

public class ApiError
{
    public string? Message { get; set; }
}

public class GetContractDetail
{
    public int Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("GUID")]
    public string Guid { get; set; } = string.Empty;

    public string? DateOfBirth { get; set; }
    public string Adres { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string TelNumberSms { get; set; } = string.Empty;
    public string TelNumber { get; set; } = string.Empty;
    public string? Pinfl { get; set; }
    public string ResponsibleWorker { get; set; } = string.Empty;
    public string? RegisterDate { get; set; }
    public string? Date { get; set; }
}

public class GetAllRequest
{
    public string? Search { get; set; }
}
