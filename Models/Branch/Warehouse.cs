using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Branch
{
    public class Warehouse
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;

        // ----- number -----
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal Capacity { get; set; } = 0;

        // ----- date -----
        public DateTime? EstablishedDate { get; set; }

        // ----- image -----
        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        // ----- relational data (dropdown) -----
        public int WarehouseTypeId { get; set; }
        public WarehouseType WarehouseType { get; set; } = null!;

        public ICollection<PurchaseInvoice> Purchases { get; set; } = new List<PurchaseInvoice>();
        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
        public ICollection<StockTransfer> StockTransfersFrom { get; set; } = new List<StockTransfer>();
        public ICollection<StockTransfer> StockTransfersTo { get; set; } = new List<StockTransfer>();
        public ICollection<ProductStock> ProductStocks { get; set; } = new List<ProductStock>();
    }
}
