using System.Collections.Generic;

namespace CzurWpfDemo.Models;

public class ContractDocumentAllResponse
{
    public bool Status { get; set; }
    public ContractDocumentAllResult? Resoult { get; set; }
}

public class ContractDocumentAllResult
{
    public List<ContractDocumentType>? Data { get; set; }
    public PaginationMeta? Meta { get; set; }
}

public class ContractDocumentType
{
    public int Id { get; set; }
    public string PunktName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Svg { get; set; } = string.Empty;
    public string ResponsibleWorker { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}

public class ContractDetailEntry
{
    public int Id { get; set; }
    public long DocumentNumber { get; set; }
    public int ContractDocumentId { get; set; }
    public string PunktName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Svg { get; set; } = string.Empty;
    public string? File { get; set; }
    public string? PhotoCount { get; set; }
    public string ResponsibleWorker { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}
