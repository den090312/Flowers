using Flowers.Interfaces;
using Flowers.Models;

namespace Flowers.Services
{
    public class WarehouseService : IWarehouseService
    {
        private readonly ILogger<WarehouseService> _logger;

        public WarehouseService(ILogger<WarehouseService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ReserveProductAsync(ReserveProductRequest request)
        {
            try
            {
                _logger.LogInformation($"Reserving product {request.ProductId}, quantity: {request.Quantity} for order {request.OrderId}");

                // Эмуляция проверки наличия товара
                await Task.Delay(100); // Имитация работы с внешним сервисом

                if (request.Quantity > 10) // Предположим, что у нас ограниченный запас
                {
                    _logger.LogWarning($"Insufficient stock for product {request.ProductId}");

                    return false;
                }

                if (request.ProductId == "out_of_stock_product")
                {
                    _logger.LogWarning($"Product {request.ProductId} is out of stock");

                    return false;
                }

                _logger.LogInformation($"Product {request.ProductId} reserved successfully");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reserving product {request.ProductId}");

                return false;
            }
        }

        public async Task<bool> ReleaseProductAsync(ReleaseProductRequest request)
        {
            try
            {
                _logger.LogInformation($"Releasing reservation for product {request.ProductId}, quantity: {request.Quantity} from order {request.OrderId}");

                // Эмуляция освобождения резерва
                await Task.Delay(100);

                _logger.LogInformation($"Product reservation released successfully");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error releasing product reservation {request.ProductId}");

                return false;
            }
        }
    }
}