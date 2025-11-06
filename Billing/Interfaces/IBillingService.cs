using Billing.Models;

namespace Billing.Interfaces
{
    public interface IBillingService
    {
        Task<bool> WithdrawAsync(WithdrawRequest request);
        Task<bool> DepositAsync(DepositRequest request);
        Task<decimal> GetBalanceAsync(long userId);
    }
}