namespace Flowers.Models
{
    public class WithdrawRequest
    {
        public long UserId { get; set; }
        
        public decimal Amount { get; set; }
    }
}
