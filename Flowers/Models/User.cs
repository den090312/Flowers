using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("users")]
    public class User
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("username")]
        public string? Username { get; set; }

        [Column("firstname")]
        public string? FirstName { get; set; }

        [Column("lastname")]
        public string? LastName { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("phone")]
        public string? Phone { get; set; }

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