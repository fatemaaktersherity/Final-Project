using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;

namespace PharmacyV2.Services
{
    // GUID-based implementation of ICodeGeneratorService. Nothing here is a
    // SQL default/identity/sequence — every code is generated in C# code and
    // then re-checked against the table it will live in, so the (already
    // astronomically unlikely) case of two Guids producing the same short
    // token is still handled safely instead of trusting probability alone.
    public class CodeGeneratorService : ICodeGeneratorService
    {
        private readonly PharmacyDbContext _db;
        private const int MaxAttempts = 10;

        public CodeGeneratorService(PharmacyDbContext db)
        {
            _db = db;
        }

        // 8 hex characters out of a Guid ("N" format = no dashes) gives
        // 16^8 (~4.3 billion) combinations — short enough to read on a
        // receipt/barcode label, long enough that a collision inside one
        // pharmacy's dataset is effectively impossible.
        private static string Token(int length = 8) =>
            Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();

        public async Task<string> GenerateProductCodeAsync()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var code = $"PRD-{Token()}";
                if (!await _db.Products.AnyAsync(p => p.ProductCode == code))
                    return code;
            }
            throw new InvalidOperationException("Could not generate a unique product code. Please try again.");
        }

        public async Task<string> GeneratePurchaseInvoiceNoAsync()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var code = $"PINV-{DateTime.UtcNow:yyyyMMdd}-{Token()}";
                if (!await _db.PurchaseInvoices.AnyAsync(p => p.InvoiceNo == code))
                    return code;
            }
            throw new InvalidOperationException("Could not generate a unique purchase invoice number. Please try again.");
        }

        public async Task<string> GenerateSaleInvoiceNoAsync()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var code = $"SINV-{DateTime.UtcNow:yyyyMMdd}-{Token()}";
                if (!await _db.Sales.AnyAsync(s => s.InvoiceNo == code))
                    return code;
            }
            throw new InvalidOperationException("Could not generate a unique sale invoice number. Please try again.");
        }

        public async Task<string> GenerateBatchNumberAsync()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var code = $"BATCH-{Token()}";
                if (!await _db.ProductStocks.AnyAsync(s => s.BatchNumber == code))
                    return code;
            }
            throw new InvalidOperationException("Could not generate a unique batch number. Please try again.");
        }
        public async Task<string> GeneratePurchaseOrderNoAsync()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var code = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Token()}";
                if (!await _db.PurchaseOrders.AnyAsync(o => o.OrderNo == code))
                    return code;
            }
            throw new InvalidOperationException("Could not generate a unique purchase order number. Please try again.");
        }
    }
}
