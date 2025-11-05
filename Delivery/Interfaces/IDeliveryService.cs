using DeliveryService.Models;

namespace DeliveryService.Interfaces
{
    public interface IDeliveryService
    {
        Task<bool> ReserveCourierAsync(ReserveCourierRequest request);
        Task<bool> CancelCourierAsync(CancelCourierRequest request);
    }
}
