namespace Warehouse.Models
{
    public class Warehouse
    {
        public long Id { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int Reserved { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}