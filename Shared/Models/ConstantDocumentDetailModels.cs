namespace CzurWpfDemo.Models;

public class ConstantDocumentDetailStoreRequest
{
    public long DocumentNumber { get; set; }
    public int ContractDocumentId { get; set; }
    public string File { get; set; } = string.Empty;
    public int PhotoCount { get; set; }
}

public class ConstantDocumentDetailStoreResponse
{
    public bool Status { get; set; }
    public string? Message { get; set; }
    public ContractDetailEntryUpdate? Resoult { get; set; }
}

public class ConstantDocumentDetailUpdateResponse
{
    public bool Status { get; set; }
    public ContractDetailEntryUpdate? Resoult { get; set; }
}

public class ContractDetailEntryUpdate
{
    public int Id { get; set; }
    public int ContractDocumentId { get; set; }
    public string PunktName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? File { get; set; }
    public int PhotoCount { get; set; }
    public string ResponsibleWorker { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
}
