using Warehouse.Models;

namespace Warehouse.Interfaces
{
    public interface IWarehouseService
    {
        Task<bool> ReserveProductAsync(ReserveProductRequest request);
        Task<bool> ReleaseProductAsync(ReleaseProductRequest request);
    }
}
