namespace PharmacyV2.Enums
{
    public enum PurchaseOrderSource
    {
        Auto,       // created from Auto-Requirement -> DailyPurchaseRequirementTable
        Manual      // created from the "Manual Order" screen
    }
    // "Temp Order" -> PendingCheck -> Checked (Order Checking screen, sets ReceivingQty)
    // -> Converted (saved into the Purchase table) or Cancelled.
    public enum PurchaseOrderStatus
    {
        Draft,
        PendingCheck,
        Checked,
        Converted,
        Cancelled
    }
    public enum DepreciationMethod 
    
    { 
        StraightLine, 
        ReducingBalance 
    }
    public enum LiabilityType 
    { 
        Loan, 
        CreditCard, 
        Lease, 
        TaxPayable, 
        Other 
    }

    // Where a LedgerAccount posting originated from — lets the General Ledger
    // trace a debit/credit line back to the document that generated it
    // (SourceId is that document's Id; no DB-level FK since it's polymorphic).
    public enum LedgerSourceType
    {
        OpeningBalance = 0,
        Sale = 1,
        SaleReturn = 2,
        Purchase = 3,
        PurchaseReturn = 4,
        SupplierPayment = 5,
        CustomerPayment = 6,
        AssetDepreciation = 7,
        Manual = 8,
        Adjustment = 9,
        SalePayment = 10,
        SaleVoid = 11,
        PurchaseReturnRefund = 12,
        SaleReturnRefund = 13
    }
}
