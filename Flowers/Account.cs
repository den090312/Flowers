namespace Flowers.Models
{
    public class Account
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public decimal Balance { get; set; }
    }

    public class DepositRequest
    {
        public long UserId { get; set; }
        public decimal Amount { get; set; }
    }

    public class WithdrawRequest
    {
        public long UserId { get; set; }
        public decimal Amount { get; set; }
    }
}