namespace Flowers.Models
{
    public class WithdrawRequest
    {
        public long UserId { get; set; }
        
        public decimal Amount { get; set; }

        public string? IdempotencyKey { get; set; }
        public long? OrderId { get; set; }
    }
}
