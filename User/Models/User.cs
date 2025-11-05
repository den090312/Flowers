using System.ComponentModel.DataAnnotations.Schema;

namespace UserService.Models
{
    [Table("users")]
    public class User
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("username")]
        public string Username { get; set; } = string.Empty;

        [Column("firstname")]
        public string FirstName { get; set; } = string.Empty;

        [Column("lastname")]
        public string LastName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("phone")]
        public string Phone { get; set; } = string.Empty;

        public override bool Equals(object? obj)
        {
            return obj is User user &&
                Id == user.Id &&
                Username == user.Username &&
                FirstName == user.FirstName &&
                LastName == user.LastName &&
                Email == user.Email &&
                Phone == user.Phone;
        }

        public override int GetHashCode()
            => HashCode.Combine(Id, Username, FirstName, LastName, Email, Phone);
    }
}