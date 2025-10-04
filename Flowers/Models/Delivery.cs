namespace Flowers.Models
{
    public class Delivery
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public long UserId { get; set; }
        public string CourierId { get; set; } = string.Empty;
        public DateTime DeliverySlot { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ReserveCourierRequest
    {
        public long OrderId { get; set; }
        public long UserId { get; set; }
        public DateTime DeliverySlot { get; set; }
    }

    public class CancelCourierRequest
    {
        public long OrderId { get; set; }
        public string CourierId { get; set; } = string.Empty;
    }
}