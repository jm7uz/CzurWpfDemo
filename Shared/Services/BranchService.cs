using System.Collections.Generic;
using System.Threading.Tasks;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class BranchService
{
    /// <summary>
    /// Barcha filiallarni olish (paginatsiya bilan)
    /// </summary>
    /// <param name="search">Qidiruv matni (ixtiyoriy)</param>
    /// <param name="perPage">Har bir sahifada nechta element (default: 100)</param>
    /// <param name="page">Sahifa raqami (default: 1)</param>
    /// <returns>Filiallar ro'yxati</returns>
    public static async Task<BranchResponse?> GetAllAsync(string? search = null, int perPage = 100, int page = 1)
    {
        var request = new BranchRequest
        {
            Search = search
        };

        var endpoint = $"branchs?perPage={perPage}&page={page}";
        return await ApiService.PostAsync<BranchResponse>(endpoint, request);
    }

    /// <summary>
    /// Faqat filiallar ro'yxatini olish (paginatsiya ma'lumotlarisiz)
    /// </summary>
    public static async Task<List<BranchItem>?> GetBranchListAsync(string? search = null)
    {
        var response = await GetAllAsync(search, perPage: 100, page: 1);
        return response?.Resoult?.Data;
    }

    /// <summary>
    /// Barcha filiallarni yuklash (barcha sahifalardan)
    /// </summary>
    /// <param name="search">Qidiruv matni</param>
    /// <returns>Barcha filiallar ro'yxati</returns>
    public static async Task<List<BranchItem>> GetAllBranchesAsync(string? search = null)
    {
        var allBranches = new List<BranchItem>();
        int currentPage = 1;
        int lastPage = 1;

        do
        {
            var response = await GetAllAsync(search, perPage: 100, page: currentPage);

            if (response == null || !response.Status || response.Resoult?.Data == null)
                break;

            allBranches.AddRange(response.Resoult.Data);

            lastPage = response.Resoult.Meta?.LastPage ?? 1;
            currentPage++;

        } while (currentPage <= lastPage);

        return allBranches;
    }
}
