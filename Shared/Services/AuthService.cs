using CzurWpfDemo.Models;

namespace CzurWpfDemo.Services;

public class AuthService
{
    public static string? Token { get; private set; }
    public static UserInfo? CurrentUser { get; private set; }

    public static async Task<LoginResponse?> LoginAsync(string phone, string password)
    {
        var request = new LoginRequest
        {
            Phone = phone,
            Password = password
        };

        var response = await ApiService.PostAsync<LoginResponse>("auth/login", request);

        if (response is { Status: true, Resoult: not null })
        {
            Token = response.Resoult.Token;
            ApiService.SetToken(Token);
        }

        return response;
    }

    public static async Task<UserInfo?> GetMeAsync()
    {
        var response = await ApiService.GetAsync<UserInfoResponse>("auth/me");

        if (response is { Status: true, Resoult: not null })
        {
            CurrentUser = response.Resoult;
        }

        return CurrentUser;
    }
}
