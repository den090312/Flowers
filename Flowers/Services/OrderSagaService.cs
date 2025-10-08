using Flowers.Data;
using Flowers.Interfaces;
using Flowers.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

using Npgsql.Internal;

namespace Flowers.Services
{
    public class OrderSagaService : IOrderSagaService
    {
        private readonly AppDbContext _context;
        private readonly IBillingService _billingService;
        private readonly IWarehouseService _warehouseService;
        private readonly IDeliveryService _deliveryService;
        private readonly ILogger<OrderSagaService> _logger;
        private long _orderId = 0;

        public OrderSagaService(
            AppDbContext context,
            IBillingService billingService,
            IWarehouseService warehouseService,
            IDeliveryService deliveryService,
            ILogger<OrderSagaService> logger)
        {
            _context = context;
            _billingService = billingService;
            _warehouseService = warehouseService;
            _deliveryService = deliveryService;
            _logger = logger;
        }

        public async Task<CreateOrderResponse?> CreateOrderAsync(CreateOrderRequest request, long userId)
        {
            var sagaIdempotencyKey = Guid.NewGuid().ToString();

            var strategy = _context.Database.CreateExecutionStrategy();
            var result = new CreateOrderResponse();
            IDbContextTransaction? transaction = null;

            await strategy.ExecuteAsync(async () =>
            {
                var existingSaga = await _context.CompletedSagas
                    .FirstOrDefaultAsync(s => s.IdempotencyKey == sagaIdempotencyKey);

                if (existingSaga != null)
                {
                    result = new CreateOrderResponse
                    {
                        OrderId = existingSaga.OrderId,
                        Status = existingSaga?.Status ?? string.Empty,
                        ErrorMessage = existingSaga?.ErrorMessage
                    };
                    return;
                }

                try
                {
                    transaction = await _context.Database.BeginTransactionAsync();
                    _orderId = 0;

                    result = await CreateOrderSagaAsync(request, userId, transaction);

                    if (result?.Status == "Completed")
                    {
                        if (IsTransactionActive(transaction))
                        {
                            await _context.SaveChangesAsync();
                            await transaction.CommitAsync();
                        }
                        else
                        {
                            _logger.LogWarning("Transaction was already completed before commit attempt");
                            result = new CreateOrderResponse
                            {
                                OrderId = _orderId,
                                Status = "Failed",
                                ErrorMessage = "Transaction consistency error"
                            };
                        }
                    }
                    else
                    {
                        result = new CreateOrderResponse
                        {
                            OrderId = _orderId,
                            Status = "Failed",
                            ErrorMessage = result?.ErrorMessage
                        };

                        await SafeRollbackAsync(transaction);
                    }
                }
                catch (Exception ex)
                {
                    await SafeRollbackAsync(transaction);
                    result = new CreateOrderResponse
                    {
                        OrderId = _orderId,
                        Status = "Failed",
                        ErrorMessage = GetUserFriendlyErrorMessage(ex)
                    };
                }
                finally
                {
                    await SafeDisposeTransactionAsync(transaction);
                }
            });

            return result;
        }

        private async Task SafeRollbackAsync(IDbContextTransaction? transaction)
        {
            if (transaction == null) return;

            try
            {
                if (IsTransactionActive(transaction))
                {
                    await transaction.RollbackAsync();
                    _logger.LogInformation("Transaction rolled back successfully");
                }
                else
                {
                    _logger.LogWarning("Attempted to rollback already completed transaction");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rollback transaction");
            }
        }

        private bool IsTransactionActive(IDbContextTransaction transaction)
        {
            try
            {
                var dbTransaction = transaction.GetDbTransaction();

                return dbTransaction?.Connection?.State == System.Data.ConnectionState.Open;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check transaction status");
                return false;
            }
        }

        private async Task SafeDisposeTransactionAsync(IDbContextTransaction? transaction)
        {
            if (transaction == null) return;

            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose transaction");
            }
        }

        private string GetUserFriendlyErrorMessage(Exception ex)
        {
            return ex.Message.Contains("NpgsqlTransaction has completed", StringComparison.OrdinalIgnoreCase)
                ? "Transaction processing error occurred"
                : ex.Message;
        }

        private CreateOrderResponse Catch(IDbContextTransaction transaction, Exception ex, CreateOrderResponse? result)
        {
            _logger.LogError(ex, $"Saga failed for order {_orderId}");

            return new CreateOrderResponse
            {
                OrderId = _orderId,
                Status = "Failed",
                ErrorMessage = result?.ErrorMessage ?? ex.Message
            };
        }

        private async Task<CreateOrderResponse?> CreateOrderSagaAsync(CreateOrderRequest request, long userId, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            // 1. Создание записи заказа
            (_orderId, Order order) = await CreateOrder(request, userId, _orderId);

            // 2. Шаг 1: Проверка платежа (Биллинг)
            (bool flowControl, CreateOrderResponse? value) = await SetBilling(request, userId, transaction, _orderId, order);

            if (!flowControl)
                return (value);

            // 3. Шаг 2: Резервирование товара (Склад)
            (flowControl, value) = await ReserveProduct(request, userId, transaction, _orderId, order);

            if (!flowControl)
                return value;

            // 4. Шаг 3: Резервирование курьера (Доставка)
            (flowControl, value) = await ReserveCourier(request, userId, transaction, _orderId, order);

            if (!flowControl)
                return value;

            // 5. Финальное обновление статуса заказа
            return UpdateOrderStatus(transaction, _orderId, order);
        }

        private CreateOrderResponse UpdateOrderStatus(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, long orderId, Order order)
        {
            order.Status = "Completed";

            _logger.LogInformation($"Saga completed successfully for order {orderId}");

            return new CreateOrderResponse
            {
                OrderId = orderId,
                Status = "Completed"
            };
        }

        private async Task<(bool flowControl, CreateOrderResponse? value)> ReserveCourier(CreateOrderRequest request, long userId, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, long orderId, Order order)
        {
            _logger.LogInformation($"Step 3: Reserving courier for order {orderId}");

            var deliverySuccess = await _deliveryService.ReserveCourierAsync(new ReserveCourierRequest
            {
                OrderId = orderId,
                UserId = userId,
                DeliverySlot = request.DeliverySlot
            });

            if (!deliverySuccess)
            {
                _logger.LogWarning($"Courier reservation failed for order {orderId}");

                // Компенсируем склад и платеж
                await _warehouseService.ReleaseProductAsync(new ReleaseProductRequest
                {
                    ProductId = request.ProductId,
                    Quantity = request.Quantity,
                    OrderId = orderId
                });

                await _billingService.DepositAsync(new DepositRequest
                {
                    UserId = userId,
                    Amount = request.Amount
                });

                order.Status = "Failed - Delivery";

                return (flowControl: false, value: new CreateOrderResponse
                {
                    OrderId = orderId,
                    Status = "Failed",
                    ErrorMessage = "Courier reservation failed: no available couriers"
                });
            }

            _logger.LogInformation($"Courier reserved successfully for order {orderId}");

            return (flowControl: true, value: null);
        }

        private async Task<(bool flowControl, CreateOrderResponse? value)> ReserveProduct(CreateOrderRequest request, long userId, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, long orderId, Order order)
        {
            _logger.LogInformation($"Step 2: Reserving product for order {orderId}");

            var reservationSuccess = await _warehouseService.ReserveProductAsync(new ReserveProductRequest
            {
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                OrderId = orderId
            });

            if (!reservationSuccess)
            {
                _logger.LogWarning($"Product reservation failed for order {orderId}");

                // Компенсируем платеж
                await _billingService.DepositAsync(new DepositRequest
                {
                    UserId = userId,
                    Amount = request.Amount
                });

                order.Status = "Failed - Warehouse";

                return (flowControl: false, value: new CreateOrderResponse
                {
                    OrderId = orderId,
                    Status = "Failed",
                    ErrorMessage = "Product reservation failed: insufficient stock"
                });
            }

            _logger.LogInformation($"Product reserved successfully for order {orderId}");

            return (flowControl: true, value: null);
        }

        private async Task<(bool flowControl, CreateOrderResponse? value)> SetBilling(CreateOrderRequest request, long userId, IDbContextTransaction transaction, long orderId, Order order)
        {
            _logger.LogInformation($"Step 1: Processing payment for order {orderId}");

            // Генерируем ключ идемпотентности для платежа
            var paymentIdempotencyKey = $"payment_{orderId}_{userId}";

            // Проверяем, не был ли уже создан заказ с таким платежом
            var existingPayment = await _context.PaymentTransactions
                .FirstOrDefaultAsync(p => p.OrderId == orderId && p.UserId == userId);

            if (existingPayment != null)
            {
                // Если платеж уже был выполнен, возвращаем его результат
                return (flowControl: existingPayment.Status == "Completed",
                        value: existingPayment.Status == "Completed" ? null : new CreateOrderResponse
                        {
                            OrderId = orderId,
                            Status = "Failed",
                            ErrorMessage = "Payment already processed and failed"
                        });
            }

            var paymentSuccess = await _billingService.WithdrawAsync(new WithdrawRequest
            {
                UserId = userId,
                Amount = request.Amount,
                IdempotencyKey = paymentIdempotencyKey,
                OrderId = orderId // Добавляем OrderId для связи
            });

            if (paymentSuccess)
            {
                _logger.LogInformation($"Payment processed successfully for order {orderId}");
                return (flowControl: true, value: null);
            }

            _logger.LogWarning($"Payment failed for order {orderId}");
            order.Status = "Failed - Payment";

            return (flowControl: false, value: new CreateOrderResponse
            {
                OrderId = orderId,
                Status = "Failed",
                ErrorMessage = "Payment failed: insufficient funds"
            });
        }

        private async Task<(long orderId, Order order)> CreateOrder(CreateOrderRequest request, long userId, long orderId)
        {
            var order = new Order
            {
                UserId = userId,
                Amount = request.Amount,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                DeliverySlot = request.DeliverySlot,
                Status = "Processing",
                CreatedAt = DateTime.UtcNow
            };

            _context.Orders.Add(order);

            await _context.SaveChangesAsync();
            
            orderId = order.Id;

            _logger.LogInformation($"Saga started for order {orderId}");
            
            return (orderId, order);
        }
    }
}