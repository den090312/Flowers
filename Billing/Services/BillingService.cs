using Billing.Data;
using Billing.Interfaces;
using Billing.Models;

using Microsoft.EntityFrameworkCore;

namespace Billing.Services
{
    public class BillingService : IBillingService
    {
        private readonly BillingDbContext _context;
        private readonly ILogger<BillingService> _logger;

        public BillingService(BillingDbContext context, ILogger<BillingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> DepositAsync(DepositRequest request)
        {
            try
            {
                var transaction = new PaymentTransaction
                {
                    UserId = request.UserId,
                    Amount = request.Amount,
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    IdempotencyKey = Guid.NewGuid().ToString()
                };

                _context.PaymentTransactions.Add(transaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Deposited {request.Amount} for user {request.UserId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error depositing for user {request.UserId}");
                return false;
            }
        }

        public async Task<decimal> GetBalanceAsync(long userId)
        {
            try
            {
                var deposits = await _context.PaymentTransactions
                    .Where(t => t.UserId == userId && t.Status == "Completed" && t.Amount > 0)
                    .SumAsync(t => t.Amount);

                var withdrawals = await _context.PaymentTransactions
                    .Where(t => t.UserId == userId && t.Status == "Completed" && t.Amount < 0)
                    .SumAsync(t => t.Amount);

                return deposits + withdrawals;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting balance for user {userId}");
                return 0;
            }
        }

        public async Task<bool> WithdrawAsync(WithdrawRequest request)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Проверяем идемпотентность
                if (!string.IsNullOrEmpty(request.IdempotencyKey))
                {
                    var existingTransaction = await _context.PaymentTransactions
                        .FirstOrDefaultAsync(t => t.IdempotencyKey == request.IdempotencyKey);

                    if (existingTransaction != null)
                    {
                        _logger.LogInformation($"Idempotent request detected for key {request.IdempotencyKey}");
                        return existingTransaction.Status == "Completed";
                    }
                }

                var balance = await GetBalanceAsync(request.UserId);

                if (balance < request.Amount)
                {
                    _logger.LogWarning($"Insufficient funds for user {request.UserId}. Balance: {balance}, Requested: {request.Amount}");
                    return false;
                }

                var paymentTransaction = new PaymentTransaction
                {
                    UserId = request.UserId,
                    OrderId = request.OrderId,
                    Amount = -request.Amount, // Отрицательная сумма для списания
                    Status = "Completed",
                    IdempotencyKey = request.IdempotencyKey ?? Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };

                _context.PaymentTransactions.Add(paymentTransaction);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"Withdrawn {request.Amount} from user {request.UserId} for order {request.OrderId}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error withdrawing for user {request.UserId}");
                return false;
            }
        }
    }
}