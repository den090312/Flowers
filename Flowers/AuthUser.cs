using Flowers.Models;

using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers
{
    [Table("auth_users")]
    public class AuthUser
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("username")]
        public required string Username { get; set; }

        [Column("password_hash")]
        public required string PasswordHash { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}

public class LoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class RegisterRequest : LoginRequest
{
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Phone { get; set; }
}

public class AuthResponse
{
    public required string Token { get; set; }
    public required long UserId { get; set; }
    public required string Username { get; set; }
}