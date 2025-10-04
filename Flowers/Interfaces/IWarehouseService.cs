using Flowers.Models;

namespace Flowers.Interfaces
{
    public interface IWarehouseService
    {
        Task<bool> ReserveProductAsync(ReserveProductRequest request);
        Task<bool> ReleaseProductAsync(ReleaseProductRequest request);
    }
}
