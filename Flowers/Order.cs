using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("orders")]
    public class Order
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("status")]
        public string Status { get; set; } = "Pending";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }

    public class CreateOrderRequest
    {
        public decimal Amount { get; set; }
    }
}