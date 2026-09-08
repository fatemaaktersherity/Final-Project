using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Branch
{
    public class StockTransferItem
    {
        public int Id { get; set; }

        public int StockTransferId { get; set; }
        public StockTransfer StockTransfer { get; set; } = null!;

        public int MedicineId { get; set; }
        public Product.Product Medicine { get; set; } = null!;

        public int SourceProductStockId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        public DateTime? ExpireDate { get; set; }
    }
}
