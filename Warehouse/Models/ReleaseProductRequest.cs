namespace Warehouse.Models
{
    public class ReleaseProductRequest
    {
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public long OrderId { get; set; }
    }
}
