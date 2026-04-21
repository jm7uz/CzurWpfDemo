using System.Collections.Generic;

namespace CzurWpfDemo.Models;

// ─── So'rov modellari ────────────────────────────────────────────────────────

public class UserListRequest
{
    public string? Search { get; set; }
}

public class UserStoreRequest
{
    public string Name     { get; set; } = string.Empty;
    public string Phone    { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool   IsActive { get; set; } = true;
    public string Role     { get; set; } = "user";
}

public class UserUpdateRequest
{
    public string  Name     { get; set; } = string.Empty;
    public string  Phone    { get; set; } = string.Empty;
    public string? Password { get; set; }   // null yoki bo'sh = o'zgartirmaslik
    public bool    IsActive { get; set; } = true;
    public string  Role     { get; set; } = "user";
}

// ─── Javob modellari ─────────────────────────────────────────────────────────

public class UserListResponse
{
    public bool            Status  { get; set; }
    public UserListResult? Resoult { get; set; }
}

public class UserListResult
{
    public List<UserItem>? Data { get; set; }
    public PaginationMeta? Meta { get; set; }
}

public class UserSingleResponse
{
    public bool      Status  { get; set; }
    public string?   Message { get; set; }
    public UserItem? Resoult { get; set; }
}

public class UserUpdateResponse
{
    public bool    Status  { get; set; }
    public string? Resoult { get; set; }
}

// ─── User ma'lumot modeli ────────────────────────────────────────────────────

public class UserItem
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = string.Empty;
    public string Phone     { get; set; } = string.Empty;
    public string Role      { get; set; } = string.Empty;
    public bool   IsActive  { get; set; }
    public string? CreatedAt { get; set; }
}
