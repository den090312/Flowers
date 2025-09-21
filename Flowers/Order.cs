namespace Flowers.Models
{
    public class Order
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CreateOrderRequest
    {
        public decimal Amount { get; set; }
    }
}