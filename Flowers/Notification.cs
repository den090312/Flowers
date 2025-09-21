namespace Flowers.Models
{
    public class Notification
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Sent";
    }
}