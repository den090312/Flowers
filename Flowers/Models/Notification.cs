using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("notifications")]
    public class Notification
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("message")]
        public string Message { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("status")]
        public string Status { get; set; } = "Sent";

        [ForeignKey("UserId")]
        public User? User { get; set; }
    }
}