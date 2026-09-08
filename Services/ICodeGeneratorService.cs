namespace PharmacyV2.Services
{
    // Central place for every system-generated business code (Product Code,
    // Purchase Invoice No, Sale Invoice No, Stock Batch No). Every code is
    // built purely in C# from a Guid — no SQL sequences, IDENTITY tricks, or
    // client-supplied values — and is checked against the database before
    // being handed back, so callers never see a collision and never have to
    // retry themselves.
    public interface ICodeGeneratorService
    {
        Task<string> GenerateProductCodeAsync();
        Task<string> GeneratePurchaseInvoiceNoAsync();
        Task<string> GenerateSaleInvoiceNoAsync();
        Task<string> GenerateBatchNumberAsync();
        Task<string> GeneratePurchaseOrderNoAsync();
    }
}
