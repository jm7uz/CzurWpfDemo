using System.Collections.Generic;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class ReportService
{
    public static async Task<List<ReportItem>?> GetByUserAsync(
        string? search, int? userId, string? dateFrom, string? dateTo)
    {
        var request = new ReportRequest
        {
            Search = search,
            UserId = userId,
            Date = (dateFrom != null && dateTo != null)
                ? new List<string> { dateFrom, dateTo }
                : null
        };

        var response = await ApiService.PostAsync<ReportResponse>("report/by/user", request);
        return response?.Resoult;
    }
}
