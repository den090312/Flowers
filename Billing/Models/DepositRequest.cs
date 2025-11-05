namespace Billing.Models
{
    public class DepositRequest
    {
        public long UserId { get; set; }
        public decimal Amount { get; set; }
    }
}