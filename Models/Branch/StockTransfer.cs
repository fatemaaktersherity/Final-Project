using PharmacyV2.Models.Branch;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Branch
{
    public class StockTransfer
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string InvoiceId { get; set; } = string.Empty;

        public DateTime TransferDate { get; set; } = DateTime.Now;

        public int FromWarehouseId { get; set; }
        public Warehouse FromWarehouse { get; set; } = null!;

        public int ToWarehouseId { get; set; }
        public Warehouse ToWarehouse { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalQty { get; set; }

        public int CreatedByUserId { get; set; }
        //public AppUser CreatedBy { get; set; } = null!;

        // ----- boolean -----
        // True once the destination warehouse (ToWarehouse) has confirmed the
        // stock actually arrived. False (default) while the transfer is still
        // in transit.
        public bool IsReceived { get; set; } = false;

        // Scanned/photographed transfer receipt/challan attached to this transfer.
        // Same optional path/bytes/content-type pattern as Product's image fields —
        // all three are nullable and default to null, so existing transfers and any
        // caller that doesn't supply a receipt keep working without change.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
    }
}