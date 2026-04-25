namespace CzurWpfDemo.Models;

public class UploadResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UploadResult? Resoult { get; set; }
}

public class UploadResult
{
    public string Url { get; set; } = string.Empty;
}

public class UploadBase64Request
{
    public string File { get; set; } = string.Empty;
    public string? ContractId { get; set; }
    public string? ClientName { get; set; }
}
