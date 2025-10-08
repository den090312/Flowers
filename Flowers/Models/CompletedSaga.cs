using System.ComponentModel.DataAnnotations.Schema;

namespace Flowers.Models
{
    [Table("completed_sagas")]
    public class CompletedSaga
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("idempotency_key")]
        public string? IdempotencyKey { get; set; }

        [Column("order_id")]
        public long OrderId { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}