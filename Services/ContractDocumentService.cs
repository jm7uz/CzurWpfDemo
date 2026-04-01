using System.Collections.Generic;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class ContractDocumentService
{
    public static async Task<List<ContractDocumentType>?> GetAllAsync()
    {
        var response = await ApiService.GetAsync<ContractDocumentAllResponse>("contract-document/all");
        return response?.Resoult?.Data;
    }
}
