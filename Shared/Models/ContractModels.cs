using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CzurWpfDemo.Models;

public class ContractDetailsRequest
{
    public string? Search { get; set; }
    public List<string>? Date { get; set; }
}

public class ContractDetailsResponse
{
    public bool Status { get; set; }
    public ContractDetailsResult? Resoult { get; set; }
}

public class ContractDetailsResult
{
    public List<ContractItem>? Data { get; set; }
    public PaginationMeta? Meta { get; set; }
}

public class ContractItem
{
    public int Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("GUID")]
    public string Guid { get; set; } = string.Empty;

    public string? DateOfBirth { get; set; }
    public string Adres { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string TelNumberSms { get; set; } = string.Empty;
    public string TelNumber { get; set; } = string.Empty;
    public string? RegisterDate { get; set; }
    public string? Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ResponsibleWorker { get; set; } = string.Empty;
    public List<ContractDetailEntry>? ConstantDetails { get; set; }
    public List<ContractDetailEntry>? Details { get; set; }

    // UI uchun formatlangan qiymatlar
    [JsonIgnore]
    public string DateFormatted => DateTime.TryParse(Date, out var d) ? d.ToString("dd.MM.yyyy HH:mm") : "";

    [JsonIgnore]
    public string BirthDateFormatted => DateTime.TryParse(DateOfBirth, out var d) ? d.ToString("dd.MM.yyyy") : "";
}

public class PaginationMeta
{
    public int CurrentPage { get; set; }
    public int LastPage { get; set; }
    public int PerPage { get; set; }
    public int Total { get; set; }
    public int? From { get; set; }
    public int? To { get; set; }
}
