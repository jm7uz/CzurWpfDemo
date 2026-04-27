namespace CzurWpfDemo.Models;

public class LoginResponse
{
    public bool Status { get; set; }
    public LoginResult? Resoult { get; set; }
}

public class LoginResult
{
    public string Token { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
