using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("accounts")]
    public class Account
    {
        [Column("id")]
        public long Id { get; set; }
        
        [Column("user_id")]
        public long UserId { get; set; }
        
        [Column("balance")]
        public decimal Balance { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}