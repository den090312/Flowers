using Flowers.Models;

namespace Flowers.Interfaces
{
    public interface IDeliveryService
    {
        Task<bool> ReserveCourierAsync(ReserveCourierRequest request);
        Task<bool> CancelCourierAsync(CancelCourierRequest request);
    }
}
