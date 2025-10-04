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

        [Column("product_id")]
        public string? ProductId { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("delivery_slot")]
        public DateTime? DeliverySlot { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }

    public class CreateOrderRequest
    {
        public decimal Amount { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime DeliverySlot { get; set; }
    }

    public class CreateOrderResponse
    {
        public long OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }
}