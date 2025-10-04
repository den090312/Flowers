using Flowers.Data;
using Flowers.Interfaces;
using Flowers.Models;

using Microsoft.EntityFrameworkCore;

namespace Flowers.Services
{
    public class BillingService : IBillingService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BillingService> _logger;

        public BillingService(AppDbContext context, ILogger<BillingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> WithdrawAsync(WithdrawRequest request)
        {
            try
            {
                _logger.LogInformation($"Withdrawing {request.Amount} for user {request.UserId}");

                var account = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.UserId == request.UserId);

                if (account == null)
                {
                    _logger.LogWarning($"Account not found for user {request.UserId}");
                    return false;
                }

                if (account.Balance < request.Amount)
                {
                    _logger.LogWarning($"Insufficient funds for user {request.UserId}. Balance: {account.Balance}, Requested: {request.Amount}");
                    return false;
                }

                account.Balance -= request.Amount;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Withdrawal successful for user {request.UserId}. New balance: {account.Balance}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during withdrawal for user {request.UserId}");
                return false;
            }
        }

        public async Task<bool> DepositAsync(DepositRequest request)
        {
            try
            {
                _logger.LogInformation($"Depositing {request.Amount} for user {request.UserId}");

                var account = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.UserId == request.UserId);

                if (account == null)
                {
                    // Создаем аккаунт если не существует
                    account = new Account
                    {
                        UserId = request.UserId,
                        Balance = 0
                    };
                    _context.Accounts.Add(account);
                }

                account.Balance += request.Amount;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Deposit successful for user {request.UserId}. New balance: {account.Balance}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during deposit for user {request.UserId}");
                return false;
            }
        }

        public async Task<decimal> GetBalanceAsync(long userId)
        {
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.UserId == userId);

            return account?.Balance ?? 0;
        }
    }
}