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
            // Если передан orderId - проверяем, не оплачен ли уже заказ
            if (request.OrderId.HasValue)
            {
                var existingPayment = await _context.PaymentTransactions
                    .FirstOrDefaultAsync(p => p.OrderId == request.OrderId.Value);

                if (existingPayment != null)
                {
                    _logger.LogInformation($"Found existing payment for order {request.OrderId.Value}, Status: {existingPayment.Status}");

                    if (existingPayment.Status == "Completed")
                    {
                        throw new InvalidOperationException($"Order {request.OrderId.Value} is already paid");
                    }
                    // Если предыдущий платеж failed, можно попробовать снова
                }
            }

            // Создаем запись о транзакции для идемпотентности
            var transaction = new PaymentTransaction
            {
                IdempotencyKey = $"withdraw_{request.UserId}_{request.OrderId}_{DateTime.UtcNow.Ticks}",
                UserId = request.UserId,
                OrderId = request.OrderId,
                Amount = request.Amount,
                Status = "Processing",
                CreatedAt = DateTime.UtcNow
            };

            _context.PaymentTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            try
            {
                // Проверяем достаточно ли средств
                var account = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.UserId == request.UserId);

                if (account == null)
                {
                    throw new InvalidOperationException($"Account not found for user {request.UserId}");
                }

                if (account.Balance < request.Amount)
                {
                    transaction.Status = "Failed";
                    transaction.CompletedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    _logger.LogWarning($"Insufficient funds for user {request.UserId}. Balance: {account.Balance}, Required: {request.Amount}");
                    return false;
                }

                // Выполняем списание
                account.Balance -= request.Amount;

                transaction.Status = "Completed";
                transaction.CompletedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Successfully withdrawn {request.Amount} from user {request.UserId}. New balance: {account.Balance}");

                return true;
            }
            catch (Exception ex)
            {
                transaction.Status = "Failed";
                transaction.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogError(ex, $"Failed to withdraw {request.Amount} from user {request.UserId}");
                throw;
            }
        }

        private async Task<bool> ProcessWithdrawal(WithdrawRequest request)
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