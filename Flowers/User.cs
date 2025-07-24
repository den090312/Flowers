using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("users")]  // Явное указание таблицы в нижнем регистре
    public class User
    {
        [Column("id")]  // Можно явно указать имена колонок
        public long Id { get; set; }

        [Column("username")]
        public required string Username { get; set; }

        [Column("firstname")]
        public required string FirstName { get; set; }

        [Column("lastname")]
        public required string LastName { get; set; }

        [Column("email")]
        public required string Email { get; set; }

        [Column("phone")]
        public required string Phone { get; set; }
    }
}