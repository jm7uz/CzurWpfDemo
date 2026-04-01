using System.Collections.Generic;

namespace CzurWpfDemo.Models;

public class ReportRequest
{
    public string? Search { get; set; }
    public int? UserId { get; set; }
    public List<string>? Date { get; set; }
}

public class ReportResponse
{
    public bool Status { get; set; }
    public List<ReportItem>? Resoult { get; set; }
}

public class ReportItem
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserPhone { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public int Contracts { get; set; }
}
