using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class UserService
{
    public static async Task<UserResponse?> GetAllAsync(string? search, int perPage, int page)
    {
        var request = new UserRequest { Search = search };
        return await ApiService.PostAsync<UserResponse>($"users?perPage={perPage}&page={page}", request);
    }

    public static async Task<List<UserItem>> GetAllUsersAsync(string? search = null)
    {
        var result = new List<UserItem>();
        int page = 1;

        while (true)
        {
            var response = await GetAllAsync(search, 100, page);
            if (response?.Resoult?.Data is not { Count: > 0 } data) break;
            result.AddRange(data);
            int lastPage = response.Resoult.Meta?.LastPage ?? 1;
            if (page >= lastPage) break;
            page++;
        }

        return result;
    }
}
