using Flowers.Interfaces;
using Flowers.Models;

namespace Flowers.Services
{
    public class DeliveryService : IDeliveryService
    {
        private readonly ILogger<DeliveryService> _logger;

        public DeliveryService(ILogger<DeliveryService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ReserveCourierAsync(ReserveCourierRequest request)
        {
            try
            {
                _logger.LogInformation($"Reserving courier for order {request.OrderId}, slot: {request.DeliverySlot}");

                // Эмуляция проверки доступности курьера
                await Task.Delay(100);

                // Бизнес-правила: нет курьеров рано утром и поздно вечером
                if (request.DeliverySlot.TimeOfDay < TimeSpan.FromHours(9) ||
                    request.DeliverySlot.TimeOfDay > TimeSpan.FromHours(18))
                {
                    _logger.LogWarning($"No couriers available for slot {request.DeliverySlot}");
                    return false;
                }

                // Эмуляция случая, когда все курьеры заняты
                if (request.DeliverySlot.DayOfWeek == DayOfWeek.Sunday)
                {
                    _logger.LogWarning($"No couriers available on Sunday");
                    return false;
                }

                _logger.LogInformation($"Courier reserved successfully for order {request.OrderId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reserving courier for order {request.OrderId}");
                return false;
            }
        }

        public async Task<bool> CancelCourierAsync(CancelCourierRequest request)
        {
            try
            {
                _logger.LogInformation($"Cancelling courier for order {request.OrderId}");

                // Эмуляция отмены бронирования курьера
                await Task.Delay(100);

                _logger.LogInformation($"Courier reservation cancelled successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling courier for order {request.OrderId}");
                return false;
            }
        }
    }
}