using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class UserService
{
    public static async Task<List<UserItem>> GetAllUsersAsync(string? search = null)
    {
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(search))
            headers["Search"] = search;
        var response = await ApiService.GetAsync<UserResponse>("users", headers);
        return response?.Resoult ?? [];
    }
}
