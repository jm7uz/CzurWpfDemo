namespace CzurWpfDemo.Models;

public class UserInfoResponse
{
    public bool Status { get; set; }
    public UserInfo? Resoult { get; set; }
}

public class UserInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
