using System.Collections.Generic;
using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class UserService
{
    // Foydalanuvchilar ro'yxatini olish (qidiruv + paginatsiya)
    public static async Task<UserListResponse?> GetAllAsync(
        string? search = null, int perPage = 50, int page = 1)
    {
        var endpoint = $"users?perPage={perPage}&page={page}";
        return await ApiService.PostAsync<UserListResponse>(endpoint, new UserListRequest { Search = search });
    }

    // Faqat ro'yxat (paginatsiyasiz)
    public static async Task<List<UserItem>?> GetListAsync(string? search = null)
    {
        var response = await GetAllAsync(search);
        return response?.Resoult?.Data;
    }

    // Yangi foydalanuvchi yaratish
    public static async Task<UserSingleResponse?> StoreAsync(UserStoreRequest request)
        => await ApiService.PostAsync<UserSingleResponse>("user", request);

    // Mavjud foydalanuvchini tahrirlash
    public static async Task<UserUpdateResponse?> UpdateAsync(int id, UserUpdateRequest request)
        => await ApiService.PutAsync<UserUpdateResponse>($"user/{id}", request);
}
