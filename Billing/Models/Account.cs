using System.ComponentModel.DataAnnotations.Schema;

namespace Billing.Models
{
    [Table("accounts")]
    public class Account
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("balance")]
        public decimal Balance { get; set; } = 0;
    }
}