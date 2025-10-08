using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("payment_transactions")]
    public class PaymentTransaction
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("idempotency_key")]
        public string? IdempotencyKey { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("order_id")]
        public long? OrderId { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("status")]
        public string? Status { get; set; } // Processing, Completed, Failed

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}