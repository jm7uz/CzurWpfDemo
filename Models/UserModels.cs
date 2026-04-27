namespace CzurWpfDemo.Models;

public class UserRequest
{
    public string? Search { get; set; }
}

public class UserResponse
{
    public bool Status { get; set; }
    public List<UserItem>? Resoult { get; set; }
}

public class UserItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int IsActive { get; set; }
    public string? ResponsibleWorker { get; set; }
    public string? CreatedAt { get; set; }

    public override string ToString() => Name;
}
