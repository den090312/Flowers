using Flowers.Models;

namespace Flowers.Interfaces
{
    public interface IOrderSagaService
    {
        Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, long userId);
    }
}
