using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PurchaseInvoiceItemReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal SubTotal { get; set; }
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    public class PurchaseInvoiceItemWriteDto
    {
        [Required]
        public int ProductId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitCost { get; set; }

        // Optional: leave blank to have a GUID-based batch number generated
        // automatically (see ICodeGeneratorService.GenerateBatchNumberAsync),
        // or send the manufacturer's real batch/lot number to record it as-is.
        [MaxLength(100)]
        public string? BatchNumber { get; set; }

        public DateTime? ExpiryDate { get; set; }
    }

    public class PurchaseInvoiceReadDto
    {
        public int Id { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public int SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public int? PurchaseOrderId { get; set; }
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Advance { get; set; }
        public decimal Due { get; set; }
        public decimal Total { get; set; }
        public decimal Discount { get; set; }
        public decimal TaxOrOthers { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public bool IsSupplierWise { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? ReceiptImagePath { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseInvoiceItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/purchaseinvoices — header + line items in one call,
    // same convention as PurchaseOrders. Due is computed server-side
    // (Total - Advance - Discount + TaxOrOthers) when left at 0.
    public class PurchaseInvoiceCreateDto
    {
        // Ignored by the server: InvoiceNo is always generated from a Guid
        // by ICodeGeneratorService (see PurchaseInvoicesController.Create),
        // so it's unique and consistently formatted without relying on the
        // caller. Kept on the DTO only so old clients that still send it
        // don't fail model binding.
        [Obsolete("Server-generated; sending a value has no effect.")]
        [MaxLength(50)]
        public string InvoiceNo { get; set; } = string.Empty;

        public DateTime PurchaseDate { get; set; } = DateTime.Now;

        [Required]
        public int SupplierId { get; set; }

        public int? PurchaseOrderId { get; set; }

        [Required]
        public int WarehouseId { get; set; }

        public string PaymentMethod { get; set; } = "Cash";

        [Range(0, double.MaxValue)]
        public decimal Advance { get; set; } = 0;

        [Range(0, double.MaxValue)]
        public decimal Due { get; set; } = 0;

        [Range(0, double.MaxValue)]
        public decimal Total { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Discount { get; set; } = 0;

        [Range(0, double.MaxValue)]
        public decimal TaxOrOthers { get; set; } = 0;

        public string PaymentStatus { get; set; } = "InComplete";

        public bool IsSupplierWise { get; set; } = true;

        public List<PurchaseInvoiceItemWriteDto>? Items { get; set; }
    }

    // Line item as sent inside PurchaseInvoiceUpdateDto.Items. Id is optional:
    // - Id omitted/0  -> treated as a NEW item (added to the invoice)
    // - Id present and matches an existing item on this invoice -> that item is updated
    // Any existing item on the invoice whose Id is NOT present in the incoming
    // list is REMOVED (same as calling the items DELETE endpoint for it) —
    // this is a full-replace sync, same convention as the rest of the app.
    public class PurchaseInvoiceItemUpsertDto : PurchaseInvoiceItemWriteDto
    {
        public int? Id { get; set; }
    }

    // Body for PUT /api/purchaseinvoices/{id} — payment status/amounts only.
    // Body for PUT /api/purchaseinvoices/{id} — header amount fields.
    // NOTE: Due and PaymentStatus below are accepted for backward
    // compatibility but IGNORED by the controller — both are now always
    // derived server-side (Total - Advance - payments already allocated via
    // SupplierPayments), so they can never drift from what's actually been
    // paid. Sending them has no effect.
    public class PurchaseInvoiceUpdateDto
    {
        [Range(0, double.MaxValue)]
        public decimal Advance { get; set; }

        [Obsolete("Ignored — Due is always derived server-side now. See SupplierPaymentsController.")]
        [Range(0, double.MaxValue)]
        public decimal Due { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Total { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Discount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal TaxOrOthers { get; set; }

        public string PaymentMethod { get; set; } = "Cash";

        [Obsolete("Ignored — PaymentStatus is always derived server-side now. See SupplierPaymentsController.")]
        public string PaymentStatus { get; set; } = "InComplete";

        // Optional. When present, the invoice's item set is synced to match
        // this list exactly (add/update/remove) — see PurchaseInvoiceItemUpsertDto.
        // When null (omitted), items are left untouched, same as before.
        public List<PurchaseInvoiceItemUpsertDto>? Items { get; set; }
    }
}