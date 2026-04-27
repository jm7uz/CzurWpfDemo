using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CzurWpfDemo.Models;

public class BranchRequest
{
    public string? Search { get; set; }
}

public class BranchResponse
{
    public bool Status { get; set; }
    public BranchResult? Resoult { get; set; }
}

public class BranchResult
{
    public List<BranchItem>? Data { get; set; }
    public BranchLinks? Links { get; set; }
    public PaginationMeta? Meta { get; set; }
}

public class BranchItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? StateId { get; set; }
    public string StateName { get; set; } = string.Empty;
    public int? RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    // API "constantDocumentDetail" (camelCase) qaytaradi — JsonPropertyName bilan aniq ko'rsatilgan
    [System.Text.Json.Serialization.JsonPropertyName("constantDocumentDetail")]
    public List<BranchConstantDocument>? ConstantDocumentDetail { get; set; }
}

public class BranchConstantDocument
{
    public int Id { get; set; }
    public int ContractDocumentId { get; set; }
    public string PunktName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Svg { get; set; } = string.Empty;
    public string? File { get; set; }
    public string? PhotoCount { get; set; }
    public string ResponsibleWorker { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}

public class BranchLinks
{
    public string? First { get; set; }
    public string? Last { get; set; }
    public string? Prev { get; set; }
    public string? Next { get; set; }
}
