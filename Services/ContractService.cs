using System.Collections.Generic;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class ContractService
{
    public static async Task<ContractDetailsResponse?> GetDetailsAsync(
        int userId, string? search, string? dateFrom, string? dateTo, int page = 1, int perPage = 30)
    {
        var request = new ContractDetailsRequest
        {
            Search = search,
            Date = (dateFrom != null && dateTo != null)
                ? new List<string> { dateFrom, dateTo }
                : null
        };

        var endpoint = $"report/details/{userId}?column=id&direction=DESC&perPage={perPage}&page={page}";
        return await ApiService.PostAsync<ContractDetailsResponse>(endpoint, request);
    }
}
