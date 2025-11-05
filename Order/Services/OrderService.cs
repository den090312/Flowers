using OrderService.Data;
using OrderService.Models;
using Microsoft.EntityFrameworkCore;

namespace OrderService.Services
{
    public interface IOrderService
    {
        Task<Order> CreateOrderAsync(Order order);
        Task<Order?> GetOrderAsync(long orderId);
        Task<List<Order>> GetUserOrdersAsync(long userId);
        Task<bool> UpdateOrderStatusAsync(long orderId, string status);
    }

    public class OrderService : IOrderService
    {
        private readonly OrderDbContext _context;
        private readonly IKafkaProducer _kafkaProducer;
        private readonly ILogger<OrderService> _logger;

        public OrderService(OrderDbContext context, IKafkaProducer kafkaProducer, ILogger<OrderService> logger)
        {
            _context = context;
            _kafkaProducer = kafkaProducer;
            _logger = logger;
        }

        public async Task<Order> CreateOrderAsync(Order order)
        {
            try
            {
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                // Отправляем событие в Kafka
                await _kafkaProducer.ProduceAsync("order-created", new
                {
                    OrderId = order.Id,
                    order.UserId,
                    order.Amount,
                    order.ProductId,
                    order.Quantity,
                    order.DeliverySlot
                });

                _logger.LogInformation($"Order {order.Id} created for user {order.UserId}");
                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                throw;
            }
        }

        public async Task<Order?> GetOrderAsync(long orderId)
        {
            return await _context.Orders.FindAsync(orderId);
        }

        public async Task<List<Order>> GetUserOrdersAsync(long userId)
        {
            return await _context.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> UpdateOrderStatusAsync(long orderId, string status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null) return false;

            order.Status = status;
            await _context.SaveChangesAsync();

            // Отправляем событие об изменении статуса
            await _kafkaProducer.ProduceAsync("order-status-updated", new
            {
                OrderId = orderId,
                NewStatus = status
            });

            return true;
        }
    }
}