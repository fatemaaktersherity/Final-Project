# PharmacyV2 — Full Source Code Reference

A .NET / ASP.NET Core Web API backend for a pharmacy management system (EF Core, JWT auth, layered Controllers → Services → Data → Models).

This document contains the complete source of every uploaded file, organized by layer, plus a business-logic overview for each module.

## Table of Contents

- [1. Project File](#1-project-file)
- [2. Architecture & Business Logic Overview](#2-architecture--business-logic-overview)
- [3. Enums](#3-enums)
- [4. Models](#4-models)
- [5. DTOs](#5-dtos)
- [6. Data (EF Core DbContext & Seed)](#6-data-ef-core-dbcontext-seed)
- [7. Services (Business Logic)](#7-services-business-logic)
- [8. Controllers (API Endpoints)](#8-controllers-api-endpoints)

---

## 1. Project File

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>aspnet-PharmacyV2-916790b7-3f41-4972-a753-392f8d6836f7</UserSecretsId>
  <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.10" NoWarn="NU1605" />
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.10" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.18" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="10.0.10">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Identity.Web" Version="4.14.2" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="7.2.0" />
  </ItemGroup>

  <ItemGroup>
    <Folder Include="wwwroot\images\sale-receipts\" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove="PharmacyV2.Tests\**\*.cs" />
  </ItemGroup>

</Project>
```
## 2. Architecture & Business Logic Overview

**PharmacyV2** is a layered ASP.NET Core Web API for running a pharmacy: purchasing, inventory/stock, sales & prescriptions, suppliers, HR, and basic accounting.

**Layers**
- **Models** — EF Core entities, grouped by module folder (`Product`, `Purchase`, `SaleInvoice`, `People`, `Payment`, `Branch`, `HR`, `Accounts`, `Other`).
- **Data** — `PharmacyDbContext` (all `DbSet`s + Fluent API configuration) and `SeedData` (initial lookup/reference data).
- **DTOs** — request/response shapes per module, keeping API contracts separate from EF entities.
- **Services** — small set of cross-cutting business services (see below); most CRUD/business logic otherwise lives directly in the Controllers.
- **Controllers** — one controller per resource, exposing REST endpoints (`[ApiController]`, route `api/[controller]`), generally calling `PharmacyDbContext` directly via EF Core.
- **Enums** — shared enums (`PurchaseOrderSource`, `PurchaseOrderStatus`, `DepreciationMethod`, `LiabilityType`, `LedgerSourceType`).

**Core business flows**

1. **Product & Stock** — `Product` → `ProductDetails`/`ProductPrice`/`ProductPriceHistory` (pricing history is tracked separately from live price), `ProductStock` (batch/expiry-level on-hand quantities per warehouse), `ExpiredProductStock` (write-offs), `ProductRak` (shelf/rack location), `DailyPurchaseRequirementTable` (auto reorder suggestions feeding into `PurchaseOrder`).
2. **Purchasing** — `PurchaseOrder` (status machine: `Draft → PendingCheck → Checked → Converted/Cancelled`, sourced `Auto` from the daily requirement table or `Manual`) → `PurchaseInvoice`/`PurchaseInvoiceItem` (actual received goods, updates `ProductStock`) → `PurchaseReturn`/`PurchaseReturnItem`/`PurchaseReturnReceive` for returns to suppliers, plus `SupplierPayment`/`SupplierPaymentDetail` for AP settlement.
3. **Sales** — `Sale`/`SaleItem`/`SalePayment` (point-of-sale invoice + payment capture, decrements `ProductStock`), `SaleReturn`/`SaleReturnItem`/`SaleReturnRefund` for customer returns/refunds, optionally linked to a `Doctor`/`Prescription`/`PrescriptionItem`.
4. **People** — `Customer`/`CustomerAddress`/`CustomerType`, `Supplier`/`SupplierContact`/`SupplierType`, `SupplierProduct`/`SupplierProductPrice` (which suppliers sell which products, at what price), `Doctor`.
5. **Branch/Warehouse** — `Warehouse`/`WarehouseType`, `StockTransfer`/`StockTransferItem` for moving stock between warehouses.
6. **HR** — `Department`, `Employee`, `EmployeeDocument`.
7. **Accounting** — `ChartOfAccount`, `LedgerAccount` (double-entry postings with a running balance), `CompanyAsset` (with depreciation method), `CompanyLiability`. `AccountingPostingService` is the single gateway used to write ledger entries:
   - `EnsureSystemAccountsAsync()` seeds a fixed chart of accounts (Cash, Bank, AR, Inventory, Fixed Assets, AP, Sales Revenue, COGS, etc.) if missing.
   - `PostAsync(...)` posts a balanced set of debit/credit lines for a given `LedgerSourceType` + `SourceId` (idempotent — skips if that source was already posted), then recalculates running balances.
   - `RecalculateRunningBalancesAsync(...)` replays every ledger row for the affected accounts in date/Id order to keep `RunningBalance` correct even for backdated entries.
8. **Auth** — `AppUser`/`Role`/`RolePermission`, JWT issuance via `ITokenService`/`TokenService`, `AuthController` for login/register.
9. **Cross-cutting services**
   - `CodeGeneratorService` — generates human-readable, collision-checked codes/numbers (`PRD-XXXXXXXX` product codes, `PINV-yyyyMMdd-XXXXXXXX` purchase invoice numbers, `SINV-yyyyMMdd-XXXXXXXX` sale invoice numbers, `BATCH-XXXXXXXX` batch numbers) using an 8-char uppercase hex slice of a `Guid`, retried up to 10 times against the DB for uniqueness.
   - `AuditService` — writes `AuditLog` entries for tracked actions.
   - `SmsLog` — records outbound SMS notifications (e.g., order/payment alerts).

**Conventions observed across controllers**
- Standard REST shape per resource: `GET` (list, with filtering/paging where applicable), `GET {id}`, `POST`, `PUT {id}`, `DELETE {id}`.
- DTOs are mapped to/from entities inside the controller action rather than via AutoMapper.
- Master/detail resources (e.g., Customer + CustomerAddress, Supplier + SupplierContact, PurchaseOrder + Items) are typically saved together in one request.


---

## 3. Enums

### `Enum.cs`

```csharp
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
```


## 4. Models

### `Accounts/ChartOfAccount.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    public class ChartOfAccount
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Code { get; set; }

        [Required, MaxLength(50)]
        public string AccountType { get; set; } = string.Empty;

        public int? ParentId { get; set; }
        public ChartOfAccount? Parent { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; } = false;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? BudgetAmount { get; set; }

        public ICollection<ChartOfAccount> Children { get; set; } = new List<ChartOfAccount>();
        public ICollection<LedgerAccount> LedgerAccounts { get; set; } = new List<LedgerAccount>();
    }
}
```

### `Accounts/CompanyAsset.cs`

```csharp
using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    public class CompanyAsset
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Value { get; set; }

        public DateTime AcquiredDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SalvageValue { get; set; } = 0;

        public int UsefulLifeYears { get; set; } = 0;

        [Column(TypeName = "decimal(5,2)")]
        public decimal DepreciationRatePercent { get; set; } = 0;

        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AccumulatedDepreciation { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BookValue { get; set; }

        public DateTime? NextDepreciationDate { get; set; }
        public DateTime? DisposalDate { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

### `Accounts/CompanyLiability.cs`

```csharp
using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    public class CompanyLiability
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public LiabilityType LiabilityType { get; set; } = LiabilityType.Other;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidAmount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal OutstandingAmount { get; set; }

        public DateTime LiabilityDate { get; set; }
        public DateTime? DueDate { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal InterestRate { get; set; } = 0;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

### `Accounts/LedgerAccount.cs`

```csharp
using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Accounts
{
    // The General Ledger. ChartOfAccount defines *what* accounts exist
    // (Cash, Sales Revenue, Accounts Payable, ...); LedgerAccount holds the
    // individual debit/credit postings *against* those accounts, e.g. one row
    // per Sale, PurchaseInvoice, SupplierPayment, manual journal entry, etc.
    // Every real-world transaction should post here as one or more balanced
    // double-entry rows (total debits == total credits per transaction).
    public class LedgerAccount
    {
        public int Id { get; set; }

        public int ChartOfAccountId { get; set; }
        public ChartOfAccount ChartOfAccount { get; set; } = null!;

        // Groups the rows that make up a single balanced double-entry transaction
        // (e.g. all lines of one Sale share the same VoucherNo).
        [Required, MaxLength(50)]
        public string VoucherNo { get; set; } = string.Empty;

        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DebitAmount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditAmount { get; set; } = 0;

        // Running balance of the owning ChartOfAccount immediately after this
        // posting — denormalized for fast ledger/statement views, same pattern
        // as PurchaseInvoice.Total / SupplierPayment.TotalAmount elsewhere.
        [Column(TypeName = "decimal(18,2)")]
        public decimal RunningBalance { get; set; } = 0;

        [MaxLength(500)]
        public string? Description { get; set; }

        // Where this posting came from, e.g. Sale, PurchaseInvoice, SupplierPayment.
        public LedgerSourceType SourceType { get; set; } = LedgerSourceType.Manual;

        // Id of the originating document (SaleId, PurchaseInvoiceId, ...).
        // Deliberately not a real FK: the source table varies with SourceType,
        // and several of those modules (Sale, PurchaseInvoice, ...) aren't wired
        // into PharmacyDbContext yet either.
        public int? SourceId { get; set; }

        public bool IsReversed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedByUserId { get; set; }
    }
}
```

### `Branch/StockTransfer.cs`

```csharp
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
```

### `Branch/StockTransferItem.cs`

```csharp
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
```

### `Branch/Warehouse.cs`

```csharp
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
```

### `Branch/WarehouseType.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Branch
{
    // Lookup table for Warehouse.WarehouseTypeId. No controller — seeded via
    // HasData() in PharmacyDbContext.
    public class WarehouseType
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        public List<Warehouse> Warehouses { get; set; } = new();
    }
}
```

### `HR/Department.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.HR
{
    // Lookup table used as the dropdown source for Employee.DepartmentId.
    // Intentionally has no controller — it's reference data only, seeded
    // once via HasData() in PharmacyDbContext.OnModelCreating.
    public class Department
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public List<Employee> Employees { get; set; } = new();
    }
}
```

### `HR/Employee.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.HR
{
    // Master record. Demonstrates the six required data types in one entity:
    // text, number, date, boolean, image and relational (dropdown).
    // EmployeeDocument (below) is the "details" side of the master-details pair.
    public class Employee
    {
        [Key]
        public int Id { get; set; }

        // ----- text -----
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Email { get; set; }

        // ----- number -----
        [Column(TypeName = "decimal(18,2)")]
        public decimal Salary { get; set; }

        // ----- date -----
        public DateTime HireDate { get; set; }

        // ----- boolean -----
        public bool IsActive { get; set; } = true;

        // ----- image -----
        // Raw image bytes. System.Text.Json reads/writes byte[] as a base64
        // string automatically, so Postman just sends/receives base64 text
        // in the JSON body — no extra file-upload plumbing needed.
        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; } // e.g. "image/png", "image/jpeg"

        // Public URL path to the physical file under wwwroot/images/employees,
        // e.g. "/images/employees/3_a1b2c3d4.jpg". Set by the dedicated
        // POST /api/employees/{id}/photo upload endpoint — same pattern as
        // Product.ImagePath.
        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        // ----- relational data (dropdown) -----
        // Same pattern as Product.UnitId -> Unit: the client picks a
        // DepartmentId from a dropdown populated by the seeded Department rows.
        public int DepartmentId { get; set; }
        public Department Department { get; set; } = null!;

        // ----- details side of the master-details pair -----
        public ICollection<EmployeeDocument> Documents { get; set; } = new List<EmployeeDocument>();
    }
}
```

### `HR/EmployeeDocument.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.HR
{
    // Detail record — many documents belong to one Employee (master).
    // Managed entirely through EmployeesController (nested create/update,
    // plus dedicated /documents endpoints), same as Product/ProductDetails.
    public class EmployeeDocument
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        public Employee Employee { get; set; } = null!;

        [Required, MaxLength(150)]
        public string DocumentTitle { get; set; } = string.Empty; // e.g. "National ID", "Certificate"

        [MaxLength(100)]
        public string? DocumentNumber { get; set; }

        public DateTime IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public bool IsVerified { get; set; } = false;
    }
}
```

### `Other/AppUser.cs`

```csharp
using PharmacyV2.Models.Branch;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int? RoleId { get; set; }
        public Role? Role { get; set; }

        public ICollection<Sale> SalesCreated { get; set; } = new List<Sale>();
        public ICollection<PurchaseInvoice> PurchasesCreated { get; set; } = new List<PurchaseInvoice>();
        public ICollection<StockTransfer> TransfersCreated { get; set; } = new List<StockTransfer>();
    }
}
```

### `Other/AuditLog.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other;

public class AuditLog
{
    public long Id { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    public string? UserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
```

### `Other/Role.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class Role
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
        public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    }
}
```

### `Other/RolePermission.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class RolePermission
    {
        public int Id { get; set; }

        public int RoleId { get; set; }
        public Role Role { get; set; } = null!;

        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;   

        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }
}
```

### `Other/SmsLog.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Other
{
    public class SmsLog
    {
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.Now;

        public bool IsSuccess { get; set; }

        [MaxLength(500)]
        public string? Response { get; set; }
    }
}
```

### `Payment/PaymentMethod.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Payment
{
    public class PaymentMethod
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;   // "Cash", "Card", "Mobile Banking"

        [MaxLength(20)]
        public string? LedgerAccountCode { get; set; }      // e.g. "1000", "1010", "1020"

        public bool IsActive { get; set; } = true;
    }
}
```

### `Payment/SupplierPayment.cs`

```csharp
using PharmacyV2.Models.People;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Payment
{
    // "Paid" sheet, top block — the saved payment voucher header (Paid No e.g. A00001).
    // The "Payment" sheet is the staging screen that lists a supplier's outstanding
    // purchases (Due/Pre-Paid/Paid) before "Save & print" writes them here as a
    // SupplierPayment + its SupplierPaymentDetail allocation rows.
    public class SupplierPayment
    {
        [Key]
        public int Id { get; set; }

        // "Paid No" e.g. A00001
        [Required, MaxLength(50)]
        public string PaidNo { get; set; } = string.Empty;

        // "Paid date"
        public DateTime PaidDate { get; set; } = DateTime.Now;

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        // Sum of Details[].PaidAmount — kept denormalized for fast list/print views,
        // same pattern as PurchaseInvoice.Total.
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ----- boolean -----
        // True if this voucher was voided/reversed after being saved (e.g. a
        // data-entry mistake or a bounced payment). False (default) for a
        // normal, standing payment. Mirrors LedgerAccount.IsReversed.
        public bool IsCancelled { get; set; } = false;

        // Scanned/photographed proof of payment (bank slip, cash receipt, mobile
        // banking screenshot, etc.) attached to the voucher. Follows the same
        // optional path/bytes/content-type pattern as Product's image fields —
        // all three are nullable and default to null, so existing rows and any
        // code that doesn't supply a receipt keep working without change.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<SupplierPaymentDetail> Details { get; set; } = new List<SupplierPaymentDetail>();
    }
}
```

### `Payment/SupplierPaymentDetail.cs`

```csharp
using PharmacyV2.Models.Purchase;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Payment
{
    // "Paid Details Table" (also mirrors the row shape of the "Payment" sheet).
    // Many-to-one -> SupplierPayment (one voucher can cover several purchase invoices).
    // Many-to-one -> PurchaseInvoice (one invoice can, in turn, be paid off across
    // several vouchers if it's settled in installments), so this table is the
    // classic many-to-many join/allocation entity between the two.
    public class SupplierPaymentDetail
    {
        public int Id { get; set; }

        public int SupplierPaymentId { get; set; }
        public SupplierPayment SupplierPayment { get; set; } = null!;

        public int PurchaseInvoiceId { get; set; }
        public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

        // "Amount No" — the running/sequence amount id used on the printed voucher line
        public int LineNo { get; set; }

        // Snapshot of the invoice's total at the moment of payment
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        // Outstanding "Due" on the invoice just before this allocation was applied
        [Column(TypeName = "decimal(18,2)")]
        public decimal DueBeforePayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PrePaid { get; set; }

        // "Paid" — amount actually settled by this allocation row
        [Column(TypeName = "decimal(18,2)")]
        public decimal PaidAmount { get; set; }
    }
}
```

### `People/Customer.cs`

```csharp
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    public class Customer
    {
        public int CustomerId { get; set; }

        // ----- text -----
        [MaxLength(50)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? LastName { get; set; }
        public string? Phone { get; set; } // UNIQUE — configured in Fluent API

        // ----- number -----
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal CreditLimit { get; set; } = 0;

        // ----- date -----
        public DateTime? DateOfBirth { get; set; }

        // ----- boolean -----
        public bool IsActive { get; set; } = true;

        // ----- image -----
        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        // Public URL path to the physical file under wwwroot/images/customers,
        // e.g. "/images/customers/3_a1b2c3d4.jpg". Set by the dedicated
        // POST /api/customers/{id}/photo upload endpoint — same pattern as
        // Employee.PhotoPath.
        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        // ----- relational data (dropdown) -----
        public int CustomerTypeId { get; set; }
        public CustomerType CustomerType { get; set; } = null!;

        // ----- details side of the master-details pair -----
        public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();

        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    }
}
```

### `People/CustomerAddress.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    // Detail record — many addresses belong to one Customer (master).
    // Managed through CustomersController (nested create/update, plus
    // dedicated /addresses endpoints), same shape as Employee/EmployeeDocument.
    public class CustomerAddress
    {
        [Key]
        public int Id { get; set; }

        public int CustomerId { get; set; }
        public Customer Customer { get; set; } = null!;

        [Required, MaxLength(50)]
        public string Label { get; set; } = string.Empty; // e.g. "Home", "Office"

        [Required, MaxLength(250)]
        public string AddressLine { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? City { get; set; }

        public bool IsDefault { get; set; } = false;
    }
}
```

### `People/CustomerType.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    // Lookup table for Customer.CustomerTypeId. No controller — seeded via
    // HasData() in PharmacyDbContext, same pattern as Unit and Department.
    public class CustomerType
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        public List<Customer> Customers { get; set; } = new();
    }
}
```

### `People/Doctor.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    public class Doctor
    {
        public int DoctorId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Specialization { get; set; }

        [MaxLength(100)]
        public string? RegistrationNo { get; set; }

        [MaxLength(150)]
        public string? Hospital { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    }
}
```

### `People/Prescription.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using PharmacyV2.Models.SaleInvoice;

namespace PharmacyV2.Models.People
{
    public class Prescription
    {
        public int PrescriptionId { get; set; }

        public int DoctorId { get; set; }
        public Doctor Doctor { get; set; } = null!;

        public int CustomerId { get; set; }
        public Customer Customer { get; set; } = null!;

        public DateTime PrescriptionDate { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? Diagnosis { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [MaxLength(500)]
        public string? ImagePath { get; set; }
        public byte[]? Image { get; set; }
        [MaxLength(100)]
        public string? ImageContentType { get; set; }

        public int? SaleId { get; set; }
        public Sale? Sale { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PrescriptionItem> Items { get; set; } = new List<PrescriptionItem>();
    }
}
```

### `People/PrescriptionItem.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    public class PrescriptionItem
    {
        public int Id { get; set; }

        public int PrescriptionId { get; set; }
        public Prescription Prescription { get; set; } = null!;

        public int? ProductId { get; set; }
        public PharmacyV2.Models.Product.Product? Product { get; set; }

        [Required, MaxLength(200)]
        public string MedicineName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Dosage { get; set; }

        [MaxLength(100)]
        public string? Duration { get; set; }

        [MaxLength(300)]
        public string? Instructions { get; set; }
    }
}
```

### `People/Supplier.cs`

```csharp
using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.Payment;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    public class Supplier
    {
        public int SupplierId { get; set; }

        [Required, MaxLength(100)]
        public string SupplierName { get; set; } = null!;

        [MaxLength(100)]
        public string? ContactPerson { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? Email { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        public DateTime CreatedAt { get; set; }
        public bool Distributor { get; set; } = false;

        // ----- number -----
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal OpeningBalance { get; set; } = 0;

        // ----- image -----
        public byte[]? Logo { get; set; }

        [MaxLength(100)]
        public string? LogoContentType { get; set; }

        [MaxLength(200)]
        public string? LogoPath { get; set; }

        // ----- relational data (dropdown) -----
        public int SupplierTypeId { get; set; }
        public SupplierType SupplierType { get; set; } = null!;

        public int? CompanyId { get; set; }
        public Company? Company { get; set; }

        // ----- details side of the master-details pair -----
        public ICollection<SupplierContact> Contacts { get; set; } = new List<SupplierContact>();

        public ICollection<ProductStock> ProductStocks { get; set; } = new List<ProductStock>();
        public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
        public ICollection<PurchaseInvoice> Purchases { get; set; } = new List<PurchaseInvoice>();
        public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();

        // The actual Supplier <-> Product many-to-many: every product this
        // supplier is linked to, each with its own per-unit pricing.
        public ICollection<SupplierProduct> SupplierProducts { get; set; } = new List<SupplierProduct>();
    }
}
```

### `People/SupplierContact.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    // Detail record — many contacts belong to one Supplier (master).
    // Managed through SuppliersController (nested create/update, plus
    // dedicated /contacts endpoints).
    public class SupplierContact
    {
        [Key]
        public int Id { get; set; }

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        [Required, MaxLength(100)]
        public string ContactName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Designation { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        public bool IsPrimary { get; set; } = false;
    }
}
```

### `People/SupplierProduct.cs`

```csharp
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.People
{
    // The actual Supplier <-> Product many-to-many link. Before this, a
    // Supplier only ever touched a Product indirectly through a ProductStock
    // batch or a PurchaseInvoiceItem line — there was no direct "this
    // supplier carries these products" / "this product is supplied by these
    // suppliers" relationship, and no single place to hang per-supplier,
    // per-unit pricing off of.
    //
    // One row = "Supplier X supplies Product Y" (unique per SupplierId+ProductId).
    // The actual prices — which differ per Unit (Pcs/Box/Strip/...) and must
    // NOT just fall back to the product's flat master price — live on the
    // child SupplierProductPrice rows.
    public class SupplierProduct
    {
        [Key]
        public int Id { get; set; }

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        public int ProductId { get; set; }
        public PharmacyV2.Models.Product.Product Product { get; set; } = null!;

        // Supplier's own code/reference for this product, if any.
        [MaxLength(50)]
        public string? SupplierProductCode { get; set; }

        // Lets a pharmacy mark which supplier is the go-to source for a
        // product when several suppliers carry it.
        public bool IsPreferred { get; set; } = false;

        public bool IsActive { get; set; } = true;

        // Rolled up / refreshed whenever a PurchaseInvoiceItem is posted
        // against this supplier+product, so "purchase history" is visible
        // at a glance without re-aggregating invoices every time.
        public DateTime? LastPurchaseDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? LastPurchaseUnitCost { get; set; }
        public int? LastPurchaseUnitId { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Current price per unit (Pcs / Box / Strip / Vial ...). History of
        // changes to these is written to ProductPriceHistory (SupplierId set).
        public ICollection<SupplierProductPrice> Prices { get; set; } = new List<SupplierProductPrice>();
    }
}
```

### `People/SupplierProductPrice.cs`

```csharp
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.People
{
    // One row per (SupplierProduct, Unit) — this is what makes Napa's Pcs
    // price, Box price, Strip price, Vial price etc. all genuinely
    // independent numbers instead of everything deriving from the master
    // Product row. When a purchase invoice line is entered for this
    // Supplier + Product + Unit, PurchasePrice here is the default UnitCost,
    // and SalePrice/UnitPrice here (NOT Product.SalePrice) is what should be
    // offered as the selling price for stock received in that unit.
    public class SupplierProductPrice
    {
        [Key]
        public int Id { get; set; }

        public int SupplierProductId { get; set; }
        public SupplierProduct SupplierProduct { get; set; } = null!;

        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;

        // How many of the product's base unit (Pcs) this Unit represents —
        // e.g. Box = 100, Strip = 10. Mirrors ProductPrice.BaseQuantity so
        // stock/quantity math is consistent regardless of which table priced
        // the line. Defaults to the Product's own ProductPrice.BaseQuantity
        // for the same Unit when not explicitly overridden.
        [Column(TypeName = "decimal(18,4)")]
        public decimal BaseQuantity { get; set; } = 1;

        // What this supplier charges the pharmacy for one of this Unit.
        [Column(TypeName = "decimal(18,4)")]
        public decimal PurchasePrice { get; set; }

        // What the pharmacy sells one of this Unit for (retail).
        [Column(TypeName = "decimal(18,4)")]
        public decimal? SalePrice { get; set; }

        // General list/unit price shown on quotes, separate from the retail
        // sale price — mirrors Product.UnitPrice but scoped to this
        // supplier + unit instead of one flat number for the whole product.
        [Column(TypeName = "decimal(18,4)")]
        public decimal? UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? DistributorPrice { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
```

### `People/SupplierType.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.People
{
    // Lookup table for Supplier.SupplierTypeId. No controller — seeded via
    // HasData() in PharmacyDbContext.
    public class SupplierType
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        public List<Supplier> Suppliers { get; set; } = new();
    }
}
```

### `Product/Company.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Product
{
    // Lookup table for the "Company" filter on the Brand Search / Brand List
    // pages (medex.com.bd-style: Company column + Company filter dropdown).
    // Replaces ProductDetails.Manufacturer (free text) as the source of truth
    // going forward — Manufacturer is left in place for backward compatibility
    // and is kept in sync from Company.Name when a product is saved with a
    // CompanyId.
    public class Company
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public List<Product> Products { get; set; } = new();
    }
}
```

### `Product/DailyPurchaseRequirementTable.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Product
{
    public class DailyPurchaseRequirementTable
    {
        [Key]
        public Guid SerialNo { get; set; } 
        public int ProductId { get; set; }
        public Product Product { get; set; } = null!; 
        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;
        public decimal LastPurchaseRate { get; set; } 
        public decimal StockQty { get; set; } 
        public decimal RequiredQty { get; set; } 
        public decimal OrdersQty { get; set; } 
    }
}
```

### `Product/DosageForm.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Product
{
    // Lookup table for the "Dosage Form" filter on the Brand Search page
    // (Tablet, Oral Suspension, Injection, Suppository, IV Infusion, etc.).
    // Seeded with common forms like Unit, extendable via DosageFormsController.
    public class DosageForm
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public List<Product> Products { get; set; } = new();
    }
}
```

### `Product/ExpiredProductStock.cs`

```csharp
using PharmacyV2.Models.Branch;
using PharmacyV2.Models.Other;
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace PharmacyV2.Models.Product
{
    public class ExpiredProductStock
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;
        public int ProductStockId { get; set; }
        public ProductStock ProductStock { get; set; } = null!;

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = null!;


        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        [MaxLength(100)]
        public string DisposalMethod { get; set; } = "Returned";

        public DateTime DisposalDate { get; set; } = DateTime.Now;

        public int ApprovedByUserId { get; set; }
        public AppUser ApprovedBy { get; set; } = null!;

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
```

### `Product/Product.cs`

```csharp
using PharmacyV2.Models.People;
using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Product;
//Main Table for Product
public class Product
{
    [Key]
    public int Id { get; set; }                  //1
    [MaxLength(50)]
    public string? ProductCode { get; set; }        //Napa-500

    [Required, MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;      //Napa 500mg Tablet (Brand Name)
    public string Strength { get; set; } = string.Empty;         //500mg

    // Generic/composition name, e.g. "Paracetamol" — this is what the
    // Brand Search page (P2) matches against alongside ProductName, the
    // same way medex.com.bd lets you search "Paracetamol" and see every
    // brand (A-One, Ace, Napa...) that shares that generic.
    [MaxLength(200)]
    public string? GenericName { get; set; }

    [MaxLength(50)]
    public string? Barcode { get; set; }

    // "Allopathic" or "Herbal" — mirrors medex.com.bd's Browse menu split
    // (Brand Names / Generics both come in Allopathic and Herbal flavors).
    // Kept as a plain string rather than an enum column so existing rows
    // default cleanly to "Allopathic" without a data migration.
    [MaxLength(20)]
    public string BrandType { get; set; } = "Allopathic";

    // Company (manufacturer) as a proper lookup so it's filterable on the
    // Brand Search page, instead of only living as free text on
    // ProductDetails.Manufacturer.
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    // Dosage form (Tablet, Syrup, Injection, Suppository, ...) — also
    // filterable on the Brand Search page.
    public int? DosageFormId { get; set; }
    public DosageForm? DosageForm { get; set; }

    // A product may be linked to the catalogue variant that supplied its
    // name, strength, company and dosage form. Null keeps all old rows valid.
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;              //pcs 
    //dropdown box showing all units of the product, 

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }               //5 tk per pcs 

    [Column(TypeName = "decimal(18,2)")]
    public decimal PurchasePrice { get; set; }          //4.5 tk per pcs

    [Column(TypeName = "decimal(18,2)")]
    public decimal? DistributorPrice { get; set; }       //4.0 tk per pcs

    [Column(TypeName = "decimal(18,2)")]
    public decimal? SalePrice { get; set; }           // Sale Price

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    // true "image" data type — raw bytes, base64 in JSON. ImagePath above is
    // kept for backward compatibility (external/static file path).
    public byte[]? ProductImage { get; set; }

    [MaxLength(100)]
    public string? ProductImageContentType { get; set; }

    // "date" data type on the master record.
    public DateTime RegisteredDate { get; set; } = DateTime.UtcNow;

    // ----- boolean -----
    // Nullable, defaults to null (not true/false) so existing rows and any
    // caller that doesn't supply it keep working without a migration error
    // or a NOT NULL violation.
    public bool? IsActive { get; set; } = null;

    public int StockQuantity { get; set; } = 0;

    public int MinStockQty { get; set; } = 100;

    public int MaxStockQty { get; set; } = 10000;
    public int PurchaseQty { get; set; } = 0;

    public ICollection<ProductStock> ProductStocks { get; set; } = new List<ProductStock>();
    public ICollection<ProductRak> ProductRaks { get; set; } = new List<ProductRak>();
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<PurchaseOrderItem> PurchaseItems { get; set; } = new List<PurchaseOrderItem>();
    public ProductDetails? ProductDetails { get; set; }
    public ICollection<ProductPrice> ProductPrices { get; set; } = new List<ProductPrice>();

    // Every supplier this product is linked to, many-to-many via
    // SupplierProduct, each with its own per-unit purchase/sale price.
    public ICollection<SupplierProduct> SupplierProducts { get; set; } = new List<SupplierProduct>();
}
```

### `Product/ProductDetails.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Product
{
    // This class represents the details of a product in the pharmacy system.
    public class ProductDetails
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;
        public string? Manufacturer { get; set; }

        [MaxLength(50)]
        public string? Schedule { get; set; } // H, H1, X etc.

        [MaxLength(100)]
        public string? DarNo { get; set; } // Drug Administration Registration

        public string StorageConditions { get; set; } = string.Empty;   

        [Column(TypeName = "decimal(5,2)")]
        public decimal? TemperatureMin { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? TemperatureMax { get; set; }

        //[MaxLength(1000)]
        //public string? Composition { get; set; }

        [MaxLength(1000)]
        public string? SideEffects { get; set; }

        [MaxLength(50)]
        public string? PregnancyCategory { get; set; }
        public bool RequiresPrescription { get; set; } = false;
        public bool IsControlledDrug { get; set; } = false;
    }
}
```

### `Product/ProductGroup.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Product;

/// <summary>
/// A medicine/brand family.  Variants beneath a group hold the valid
/// strength + dosage-form combinations, so cashiers cannot invent an
/// invalid combination while adding stock products.
/// </summary>
public class ProductGroup
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? GenericName { get; set; }

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public List<ProductVariant> Variants { get; set; } = new();
}
```

### `Product/ProductPrice.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Rendering;
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Product
{
    // This class represents the price of a product for a specific unit.
    public class ProductPrice
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;

        public int UnitId { get; set; }
        public virtual Unit Unit { get; set; } = null!;      
        //dropdown box showing all units of the product, user can select any unit for price entry like box, strip, bottle etc. and then enter the price for that unit.

        // A pack can have a meaningful name beyond its unit, for example
        // "60 ml bottle (Raspberry)".  This also permits multiple Bottle
        // variants for one product, each with its own price.
        [Required, MaxLength(150)]
        public string DisplayName { get; set; } = string.Empty;

        // General list/unit price for this unit (kept for backward
        // compatibility with existing callers that only ever set one price).
        [Column(TypeName = "decimal(18,4)")]
        public decimal PerUnitPrice { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal BaseQuantity { get; set; } = 1;
        //according to the selected unit, the price will be entered for that unit. For example, if the user selects "box" as the unit, then the price entered will be for one box of the product.

        // Purchase (cost) price and sale (retail) price, independent per
        // unit. This is what fixes the "everything falls back to the Pcs
        // price" bug: a Box no longer inherits PurchasePrice/SalePrice from
        // the master Product row, it has its own values here. Nullable so
        // existing rows / callers that only fill PerUnitPrice keep working —
        // when null, callers should fall back to PerUnitPrice.
        [Column(TypeName = "decimal(18,4)")]
        public decimal? PurchasePrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? SalePrice { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? DistributorPrice { get; set; }
    }
}
```

### `Product/ProductPriceHistory.cs`

```csharp
using PharmacyV2.Models.People;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Product;

public class ProductPriceHistory
{
    [Key] public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [MaxLength(30)] public string PriceType { get; set; } = string.Empty;
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    // When set, this history row is scoped to one supplier's price for this
    // product+unit (SupplierProductPrice changes) rather than the product's
    // own master/packaging price. Null = product-level change, as before.
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? PreviousPrice { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NewPrice { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? PreviousBaseQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? NewBaseQuantity { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
```

### `Product/ProductRak.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using PharmacyV2.Models.Branch;

namespace PharmacyV2.Models.Product
{
    // This class represents a product rack in a warehouse.
    public class ProductRak
    {
        [Key]
        public int Id { get; set; }

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = null!;

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public ICollection<ProductStock> ProductStocks { get; set; } = new List<ProductStock>();
    }
}
```

### `Product/ProductStock.cs`

```csharp
using PharmacyV2.Models.Branch;
using PharmacyV2.Models.People;
using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Product
{
    // This class represents the stock of a product in a specific warehouse, including details such as batch number, quantity, expiry date, and related entities like supplier and purchase invoice.
    public class ProductStock
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = null!;
        public int? ProductRakId { get; set; }
        public ProductRak? ProductRak { get; set; }

        public string BatchNumber { get; set; } = null!; // part of composite UNIQUE (ProductId, BatchNumber)

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; } = 0;

        public DateOnly ExpiryDate { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal AvailableQuantity { get; set; } = 0;
        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public int? PurchaseInvoiceId { get; set; }
        public PurchaseInvoice? PurchaseInvoice { get; set; }

        public DateOnly ReceivedDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
        public ICollection<StockTransferItem> StockTransferItems { get; set; } = new List<StockTransferItem>();
        public ICollection<ExpiredProductStock> ExpiredProducts { get; set; } = new List<ExpiredProductStock>();
    }
}
```

### `Product/ProductVariant.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Product;

/// <summary>A sellable formulation of a ProductGroup, e.g. Napa 500 mg Tablet.</summary>
public class ProductVariant
{
    [Key]
    public int Id { get; set; }

    public int ProductGroupId { get; set; }
    public ProductGroup ProductGroup { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Strength { get; set; } = string.Empty;

    public int DosageFormId { get; set; }
    public DosageForm DosageForm { get; set; } = null!;

    public List<Product> Products { get; set; } = new();
}
```

### `Product/Unit.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Product
{
    //This class represents a unit of measurement for products in the pharmacy system. It includes properties for the unit's ID, name, and collections of associated products and product prices.
    public class Unit
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
        public List<Product> Products { get; set; } 
        public List<ProductPrice> ProductPrices { get; set; }
        public Unit()
        {
            Products = new List<Product>();
            ProductPrices = new List<ProductPrice>();
        }
    }
}
```

### `Purchase/PurchaseInvoice.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Rendering;
using PharmacyV2.Models.Branch;
using PharmacyV2.Models.People;
using PharmacyV2.Models.Payment;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Purchase
{
    public class PurchaseInvoice
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string InvoiceNo { get; set; } = string.Empty;

        public DateTime PurchaseDate { get; set; } = DateTime.Now;

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        // "Purchase" sheet's "Order No(Fk)" — links the invoice back to the order it
        // was raised from (via the Order Checking screen). Nullable: a purchase can
        // still be entered directly without going through the order workflow.
        public int? PurchaseOrderId { get; set; }
        public PurchaseOrder? PurchaseOrder { get; set; }

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; } = null!;
        public string PaymentMethod { get; set; } = "Cash";

        [Column(TypeName = "decimal(18,2)")]
        public decimal Advance { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Due { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Discount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TaxOrOthers { get; set; } = 0;

        public string PaymentStatus { get; set; } = "InComplete";

        public bool IsSupplierWise { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Scanned/photographed supplier receipt or bill attached to this invoice.
        // Same optional path/bytes/content-type pattern as Product's image fields —
        // all three are nullable and default to null, so existing invoices and any
        // caller that doesn't supply a receipt keep working without change.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<PurchaseReturn> Returns { get; set; } = new List<PurchaseReturn>();

        // Details side of the PurchaseInvoice master-details pair — the
        // products/quantities actually received against this invoice.
        public ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();

        // "Payment" / "Paid Details" rows raised against this invoice — one invoice
        // can be paid off across several vouchers (installments).
        public ICollection<SupplierPaymentDetail> PaymentAllocations { get; set; } = new List<SupplierPaymentDetail>();
    }
}
```

### `Purchase/PurchaseInvoiceItem.cs`

```csharp
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Purchase
{
    // Detail record — many line items belong to one PurchaseInvoice (master).
    // This was previously missing: PurchaseInvoice had no way to record which
    // products/quantities were actually received against an invoice. Managed
    // through PurchaseInvoicesController (nested create/update, plus
    // dedicated /items endpoints), same shape as PurchaseOrder/PurchaseOrderItem.
    public class PurchaseInvoiceItem
    {
        public int Id { get; set; }

        public int PurchaseInvoiceId { get; set; }
        public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

        public int ProductId { get; set; }
        public Product.Product Product { get; set; } = null!;

        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        // Not DB-computed (unlike SaleItem.TotalPrice) — kept as a plain
        // column and set server-side in the controller as Quantity * UnitCost,
        // so it stays consistent without requiring a computed-column migration.
        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; }

        // Optional batch/expiry tracking captured at receiving time.
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }
}
```

### `Purchase/PurchaseOrder.cs`

```csharp
using PharmacyV2.Enums;
using PharmacyV2.Models.People;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.Models.Purchase
{
    // Where this comes from in the sheet: "Order System" (Order Main Table), "Manual Order"
    // and "Order Checking" tabs all edit/view the same header record — a purchase order
    // raised against a supplier, either generated automatically from
    // DailyPurchaseRequirementTable or entered manually via the "Manual Order" screen.

    public class PurchaseOrder
    {
        [Key]
        public int Id { get; set; }

        // "Order No" e.g. X000001 / O000001
        [Required, MaxLength(50)]
        public string OrderNo { get; set; } = string.Empty;

        // "Order Date(Hidden) / sysDate"
        public DateTime OrderDate { get; set; } = DateTime.Now;

        // "Requirement Date" — pulled from DailyPurchaseRequirementTable when auto-generated
        public DateTime? RequirementDate { get; set; }

        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = null!;

        // "Supplier Category:" dropdown shown next to the order header
        [MaxLength(100)]
        public string? SupplierCategory { get; set; }

        public PurchaseOrderSource Source { get; set; } = PurchaseOrderSource.Manual;
        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

        // ----- boolean -----
        // Priority flag set by the buyer, independent of the Status workflow
        // above (Draft/PendingCheck/Checked/Converted/Cancelled) — lets an
        // urgent order be picked out of a list without changing its stage.
        public bool IsUrgent { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Scanned/photographed order receipt/confirmation attached to this order.
        // Same optional path/bytes/content-type pattern as Product's image fields —
        // all three are nullable and default to null, so existing orders and any
        // caller that doesn't supply a receipt keep working without change.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();

        // One order is usually converted into one purchase invoice, but a supplier could
        // deliver in more than one batch against the same order, so keep it one-to-many.
        public ICollection<PurchaseInvoice> Purchases { get; set; } = new List<PurchaseInvoice>();
    }
}
```

### `Purchase/PurchaseOrderItem.cs`

```csharp
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Purchase
{
    // "Order Detail" rows from Order System / Manual Order / Order Checking.
    // Many-to-one back to PurchaseOrder (a PurchaseOrder has many items).
    public class PurchaseOrderItem
    {
        public int Id { get; set; }

        public int PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; } = null!;

        public int ProductId { get; set; }
        public Product.Product Product { get; set; } = null!;

        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;

        // "Last Purchase Price" snapshot at order time
        [Column(TypeName = "decimal(18,2)")]
        public decimal LastPurchasePrice { get; set; }

        // "StockQty" snapshot at order time
        [Column(TypeName = "decimal(18,2)")]
        public decimal StockQty { get; set; }

        // "RequiredQty/MinQty"
        [Column(TypeName = "decimal(18,2)")]
        public decimal RequiredQty { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OrderQty { get; set; }

        // Filled in on the "Order Checking" screen once goods are received
        [Column(TypeName = "decimal(18,2)")]
        public decimal? ReceivingQty { get; set; }

        // "Cancel Box"
        public bool IsCancelled { get; set; } = false;
    }
}
```

### `Purchase/PurchaseReturn.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Purchase
{
    public class PurchaseReturn
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string ReturnNo { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        public int PurchaseInvoiceId { get; set; }
        public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReturnTotal { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // ----- boolean -----
        // True once the return is fully processed — items handed back to the
        // supplier and the corresponding refund/credit received (see Receives
        // below). False (default) while it's still open/in progress.
        public bool IsCompleted { get; set; } = false;

        // Scanned/photographed return slip/credit note attached to this return.
        // Same optional path/bytes/content-type pattern as Product's image fields —
        // all three are nullable and default to null, so existing returns and any
        // caller that doesn't supply a receipt keep working without change.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
        public ICollection<PurchaseReturnReceive> Receives { get; set; } = new List<PurchaseReturnReceive>();
    }
}
```

### `Purchase/PurchaseReturnItem.cs`

```csharp
using PharmacyV2.Models.Product;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Purchase
{
    public class PurchaseReturnItem
    {
        public int Id { get; set; }

        public int PurchaseReturnId { get; set; }
        public PurchaseReturn PurchaseReturn { get; set; } = null!;

        public int ProductId { get; set; }
        public int PurchaseInvoiceItemId { get; set; }
        public Product.Product Product { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseQuantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; }
    }
}
```

### `Purchase/PurchaseReturnReceive.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.Purchase
{
    public class PurchaseReturnReceive
    {
        public int Id { get; set; }

        public int PurchaseReturnId { get; set; }
        public PurchaseReturn PurchaseReturn { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReceivedAmount { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        public string? ReceivedByUserId { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
```

### `SaleInvoice/Sale.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using PharmacyV2.Models.People;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Models.SaleInvoice
{
    public class Sale
    {
        public int SaleId { get; set; }

        // System-generated (GUID-backed, see ICodeGeneratorService), unique
        // per sale — the "Sale Invoice No" printed on the receipt, same idea
        // as PurchaseInvoice.InvoiceNo on the purchase side. Never set by
        // the client; SalesController.Create always fills it in.
        [MaxLength(50)]
        public string InvoiceNo { get; set; } = string.Empty;

        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }

        public DateTime SaleDate { get; set; }

        // Removed the single header-level UnitId — a sale can contain items
        // in different units (Pcs, Strip, Bottle...), so the unit now lives
        // per line item on SaleItem.UnitId instead.

        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "Cash";

        // NOTE: original CASHIER_ID had no FK — recommend wiring this to
        // ASP.NET Core Identity's user id (see ApplicationUser / recommendations doc)
        // instead of a bare, unconstrained NUMBER.
        public string? CashierId { get; set; }

        // ----- boolean -----
        // Nullable, defaults to null (not true/false) so existing sales and
        // any caller that doesn't supply it keep working without error.
        public bool? IsPaid { get; set; } = null;

        // Voiding preserves the invoice and audit trail; it is never a hard
        // delete because stock and ledger movements must be reversible.
        public bool IsVoided { get; set; } = false;
        [MaxLength(500)] public string? VoidReason { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidedByUserId { get; set; }

        // Scanned/photographed receipt attached to this sale. Same optional
        // path/bytes/content-type pattern as Product's image fields — all
        // three are nullable and default to null.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
        public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
    }

}
```

### `SaleInvoice/SaleItem.cs`

```csharp
using PharmacyV2.Models.Product;

namespace PharmacyV2.Models.SaleInvoice
{
    public class SaleItem
    {
        public int SaleItemId { get; set; }

        public int SaleId { get; set; }
        public Sale Sale { get; set; } = null!;

        public int ProductStockId { get; set; }
        public ProductStock ProductStocks { get; set; } = null!;

        // Moved here from Sale (header) so each line item can be sold in a
        // different unit — e.g. one medicine as "Strip", another as "Bottle",
        // within the same invoice. Same pattern as PurchaseInvoiceItem.UnitId
        // and SaleReturnItem.UnitId.
        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;

        public int Quantity { get; set; } // CHECK (QUANTITY > 0)

        public decimal BaseQuantity { get; set; } = 1;

        public decimal UnitPrice { get; set; }

        // GENERATED ALWAYS AS (QUANTITY * UNIT_PRICE) VIRTUAL -> SQL Server computed column
        // EF Core treats this as database-generated; do not set it from code.
        public decimal TotalPrice { get; private set; }
    }
}
```

### `SaleInvoice/SalePayment.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.SaleInvoice;

public class SalePayment
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, MaxLength(30)] public string PaymentMethod { get; set; } = "Cash";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? Note { get; set; }
    public string? ReceivedByUserId { get; set; }
}
```

### `SaleInvoice/SaleReturn.cs`

```csharp
using PharmacyV2.Models.SaleInvoice;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.SaleInvoice
{
    public class SaleReturn
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string ReturnNo { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        public int SaleId { get; set; }
        public Sale SaleInvoice { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReturnTotal { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
        public bool IsDeleted { get; set; } = false;

        // Scanned/photographed return slip attached to this return. Same
        // optional path/bytes/content-type pattern as Product's image
        // fields — all three are nullable and default to null.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
        public ICollection<SaleReturnRefund> Refunds { get; set; } = new List<SaleReturnRefund>();
    }
}
```

### `SaleInvoice/SaleReturnItem.cs`

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Models.SaleInvoice
{
    public class SaleReturnItem
    {
        public int Id { get; set; }

        public int SaleReturnId { get; set; }
        public SaleReturn SaleReturn { get; set; } = null!;

        public int MedicineId { get; set; }
        public int SaleItemId { get; set; }
        public Product.Product Medicine { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        public int UnitId { get; set; }
        public Unit Unit { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseQuantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SalesPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; }
    }
}
```

### `SaleInvoice/SaleReturnRefund.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmacyV2.Models.SaleInvoice;

public class SaleReturnRefund
{
    public int Id { get; set; }
    public int SaleReturnId { get; set; }
    public SaleReturn SaleReturn { get; set; } = null!;
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, MaxLength(30)] public string PaymentMethod { get; set; } = "Cash";
    public DateTime RefundedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? Note { get; set; }
    public string? RefundedByUserId { get; set; }
}
```


## 5. DTOs

### `Accountsdtos.cs`

```csharp
using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // ===================== ChartOfAccount =====================
    public class ChartOfAccountListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string AccountType { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public string? ParentName { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystem { get; set; }
        public decimal? BudgetAmount { get; set; }
        public int ChildCount { get; set; }
    }

    public class ChartOfAccountReadDto : ChartOfAccountListItemDto
    {
        public List<ChartOfAccountListItemDto> Children { get; set; } = new();
    }

    public class ChartOfAccountWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Code { get; set; }

        [Required, MaxLength(50)]
        public string AccountType { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public bool IsActive { get; set; } = true;

        [Range(0, double.MaxValue)]
        public decimal? BudgetAmount { get; set; }
    }

    // ===================== LedgerAccount (General Ledger) =====================
    public class LedgerLineReadDto
    {
        public int Id { get; set; }
        public int ChartOfAccountId { get; set; }
        public string ChartOfAccountName { get; set; } = string.Empty;
        public string VoucherNo { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal RunningBalance { get; set; }
        public string? Description { get; set; }
        public LedgerSourceType SourceType { get; set; }
        public int? SourceId { get; set; }
        public bool IsReversed { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedByUserId { get; set; }
    }

    // One line of a journal entry — exactly one of DebitAmount/CreditAmount
    // must be > 0 (matches the CK_LedgerAccount_DebitXorCredit constraint).
    public class JournalLineWriteDto
    {
        public int ChartOfAccountId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal DebitAmount { get; set; }

        [Range(0, double.MaxValue)]
        public decimal CreditAmount { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }
    }

    // POST body for creating one balanced double-entry transaction (2+ lines
    // sharing a single VoucherNo, total debits == total credits).
    public class JournalEntryCreateDto
    {
        // Leave blank to auto-generate one.
        [MaxLength(50)]
        public string? VoucherNo { get; set; }

        public DateTime? TransactionDate { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public LedgerSourceType SourceType { get; set; } = LedgerSourceType.Manual;

        public int? SourceId { get; set; }

        [MinLength(2)]
        public List<JournalLineWriteDto> Lines { get; set; } = new();
    }

    public class JournalEntryReadDto
    {
        public string VoucherNo { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public List<LedgerLineReadDto> Lines { get; set; } = new();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
    }

    // ===================== CompanyAsset =====================
    public class CompanyAssetReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public DateTime AcquiredDate { get; set; }
        public decimal SalvageValue { get; set; }
        public int UsefulLifeYears { get; set; }
        public decimal DepreciationRatePercent { get; set; }
        public DepreciationMethod DepreciationMethod { get; set; }
        public decimal AccumulatedDepreciation { get; set; }
        public decimal BookValue { get; set; }
        public DateTime? NextDepreciationDate { get; set; }
        public DateTime? DisposalDate { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class CompanyAssetWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal Value { get; set; }

        public DateTime AcquiredDate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal SalvageValue { get; set; } = 0;

        [Range(0, int.MaxValue)]
        public int UsefulLifeYears { get; set; } = 0;

        [Range(0, 100)]
        public decimal DepreciationRatePercent { get; set; } = 0;

        public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ===================== CompanyLiability =====================
    public class CompanyLiabilityReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public LiabilityType LiabilityType { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public DateTime LiabilityDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal InterestRate { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class CompanyLiabilityWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public LiabilityType LiabilityType { get; set; } = LiabilityType.Other;

        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        public DateTime LiabilityDate { get; set; }
        public DateTime? DueDate { get; set; }

        [Range(0, 100)]
        public decimal InterestRate { get; set; } = 0;

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class LiabilityPaymentDto
    {
        [Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }
    }
}
```

### `AuthDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // Body for POST /api/auth/register
    public class RegisterRequestDto
    {
        [Required, MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        // Optional: assign a role at registration time (e.g. "Admin", "Cashier").
        // If omitted the user is created with no role and can be assigned one later.
        public int? RoleId { get; set; }
    }

    // Body for POST /api/auth/login
    public class LoginRequestDto
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    // Returned by login — the JWT the client must send back as
    // "Authorization: Bearer {token}" on every subsequent request.
    public class AuthResponseDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? RoleName { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
    }

    // Returned by register — no token. Client must call /api/auth/login
    // afterwards to obtain one.
    public class RegisterResponseDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? RoleName { get; set; }
        public string Message { get; set; } = "Registration successful. Please log in to get an access token.";
    }

    // Returned by GET /api/auth/me
    public class UserProfileDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public string? RoleName { get; set; }
    }
}
```

### `CustomerDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class CustomerListItemDto
    {
        public int CustomerId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public bool HasPhoto { get; set; }

        // Just the short URL path (not raw bytes) — matches EmployeeListItemDto.PhotoPath.
        public string? PhotoPath { get; set; }
        public int CustomerTypeId { get; set; }
        public string CustomerTypeName { get; set; } = string.Empty;
        // Per-customer addresses, same shape as GetById returns.
        public List<CustomerAddressReadDto> Addresses { get; set; } = new();
    }

    public class CustomerReadDto
    {
        public int CustomerId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public decimal CreditLimit { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public bool IsActive { get; set; }
        public byte[]? Photo { get; set; }
        public string? PhotoContentType { get; set; }

        // Short URL path to the physical file, not raw bytes — combine with
        // your API's base URL to load it, e.g. https://localhost:7079 + PhotoPath.
        public string? PhotoPath { get; set; }
        public int CustomerTypeId { get; set; }
        public string CustomerTypeName { get; set; } = string.Empty;
        public List<CustomerAddressReadDto> Addresses { get; set; } = new();
    }

    public class CustomerAddressReadDto
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string AddressLine { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool IsDefault { get; set; }
    }

    public class CustomerCreateDto
    {
        [MaxLength(50)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? LastName { get; set; }

        public string? Phone { get; set; } // must be unique if provided

        [Range(0, double.MaxValue)]
        public decimal CreditLimit { get; set; } = 0;

        public DateTime? DateOfBirth { get; set; }

        public bool IsActive { get; set; } = true;

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int CustomerTypeId { get; set; }

        public List<CustomerAddressWriteDto>? Addresses { get; set; }
    }

    public class CustomerUpdateDto
    {
        [MaxLength(50)]
        public string? FirstName { get; set; }

        [MaxLength(50)]
        public string? LastName { get; set; }

        public string? Phone { get; set; }

        [Range(0, double.MaxValue)]
        public decimal CreditLimit { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public bool IsActive { get; set; }

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int CustomerTypeId { get; set; }

        // Full sync, same convention as EmployeeUpdateDto.Documents.
        public List<CustomerAddressSyncDto>? Addresses { get; set; }
    }

    public class CustomerAddressWriteDto
    {
        [Required, MaxLength(50)]
        public string Label { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string AddressLine { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? City { get; set; }

        public bool IsDefault { get; set; } = false;
    }

    public class CustomerAddressSyncDto : CustomerAddressWriteDto
    {
        // null or 0 => insert a new address; otherwise update the matching one.
        public int? Id { get; set; }
    }
}
```

### `DoctorDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class DoctorReadDto
    {
        public int DoctorId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Specialization { get; set; }
        public string? RegistrationNo { get; set; }
        public string? Hospital { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public int PrescriptionCount { get; set; }
    }

    public class DoctorWriteDto
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Specialization { get; set; }

        [MaxLength(100)]
        public string? RegistrationNo { get; set; }

        [MaxLength(150)]
        public string? Hospital { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

### `EmployeeDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // ---------- Read DTOs ----------

    public class EmployeeListItemDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public decimal Salary { get; set; }
        public DateTime HireDate { get; set; }
        public bool IsActive { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }

        // Just the short URL path (not raw bytes) — matches ProductListItemDto.ImagePath.
        public string? PhotoPath { get; set; }
        // Per-employee documents, same shape as GetById returns.
        public List<EmployeeDocumentReadDto> Documents { get; set; } = new();
    }

    // Full shape for GET /api/employees/{id}
    public class EmployeeReadDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public decimal Salary { get; set; }
        public DateTime HireDate { get; set; }
        public bool IsActive { get; set; }

        // Short URL path to the physical file, not raw bytes — combine with
        // your API's base URL to load it, e.g. https://localhost:7079 + PhotoPath.
        public string? PhotoPath { get; set; }
        public string? PhotoContentType { get; set; }

        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;

        public List<EmployeeDocumentReadDto> Documents { get; set; } = new();
    }

    public class EmployeeDocumentReadDto
    {
        public int Id { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public string? DocumentNumber { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsVerified { get; set; }
    }

    // ---------- Write DTOs ----------

    public class EmployeeCreateDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200), EmailAddress]
        public string? Email { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Salary { get; set; }

        [Required]
        public DateTime HireDate { get; set; }

        public bool IsActive { get; set; } = true;

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        public List<EmployeeDocumentWriteDto>? Documents { get; set; }
    }

    // Full replace on PUT — mirrors ProductUpdateDto's shape/behavior.
    public class EmployeeUpdateDto
    {
        [Required, MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200), EmailAddress]
        public string? Email { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Salary { get; set; }

        [Required]
        public DateTime HireDate { get; set; }

        public bool IsActive { get; set; }

        public byte[]? Photo { get; set; }

        [MaxLength(100)]
        public string? PhotoContentType { get; set; }

        [MaxLength(500)]
        public string? PhotoPath { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        // Full sync of the detail rows: existing Id -> update, no Id -> insert,
        // any existing document left out of this list -> deleted.
        public List<EmployeeDocumentSyncDto>? Documents { get; set; }
    }

    public class EmployeeDocumentWriteDto
    {
        [Required, MaxLength(150)]
        public string DocumentTitle { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? DocumentNumber { get; set; }

        [Required]
        public DateTime IssueDate { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public bool IsVerified { get; set; } = false;
    }

    public class EmployeeDocumentSyncDto : EmployeeDocumentWriteDto
    {
        // null or 0 => insert a new document; otherwise update the matching one.
        public int? Id { get; set; }
    }
}
```

### `Extrasdtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class ProductStockListItemDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? ProductImagePath { get; set; }
        public string? GenericName { get; set; }
        public string Strength { get; set; } = string.Empty;
        public string BrandType { get; set; } = "Allopathic";
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? DosageFormId { get; set; }
        public string? DosageFormName { get; set; }
        public int? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public bool RequiresPrescription { get; set; } = false;
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public decimal AvailableQuantity { get; set; }
        public decimal Quantity { get; set; }
        public DateOnly ExpiryDate { get; set; }
        public DateOnly ReceivedDate { get; set; }
        public decimal UnitCost { get; set; }
        public decimal SalePrice { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public List<ProductPriceReadDto> Packagings { get; set; } = new();

        // Total units of this Product ever sold (summed across all its
        // stock batches) — powers the "Popularity" sort on the Sale form's
        // Alternate Brands list. 0 for a brand-new product with no sales yet.
        public int PopularityScore { get; set; }
    }

    public class ProductStockWriteDto
    {
        [Range(1, int.MaxValue)]
        public int ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int WarehouseId { get; set; }

        [Required, MaxLength(100)]
        public string BatchNumber { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal Quantity { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? AvailableQuantity { get; set; }

        public DateOnly ExpiryDate { get; set; }
        public DateOnly? ReceivedDate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitCost { get; set; }

        public int? SupplierId { get; set; }
        public int? ProductRakId { get; set; }
    }

    // ===================== ExpiredProductStock =====================
    public class ExpiredProductStockReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int ProductStockId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateOnly ExpiryDate { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
        public string DisposalMethod { get; set; } = string.Empty;
        public DateTime DisposalDate { get; set; }
        public int ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public class ExpiredProductStockCreateDto
    {
        public int ProductStockId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        [MaxLength(100)]
        public string DisposalMethod { get; set; } = "Returned";


        [MaxLength(500)]
        public string? Note { get; set; }
    }

    // ===================== DailyPurchaseRequirementTable =====================
    public class PurchaseRequirementReadDto
    {
        public Guid SerialNo { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal LastPurchaseRate { get; set; }
        public decimal StockQty { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal OrdersQty { get; set; }
    }

    public class PurchaseRequirementWriteDto
    {
        public int ProductId { get; set; }
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LastPurchaseRate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal StockQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal RequiredQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal OrdersQty { get; set; }
    }

    // ===================== RolePermission =====================
    public class RolePermissionReadDto
    {
        public int Id { get; set; }
        public string Module { get; set; } = string.Empty;
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }

    public class RolePermissionSetDto
    {
        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;
        public bool CanView { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }

    // ===================== SmsLog =====================
    public class SmsLogReadDto
    {
        public int Id { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public bool IsSuccess { get; set; }
        public string? Response { get; set; }
    }

    public class SmsLogCreateDto
    {
        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public bool IsSuccess { get; set; }

        [MaxLength(500)]
        public string? Response { get; set; }
    }
}
```

### `Lookupdtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // ===================== Department =====================
    public class DepartmentReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }

    public class DepartmentWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== Unit =====================
    public class UnitReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UnitWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== Role =====================
    public class RoleReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int UserCount { get; set; }
    }

    public class RoleWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    // ===================== CustomerType =====================
    public class CustomerTypeReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CustomerCount { get; set; }
    }

    public class CustomerTypeWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== SupplierType =====================
    public class SupplierTypeReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SupplierCount { get; set; }
    }

    public class SupplierTypeWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== WarehouseType =====================
    public class WarehouseTypeReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int WarehouseCount { get; set; }
    }

    public class WarehouseTypeWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== Warehouse =====================
    public class WarehouseListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public decimal Capacity { get; set; }
        public DateTime? EstablishedDate { get; set; }
        public bool HasPhoto { get; set; }
        public int WarehouseTypeId { get; set; }
        public string WarehouseTypeName { get; set; } = string.Empty;
    }

    public class WarehouseReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public decimal Capacity { get; set; }
        public DateTime? EstablishedDate { get; set; }
        public bool HasPhoto { get; set; }
        public string? PhotoContentType { get; set; }
        public int WarehouseTypeId { get; set; }
        public string WarehouseTypeName { get; set; } = string.Empty;
    }

    public class WarehouseWriteDto
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;

        [Range(0, double.MaxValue)]
        public decimal Capacity { get; set; }

        public DateTime? EstablishedDate { get; set; }

        public int WarehouseTypeId { get; set; }
    }

    // ===================== Company =====================
    public class CompanyReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int ProductCount { get; set; }
    }

    public class CompanyWriteDto
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    // ===================== DosageForm =====================
    public class DosageFormReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ProductCount { get; set; }
    }

    public class DosageFormWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
    }

    // ===================== ProductRak =====================
    public class ProductRakReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public int StockRowCount { get; set; }
    }

    public class ProductRakWriteDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int WarehouseId { get; set; }
    }
}
```

### `PaymentMethodDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PaymentMethodReadDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LedgerAccountCode { get; set; }
        public bool IsActive { get; set; }
    }

    public class PaymentMethodWriteDto
    {
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? LedgerAccountCode { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

### `PrescriptionDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PrescriptionItemReadDto
    {
        public int Id { get; set; }
        public int? ProductId { get; set; }
        public string? ProductName { get; set; }
        public string MedicineName { get; set; } = string.Empty;
        public string? Dosage { get; set; }
        public string? Duration { get; set; }
        public string? Instructions { get; set; }
    }

    public class PrescriptionItemWriteDto
    {
        public int? ProductId { get; set; }

        [Required, MaxLength(200)]
        public string MedicineName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Dosage { get; set; }

        [MaxLength(100)]
        public string? Duration { get; set; }

        [MaxLength(300)]
        public string? Instructions { get; set; }
    }

    public class PrescriptionReadDto
    {
        public int PrescriptionId { get; set; }
        public int DoctorId { get; set; }
        public string? DoctorName { get; set; }
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public DateTime PrescriptionDate { get; set; }
        public string? Diagnosis { get; set; }
        public string? Notes { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageContentType { get; set; }
        public int? SaleId { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PrescriptionItemReadDto> Items { get; set; } = new();
    }

    public class PrescriptionCreateDto
    {
        [Required]
        public int DoctorId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public DateTime PrescriptionDate { get; set; } = DateTime.UtcNow;

        [MaxLength(1000)]
        public string? Diagnosis { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? SaleId { get; set; }

        public List<PrescriptionItemWriteDto>? Items { get; set; }
    }

    public class PrescriptionUpdateDto
    {
        [Required]
        public int DoctorId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public DateTime? PrescriptionDate { get; set; }

        [MaxLength(1000)]
        public string? Diagnosis { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public int? SaleId { get; set; }

        public List<PrescriptionItemSyncDto>? Items { get; set; }
    }

    public class PrescriptionItemSyncDto : PrescriptionItemWriteDto
    {
        public int? Id { get; set; }
    }
}
```

### `ProductDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PharmacyV2.DTOs
{
    public class DosageFormOptionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ProductFilterOptionsDto
    {
        public List<DosageFormOptionDto> DosageForms { get; set; } = new();
        public List<string> GenericNames { get; set; } = new();
    }

    public class ProductListItemDto
    {
        public int Id { get; set; }
        public string? ProductCode { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string BrandType { get; set; } = "Allopathic";
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? DosageFormId { get; set; }
        public string? DosageFormName { get; set; }
        public int? ProductVariantId { get; set; }
        public int? ProductGroupId { get; set; }
        public string? ProductGroupName { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public int StockQuantity { get; set; }

        // Just the URL path (not the raw bytes) so the list stays light —
        // combine with your API's base URL to load it, e.g.
        // https://localhost:7079 + ImagePath. Null if no image was uploaded.
        // [JsonIgnore(Never)] forces this to appear as "imagePath": null in
        // the response instead of being dropped by the app-wide
        // DefaultIgnoreCondition = WhenWritingNull setting in Program.cs.
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ImagePath { get; set; }

        // Flattened from the 1:1 ProductDetails row (null if the product
        // has no details row yet).
        public string? Manufacturer { get; set; }
        public string? Schedule { get; set; }
        public string? DarNo { get; set; }
        public string? StorageConditions { get; set; }
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }
        public string? SideEffects { get; set; }
        public string? PregnancyCategory { get; set; }
        public bool? RequiresPrescription { get; set; }
        public bool? IsControlledDrug { get; set; }
        // Per-unit prices (e.g. Pcs / Box), same shape as GetById returns.
        public List<ProductPriceReadDto> Prices { get; set; } = new();
    }

    // Full shape for GET /api/products/{id}
    public class ProductReadDto
    {
        public int Id { get; set; }
        public string? ProductCode { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string? Barcode { get; set; }
        public string BrandType { get; set; } = "Allopathic";

        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? DosageFormId { get; set; }
        public string? DosageFormName { get; set; }
        public int? ProductVariantId { get; set; }
        public int? ProductGroupId { get; set; }
        public string? ProductGroupName { get; set; }

        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? DistributorPrice { get; set; }
        public decimal? SalePrice { get; set; }

        // Same override as ProductListItemDto.ImagePath — always show these
        // three as null rather than being dropped from the JSON when empty.
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ImagePath { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public byte[]? ProductImage { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ProductImageContentType { get; set; }
        public DateTime RegisteredDate { get; set; }
        public bool? IsActive { get; set; }

        public int StockQuantity { get; set; }
        public int MinStockQty { get; set; }
        public int MaxStockQty { get; set; }
        public int PurchaseQty { get; set; }

        public ProductDetailsReadDto? Details { get; set; }
        public List<ProductPriceReadDto> Prices { get; set; } = new();
    }

    public class ProductDetailsReadDto
    {
        public string? Description { get; set; }
        public string? Manufacturer { get; set; }
        public string? Schedule { get; set; }
        public string? DarNo { get; set; }
        public string StorageConditions { get; set; } = string.Empty;
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }
        public string? Composition { get; set; }
        public string? SideEffects { get; set; }
        public string? PregnancyCategory { get; set; }
        public bool RequiresPrescription { get; set; }
        public bool IsControlledDrug { get; set; }
    }

    public class ProductPriceReadDto
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public decimal PerUnitPrice { get; set; }
        public decimal BaseQuantity { get; set; }

        // Independent per-unit purchase/sale/distributor prices. Null means
        // "not set for this unit yet" — callers should fall back to
        // PerUnitPrice (purchase) / the product's own SalePrice as before,
        // rather than silently defaulting to 0.
        public decimal? PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? DistributorPrice { get; set; }
    }

    // Row for the alphabetical "List of Brand Names" page (P3).
    public class ProductBrandListItemDto
    {
        public int Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Strength { get; set; } = string.Empty;
        public string? GenericName { get; set; }
        public string BrandType { get; set; } = "Allopathic";
        public string? ImagePath { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? DosageFormName { get; set; }
    }

    public class ProductPriceHistoryReadDto
    {
        public int Id { get; set; }
        public string PriceType { get; set; } = string.Empty;
        public string? UnitName { get; set; }
        public decimal? PreviousPrice { get; set; }
        public decimal NewPrice { get; set; }
        public decimal? PreviousBaseQuantity { get; set; }
        public decimal? NewBaseQuantity { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    // ---------- Write DTOs ----------

    public class ProductCreateDto
    {
        // Ignored by the server: ProductCode is always generated from a
        // Guid by ICodeGeneratorService (see ProductsController.Create), so
        // it's unique and consistently formatted without relying on the
        // caller. Kept on the DTO only so old clients that still send it
        // don't fail model binding.
        [Obsolete("Server-generated; sending a value has no effect.")]
        [MaxLength(50)]
        public string? ProductCode { get; set; }

        [Required, MaxLength(200)]
        public string ProductName { get; set; } = string.Empty;

        public string Strength { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? GenericName { get; set; }

        [MaxLength(50)]
        public string? Barcode { get; set; }

        // "Allopathic" or "Herbal". Defaults to "Allopathic" when omitted.
        [MaxLength(20)]
        public string? BrandType { get; set; }

        public int? CompanyId { get; set; }
        public int? DosageFormId { get; set; }
        public int? ProductVariantId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [MaxLength(500)]
        public string? ImagePath { get; set; }

        public byte[]? ProductImage { get; set; }

        [MaxLength(100)]
        public string? ProductImageContentType { get; set; }

        public DateTime? RegisteredDate { get; set; }

        public bool? IsActive { get; set; } = null;

        [Range(0, double.MaxValue)]
        public int MinStockQty { get; set; } = 10;

        [Range(0, double.MaxValue)]
        public int MaxStockQty { get; set; } = 10000;

        public ProductDetailsWriteDto? Details { get; set; }
        public List<ProductPriceWriteDto>? Prices { get; set; }
    }

    public class ProductUpdateDto
    {
        // Ignored by the server: ProductCode is immutable once generated at
        // creation time (see ProductsController.Update). Kept on the DTO
        // only so old clients that still send it don't fail model binding.
        [Obsolete("Server-generated and immutable; sending a value has no effect.")]
        [MaxLength(50)]
        public string? ProductCode { get; set; }

        [Required, MaxLength(200)]
        public string ProductName { get; set; } = string.Empty;

        public string Strength { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? GenericName { get; set; }

        [MaxLength(50)]
        public string? Barcode { get; set; }

        // "Allopathic" or "Herbal". Defaults to "Allopathic" when omitted.
        [MaxLength(20)]
        public string? BrandType { get; set; }

        public int? CompanyId { get; set; }
        public int? DosageFormId { get; set; }
        public int? ProductVariantId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [MaxLength(500)]
        public string? ImagePath { get; set; }

        public byte[]? ProductImage { get; set; }

        [MaxLength(100)]
        public string? ProductImageContentType { get; set; }

        // Nullable on purpose — if the caller doesn't send RegisteredDate on
        // an update, we now leave the product's original date untouched
        // instead of overwriting it with DateTime.MinValue.
        public DateTime? RegisteredDate { get; set; }

        public bool? IsActive { get; set; } = null;

        [Range(0, double.MaxValue)]
        public int MinStockQty { get; set; }

        [Range(0, double.MaxValue)]
        public int MaxStockQty { get; set; }

        // Omit to leave the details row untouched; provide to upsert it.
        public ProductDetailsWriteDto? Details { get; set; }

        // Omit to leave prices untouched; provide a list to fully sync
        // (rows with a matching Id are updated, rows with no/unmatched Id
        // are inserted, and any existing price not present here is removed) —
        // same convention as CustomerUpdateDto.Addresses.
        public List<ProductPriceSyncDto>? Prices { get; set; }
    }

    public class ProductDetailsWriteDto
    {
        public string? Description { get; set; }
        public string? Manufacturer { get; set; }

        [MaxLength(50)]
        public string? Schedule { get; set; }

        [MaxLength(100)]
        public string? DarNo { get; set; }

        public string StorageConditions { get; set; } = string.Empty;
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }

        [MaxLength(1000)]
        public string? Composition { get; set; }

        [MaxLength(1000)]
        public string? SideEffects { get; set; }

        [MaxLength(50)]
        public string? PregnancyCategory { get; set; }

        public bool RequiresPrescription { get; set; }
        public bool IsControlledDrug { get; set; }
    }

    public class ProductPriceWriteDto
    {
        [Required]
        public int UnitId { get; set; }

        [MaxLength(150)]
        public string? DisplayName { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PerUnitPrice { get; set; }

        [Range(0.0001, double.MaxValue)]
        public decimal BaseQuantity { get; set; } = 1;

        [Range(0, double.MaxValue)]
        public decimal? PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }
    }

    public class ProductPriceSyncDto : ProductPriceWriteDto
    {
        // null or 0 => insert a new price row; otherwise update the matching one.
        public int? Id { get; set; }
    }
    // Simple, consistent error payload for 400/404/409 responses.
    public class ApiError
    {
        public string Message { get; set; }

        public ApiError(string message)
        {
            Message = message;
        }
    }
}
```

### `ProductGroupDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs;

public class ProductGroupReadDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int VariantCount { get; set; }
}

public class ProductGroupDetailDto : ProductGroupReadDto
{
    public List<ProductVariantReadDto> Variants { get; set; } = new();
}

public class ProductGroupWriteDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? GenericName { get; set; }
    public int? CompanyId { get; set; }
}

public class ProductVariantReadDto
{
    public int Id { get; set; }
    public int ProductGroupId { get; set; }
    public string Strength { get; set; } = string.Empty;
    public int DosageFormId { get; set; }
    public string DosageFormName { get; set; } = string.Empty;
}

public class ProductVariantWriteDto
{
    [Required, MaxLength(100)]
    public string Strength { get; set; } = string.Empty;
    [Required]
    public int DosageFormId { get; set; }
}
```

### `PurchaseInvoiceDtos.cs`

```csharp
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
```

### `PurchaseOrderDtos.cs`

```csharp
using PharmacyV2.Enums;
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class PurchaseOrderItemReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal LastPurchasePrice { get; set; }
        public decimal StockQty { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal OrderQty { get; set; }
        public decimal? ReceivingQty { get; set; }
        public bool IsCancelled { get; set; }
    }

    public class PurchaseOrderItemWriteDto
    {
        [Required]
        public int ProductId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LastPurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal StockQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal RequiredQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal OrderQty { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? ReceivingQty { get; set; }

        public bool IsCancelled { get; set; } = false;
    }

    public class PurchaseOrderReadDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public DateTime? RequirementDate { get; set; }
        public int SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierCategory { get; set; }
        public PurchaseOrderSource Source { get; set; }
        public PurchaseOrderStatus Status { get; set; }
        public bool IsUrgent { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseOrderItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/purchaseorders — header + items in one call.
    public class PurchaseOrderCreateDto
    {
        [Required, MaxLength(50)]
        public string OrderNo { get; set; } = string.Empty;

        public DateTime OrderDate { get; set; } = DateTime.Now;

        public DateTime? RequirementDate { get; set; }

        [Required]
        public int SupplierId { get; set; }

        [MaxLength(100)]
        public string? SupplierCategory { get; set; }

        public PurchaseOrderSource Source { get; set; } = PurchaseOrderSource.Manual;

        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

        public bool IsUrgent { get; set; } = false;

        // Optional receipt image — omit/leave null and no image is stored,
        // same as every other field here that isn't [Required].
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseOrderItemWriteDto> Items { get; set; } = new();
    }

    // Body for PUT /api/purchaseorders/{id} — header fields only; use the
    // /items endpoints to add or remove line items.
    public class PurchaseOrderUpdateDto
    {
        [Required]
        public int SupplierId { get; set; }

        [MaxLength(100)]
        public string? SupplierCategory { get; set; }

        public PurchaseOrderSource Source { get; set; }

        public PurchaseOrderStatus Status { get; set; }

        public bool IsUrgent { get; set; }

        public DateTime? RequirementDate { get; set; }

        // Optional — omit/leave null to leave the stored receipt untouched.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
```

### `PurchaseReturnDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PharmacyV2.DTOs
{
    public class PurchaseReturnItemReadDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class PurchaseReturnItemWriteDto
    {
        // The original purchase line is the source of truth for product,
        // unit, batch and recorded cost.
        [Range(1, int.MaxValue)] public int PurchaseInvoiceItemId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        // Kept only so obsolete item-level controller actions fail safely;
        // these values are never accepted from JSON and are not part of the API.
        [JsonIgnore] public int ProductId { get; set; }
        [JsonIgnore] public int UnitId { get; set; }
        [JsonIgnore] public decimal BaseQuantity { get; set; }
        [JsonIgnore] public decimal UnitCost { get; set; }

    }

    public class PurchaseReturnReceiveReadDto
    {
        public int Id { get; set; }
        public decimal ReceivedAmount { get; set; }
        public DateTime ReceivedDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? Note { get; set; }
    }

    public class PurchaseReturnReceiveWriteDto
    {
        [Range(0.01, double.MaxValue)]
        public decimal ReceivedAmount { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        [Required, RegularExpression("^(Cash|Card|Mobile Banking)$")]
        public string PaymentMethod { get; set; } = "Cash";

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class PurchaseReturnReceiveSyncDto : PurchaseReturnReceiveWriteDto
    {
        // null or 0 => insert a new receive row; otherwise update the matching one.
        public int? Id { get; set; }
    }

    public class PurchaseReturnReadDto
    {
        public int Id { get; set; }
        public string ReturnNo { get; set; } = string.Empty;
        public DateTime ReturnDate { get; set; }
        public int PurchaseInvoiceId { get; set; }
        public decimal ReturnTotal { get; set; }
        public string? Reason { get; set; }
        public bool IsCompleted { get; set; }

        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseReturnItemReadDto> Items { get; set; } = new();
        public List<PurchaseReturnReceiveReadDto> Receives { get; set; } = new();
    }

    // Body for POST /api/purchasereturns — header + line items in one call,
    // same convention as PurchaseOrders. ReturnTotal is always computed
    // server-side as the sum of item SubTotals (Quantity * UnitCost).
    public class PurchaseReturnCreateDto
    {
        [Required, MaxLength(50)]
        public string ReturnNo { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        [Required]
        public int PurchaseInvoiceId { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // Optional receipt image — omit/leave null and no image is stored,
        // same as every other field here that isn't [Required].
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseReturnItemWriteDto>? Items { get; set; }

        // Optional — usually a return is created first and the refund comes
        // in later via POST /{id}/receives, but you can seed one or more
        // receives in the same call if you already have them.
        public List<PurchaseReturnReceiveWriteDto>? Receives { get; set; }
    }

    // Body for PUT /api/purchasereturns/{id} — header fields, plus an
    // optional full sync of Receives. Omit Receives entirely to leave them
    // untouched; send it (even as []) to fully replace the set — same
    // "full sync" convention as ProductUpdateDto.Prices: no Id/unmatched Id
    // => insert, matching Id => update, existing row missing from the
    // payload => delete. Items still go through the dedicated /items
    // endpoints.
    public class PurchaseReturnUpdateDto
    {
        [MaxLength(500)]
        public string? Reason { get; set; }

        public bool IsCompleted { get; set; }

        // Optional — omit/leave null to leave the stored receipt untouched.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<PurchaseReturnReceiveSyncDto>? Receives { get; set; }
    }
}
```

### `SaleDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class SaleItemReadDto
    {
        public int SaleItemId { get; set; }
        public int ProductStockId { get; set; }
        public string? ProductName { get; set; }
        public string? ProductImagePath { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public int Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

    // Quantity/UnitPrice/UnitId for one batch being sold. TotalPrice is
    // DB-computed — never send it, the API ignores it. UnitId lets each line
    // item be sold in its own unit (e.g. one medicine as "Strip", another as
    // "Bottle") within the same sale.
    public class SaleItemWriteDto
    {
        [Required]
        public int ProductStockId { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        // Ignored by the server: UnitPrice is always resolved from
        // Product.SalePrice (see SalesController.GetCurrentSalePriceAsync),
        // so it stays in sync automatically whenever that column is edited
        // on the Product. Sending a value here has no effect — kept on the
        // DTO only so SaleItemReadDto/SaleItemSyncDto share the same shape.
        [Range(0, double.MaxValue)]
        public decimal UnitPrice { get; set; }
    }

    public class SaleReadDto
    {
        public int SaleId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string? CashierId { get; set; }
        public bool? IsPaid { get; set; }
        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
        public DateTime? VoidedAt { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal DueAmount { get; set; }

        // Total of all (non-deleted) SaleReturns posted against this sale —
        // powers the Sales list "Returned" status (medex-style: a sale with
        // returns shouldn't still read as plain "Paid").
        public decimal ReturnedAmount { get; set; }
        public bool HasReturns { get; set; }

        // Receipt image now lives on disk (wwwroot/images/sale-receipts) — see
        // POST/DELETE api/sales/{id}/receipt-image. Only the path + content
        // type travel in JSON now, no more base64 blob bloating every response.
        public string? ReceiptImagePath { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<SaleItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/sales — header + line items in one call. Each item's
    // ProductStockId batch must have enough AvailableQuantity; stock is
    // decremented server-side. Upload the receipt image afterwards via
    // POST /api/sales/{id}/receipt-image (multipart file) — it's no longer
    // accepted here as raw bytes.
    public class SaleCreateDto
    {
        public int? CustomerId { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.UtcNow;

        // Ignored: TotalAmount is always computed server-side from
        // Items[].Quantity * Items[].UnitPrice so it can't drift from the
        // line items that actually moved stock.
        [Obsolete("Server-computed from Items; sending this has no effect.")]
        public decimal TotalAmount { get; set; }

        public string PaymentMethod { get; set; } = "Cash";

        public string? CashierId { get; set; }

        public bool? IsPaid { get; set; } = null;

        [MinLength(1)]
        public List<SaleItemWriteDto> Items { get; set; } = new();
    }

    // Body for PUT /api/sales/{id} — header fields only; line items are
    // synced separately (see comments in SalesController). Receipt image is
    // managed only via POST/DELETE api/sales/{id}/receipt-image.
    public class SaleUpdateDto
    {
        public int? CustomerId { get; set; }

        // Was missing before, so SaleDate could never be changed via PUT —
        // Update() had no line setting it on the tracked entity either.
        // Nullable: omit it (or send null) to leave the existing SaleDate
        // untouched, same convention as the other optional fields here.
        public DateTime? SaleDate { get; set; }

        public string PaymentMethod { get; set; } = "Cash";

        public string? CashierId { get; set; }

        public bool? IsPaid { get; set; } = null;

        // Full sync of the line items: existing SaleItemId -> update
        // (ProductStockId/Quantity/UnitPrice), no/0 SaleItemId -> insert,
        // any existing item left out of this list -> deleted. Stock is
        // reconciled either way (released on remove/change, re-reserved
        // on add/change) and TotalAmount is recomputed from the result.
        // Leave this null to update header fields only and leave items untouched.
        public List<SaleItemSyncDto>? Items { get; set; }
    }

    // Payment collection is a separate accounting event. It never edits a
    // posted sale or its stock lines.
    public class SalePaymentDto
    {
        [Required, RegularExpression("^(Cash|Card|Mobile Banking)$")]
        public string PaymentMethod { get; set; } = "Cash";
        [Range(0.01, double.MaxValue)] public decimal? Amount { get; set; }
        [MaxLength(500)] public string? Note { get; set; }
    }

    public class SaleVoidDto
    {
        [Required, MaxLength(500)]
        public string Reason { get; set; } = string.Empty;
    }

    public class SaleItemSyncDto : SaleItemWriteDto
    {
        // null or 0 => insert a new item; otherwise update the matching one.
        public int? SaleItemId { get; set; }
    }
}
```

### `SaleReturnDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PharmacyV2.DTOs
{
    public class SaleReturnItemReadDto
    {
        public int Id { get; set; }
        public int MedicineId { get; set; }
        public string? MedicineName { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal SalesPrice { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class SaleReturnItemWriteDto
    {
        // The original sale line is the single source of truth for product,
        // unit, package conversion and recorded sale price.
        [Range(1, int.MaxValue)] public int SaleItemId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        // Kept only so obsolete item-level controller actions fail safely;
        // these values are never accepted from JSON and are not part of the API.
        [JsonIgnore] public int MedicineId { get; set; }
        [JsonIgnore] public int UnitId { get; set; }
        [JsonIgnore] public decimal BaseQuantity { get; set; }
        [JsonIgnore] public decimal SalesPrice { get; set; }

    }

    public class SaleReturnReadDto
    {
        public int Id { get; set; }
        public string ReturnNo { get; set; } = string.Empty;
        public DateTime ReturnDate { get; set; }
        public int SaleId { get; set; }
        public decimal ReturnTotal { get; set; }
        public string? Reason { get; set; }
        public bool IsDeleted { get; set; }
        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }
        public List<SaleReturnItemReadDto> Items { get; set; } = new();
        public decimal RefundedAmount { get; set; }
        public decimal CustomerCredit { get; set; }
    }

    public class SaleReturnRefundDto
    {
        [Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
        [Required, RegularExpression("^(Cash|Card|Mobile Banking)$")] public string PaymentMethod { get; set; } = "Cash";
        [MaxLength(500)] public string? Note { get; set; }
    }

    // Body for POST /api/salereturns — header + line items in one call,
    // same convention as PurchaseOrders. ReturnTotal is always computed
    // server-side as the sum of item SubTotals (Quantity * SalesPrice).
    public class SaleReturnCreateDto
    {
        [Required, MaxLength(50)]
        public string ReturnNo { get; set; } = string.Empty;

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        [Required]
        public int SaleId { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        public List<SaleReturnItemWriteDto>? Items { get; set; }
    }

    // Body for PUT /api/salereturns/{id} — header fields only; use the
    // /items endpoints to add/update/remove line items.
    public class SaleReturnUpdateDto
    {
        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
```

### `StockTransferDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class StockTransferItemReadDto
    {
        public int Id { get; set; }
        public int MedicineId { get; set; }
        public int SourceProductStockId { get; set; }
        public string? MedicineName { get; set; }
        public decimal Quantity { get; set; }
        public DateTime? ExpireDate { get; set; }
    }

    public class StockTransferItemWriteDto
    {
        [Required]
        public int MedicineId { get; set; }
        [Required] public int SourceProductStockId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        public DateTime? ExpireDate { get; set; }
    }

    public class StockTransferReadDto
    {
        public int Id { get; set; }
        public string InvoiceId { get; set; } = string.Empty;
        public DateTime TransferDate { get; set; }
        public int FromWarehouseId { get; set; }
        public string? FromWarehouseName { get; set; }
        public int ToWarehouseId { get; set; }
        public string? ToWarehouseName { get; set; }
        public decimal TotalQty { get; set; }
        public int CreatedByUserId { get; set; }
        public bool IsReceived { get; set; }

        public string? ReceiptImagePath { get; set; }

        // No longer sends raw bytes in the list/detail JSON (that's what was
        // blowing up the response size — every byte becomes ~1.33 bytes of
        // base64 text, repeated on every GetAll call). Instead we just say
        // whether a receipt exists and where to fetch it from.
        public bool HasReceiptImage { get; set; }
        public string? ReceiptImageUrl { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<StockTransferItemReadDto> Items { get; set; } = new();
    }

    // Body for POST /api/stocktransfers — header + items in one call.
    // FromWarehouseId and ToWarehouseId must differ and must both exist.
    // TotalQty is computed server-side from the items.
    public class StockTransferCreateDto
    {
        [Required, MaxLength(50)]
        public string InvoiceId { get; set; } = string.Empty;

        public DateTime TransferDate { get; set; } = DateTime.Now;

        [Required]
        public int FromWarehouseId { get; set; }

        [Required]
        public int ToWarehouseId { get; set; }

        [Required]
        public int CreatedByUserId { get; set; }

        // Optional receipt image — omit/leave null and no image is stored,
        // same as every other field here that isn't [Required].
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        [MinLength(1)]
        public List<StockTransferItemWriteDto> Items { get; set; } = new();
    }

    // Body for PUT /api/stocktransfers/{id} — header notes/invoice id only
    // (items are append/remove only, via the parent controller's item endpoints
    // if you add them, following the PurchaseOrders pattern).
    public class StockTransferUpdateDto
    {
        [Required, MaxLength(50)]
        public string InvoiceId { get; set; } = string.Empty;

        public bool IsReceived { get; set; }

        // Optional — omit/leave null to leave the stored receipt untouched.
        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
```

### `SupplierDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class SupplierListItemDto
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? Phone { get; set; }
        public bool Distributor { get; set; }
        public int SupplierTypeId { get; set; }
        public string SupplierTypeName { get; set; } = string.Empty;
        public string? LogoPath { get; set; }
        // Per-supplier contacts, same shape as GetById returns.
        public List<SupplierContactReadDto> Contacts { get; set; } = new();
    }

    public class SupplierReadDto
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public bool Distributor { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal OpeningBalance { get; set; }
        public byte[]? Logo { get; set; }
        public string? LogoContentType { get; set; }
        public string? LogoPath { get; set; }
        public int SupplierTypeId { get; set; }
        public string SupplierTypeName { get; set; } = string.Empty;
        public List<SupplierContactReadDto> Contacts { get; set; } = new();
    }

    public class SupplierContactReadDto
    {
        public int Id { get; set; }
        public string ContactName { get; set; } = string.Empty;
        public string? Designation { get; set; }
        public string? Phone { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class SupplierCreateDto
    {
        [Required, MaxLength(100)]
        public string SupplierName { get; set; } = string.Empty;

        // Company-র নিজস্ব distributor হলে সেট করো; individual/third-party
        // supplier হলে null রাখো — Company dropdown-এ "Others/Individual"
        // সিলেক্ট করলে frontend থেকে null পাঠাবে, SupplierName তখন manually
        // type করা যাবে।
        public int? CompanyId { get; set; }

        [MaxLength(100)]
        public string? ContactPerson { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        public bool Distributor { get; set; } = false;

        [Range(0, double.MaxValue)]
        public decimal OpeningBalance { get; set; } = 0;

        public byte[]? Logo { get; set; }

        [MaxLength(100)]
        public string? LogoContentType { get; set; }

        [Required]
        public int SupplierTypeId { get; set; }

        public DateTime? CreatedAt { get; set; }

        public List<SupplierContactWriteDto>? Contacts { get; set; }
    }

    public class SupplierUpdateDto
    {
        [Required, MaxLength(100)]
        public string SupplierName { get; set; } = string.Empty;

        public int? CompanyId { get; set; }

        [MaxLength(100)]
        public string? ContactPerson { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        public bool Distributor { get; set; }

        [Range(0, double.MaxValue)]
        public decimal OpeningBalance { get; set; }

        public byte[]? Logo { get; set; }

        [MaxLength(100)]
        public string? LogoContentType { get; set; }

        [Required]
        public int SupplierTypeId { get; set; }

        public DateTime? CreatedAt { get; set; }

        public List<SupplierContactSyncDto>? Contacts { get; set; }
    }

    public class SupplierContactWriteDto
    {
        [Required, MaxLength(100)]
        public string ContactName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Designation { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        public bool IsPrimary { get; set; } = false;
    }

    public class SupplierContactSyncDto : SupplierContactWriteDto
    {
        // null or 0 => insert a new contact; otherwise update the matching one.
        public int? Id { get; set; }
    }
}
```

### `SupplierPaymentDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    public class SupplierPaymentDetailReadDto
    {
        public int Id { get; set; }
        public int PurchaseInvoiceId { get; set; }
        public string? InvoiceNo { get; set; }
        public int LineNo { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DueBeforePayment { get; set; }
        public decimal? PrePaid { get; set; }
        public decimal PaidAmount { get; set; }
    }

    // Body for one allocation row — "pay PaidAmount of this voucher toward
    // this specific PurchaseInvoice". LineNo/TotalAmount/DueBeforePayment
    // are always computed server-side (never trust the client for these),
    // so they're deliberately absent from this write DTO.
    public class SupplierPaymentDetailWriteDto
    {
        [Required]
        public int PurchaseInvoiceId { get; set; }

        [Range(0.01, double.MaxValue)]
        public decimal PaidAmount { get; set; }

        public decimal? PrePaid { get; set; }
    }

    public class SupplierPaymentReadDto
    {
        public int Id { get; set; }
        public string PaidNo { get; set; } = string.Empty;
        public DateTime PaidDate { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public int SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsCancelled { get; set; }

        public string? ReceiptImagePath { get; set; }
        public byte[]? ReceiptImage { get; set; }
        public string? ReceiptImageContentType { get; set; }

        public List<SupplierPaymentDetailReadDto> Details { get; set; } = new();
    }

    // Body for POST /api/supplierpayments — header + allocation rows in one
    // call (same convention as Sales/PurchaseInvoices). Every PurchaseInvoiceId
    // in Details must already exist AND belong to this same SupplierId — you
    // can't pay off someone else's invoice. Each row's PaidAmount can't
    // exceed that invoice's current Due.
    public class SupplierPaymentCreateDto
    {
        [Required, MaxLength(50)]
        public string PaidNo { get; set; } = string.Empty;

        public DateTime PaidDate { get; set; } = DateTime.Now;

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        [Required]
        public int SupplierId { get; set; }

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }

        [MinLength(1)]
        public List<SupplierPaymentDetailWriteDto> Details { get; set; } = new();
    }

    // Body for PUT /api/supplierpayments/5 — header fields only (PaidNo/PaidDate,
    // receipt). Details are managed through the dedicated /details endpoints
    // below, same pattern as Sales items.
    // IsCancelled: false -> true voids the voucher (every allocation row is
    // excluded from Due calculations from then on, restoring Due on the
    // affected invoices). Once cancelled it cannot be un-cancelled through
    // this endpoint — create a fresh voucher instead, so there's always a
    // clean audit trail of what was voided and when.
    public class SupplierPaymentUpdateDto
    {
        [Required, MaxLength(50)]
        public string PaidNo { get; set; } = string.Empty;

        public DateTime PaidDate { get; set; }

        [Required, MaxLength(30)]
        public string PaymentMethod { get; set; } = "Cash";

        public bool IsCancelled { get; set; } = false;

        [MaxLength(500)]
        public string? ReceiptImagePath { get; set; }

        public byte[]? ReceiptImage { get; set; }

        [MaxLength(100)]
        public string? ReceiptImageContentType { get; set; }
    }
}
```

### `SupplierProductDtos.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace PharmacyV2.DTOs
{
    // One (Unit, prices) row nested under a SupplierProduct.
    public class SupplierProductPriceReadDto
    {
        public int Id { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DistributorPrice { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SupplierProductPriceWriteDto
    {
        // null/0 => insert; matching Id => update (same "full sync" convention
        // used elsewhere in this API, e.g. ProductPriceSyncDto).
        public int? Id { get; set; }

        [Required]
        public int UnitId { get; set; }

        [Range(0.0001, double.MaxValue)]
        public decimal BaseQuantity { get; set; } = 1;

        [Range(0, double.MaxValue)]
        public decimal PurchasePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? SalePrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? UnitPrice { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? DistributorPrice { get; set; }
    }

    // GET /api/supplierproducts?supplierId= or ?productId=
    public class SupplierProductReadDto
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductStrength { get; set; }
        public string? SupplierProductCode { get; set; }
        public bool IsPreferred { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
        public decimal? LastPurchaseUnitCost { get; set; }
        public string? LastPurchaseUnitName { get; set; }
        public string? Note { get; set; }
        public List<SupplierProductPriceReadDto> Prices { get; set; } = new();
    }

    public class SupplierProductWriteDto
    {
        [Required]
        public int SupplierId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [MaxLength(50)]
        public string? SupplierProductCode { get; set; }

        public bool IsPreferred { get; set; } = false;
        public bool IsActive { get; set; } = true;

        [MaxLength(500)]
        public string? Note { get; set; }

        // Omit to leave prices untouched on update; provide to fully sync
        // (same convention as Product.Prices).
        public List<SupplierProductPriceWriteDto>? Prices { get; set; }
    }

    // Effective price lookup for a Purchase Invoice line item — the piece
    // that lets the frontend auto-fill UnitCost/SalePrice the moment
    // Product + Unit (+ Supplier) are picked, instead of falling back to the
    // Product's flat master price.
    public class EffectiveUnitPriceDto
    {
        public int ProductId { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DistributorPrice { get; set; }

        // "Supplier" if a SupplierProductPrice row matched, "Product" if it
        // fell back to the product-level ProductPrice for this unit —
        // lets the UI show the user where the number came from.
        public string Source { get; set; } = "Product";
    }
}
```


## 6. Data (EF Core DbContext & Seed)

### `PharmacyDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Models.Accounts;
using PharmacyV2.Models.Branch;
using PharmacyV2.Models.HR;
using PharmacyV2.Models.Other;
using PharmacyV2.Models.Payment;
using PharmacyV2.Models.People;
using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;

namespace PharmacyV2.Data
{
    public class PharmacyDbContext : DbContext
    {
        public PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
            : base(options)
        {
        }

        // ---- Product module ----
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Unit> Units => Set<Unit>();
        public DbSet<ProductDetails> ProductDetails => Set<ProductDetails>();
        public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
        public DbSet<ProductPriceHistory> ProductPriceHistories => Set<ProductPriceHistory>();
        public DbSet<ProductRak> ProductRaks => Set<ProductRak>();
        public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
        public DbSet<ExpiredProductStock> ExpiredProductStocks => Set<ExpiredProductStock>();
        public DbSet<DailyPurchaseRequirementTable> DailyPurchaseRequirements => Set<DailyPurchaseRequirementTable>();
        public DbSet<Company> Companies => Set<Company>();
        public DbSet<DosageForm> DosageForms => Set<DosageForm>();
        public DbSet<ProductGroup> ProductGroups => Set<ProductGroup>();
        public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

        // Supplier <-> Product many-to-many + its per-unit current prices.
        public DbSet<SupplierProduct> SupplierProducts => Set<SupplierProduct>();
        public DbSet<SupplierProductPrice> SupplierProductPrices => Set<SupplierProductPrice>();

        // ---- Accounts module ----
        public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
        public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
        public DbSet<CompanyAsset> CompanyAssets => Set<CompanyAsset>();
        public DbSet<CompanyLiability> CompanyLiabilities => Set<CompanyLiability>();

        // ---- Auth / Other module ----
        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<SmsLog> SmsLogs => Set<SmsLog>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        // ---- People module ----
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Doctor> Doctors { get; set; } = null!;
        public DbSet<Prescription> Prescriptions { get; set; } = null!;
        public DbSet<PrescriptionItem> PrescriptionItems { get; set; } = null!;

        // Lookup/dropdown data only — no controller, seeded below.
        public DbSet<CustomerType> CustomerTypes => Set<CustomerType>();
        public DbSet<SupplierType> SupplierTypes => Set<SupplierType>();

        // Details side of the Customer / Supplier master-details pairs.
        public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
        public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();

        // ---- Branch module ----
        public DbSet<Warehouse> Warehouses => Set<Warehouse>();

        // Lookup/dropdown data only — no controller, seeded below.
        public DbSet<WarehouseType> WarehouseTypes => Set<WarehouseType>();

        public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
        public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();

        // ---- Purchase module ----
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
        public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
        public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
        public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
        public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
        public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();
        public DbSet<PurchaseReturnReceive> PurchaseReturnReceives => Set<PurchaseReturnReceive>();

        // ---- Sale module ----
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<SaleItem> SaleItems => Set<SaleItem>();
        public DbSet<SalePayment> SalePayments => Set<SalePayment>();
        public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
        public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
        public DbSet<SaleReturnRefund> SaleReturnRefunds => Set<SaleReturnRefund>();

        // ---- Payment module ----
        public DbSet<PharmacyV2.Models.Payment.SupplierPayment> SupplierPayments => Set<PharmacyV2.Models.Payment.SupplierPayment>();
        public DbSet<PharmacyV2.Models.Payment.SupplierPaymentDetail> SupplierPaymentDetails => Set<PharmacyV2.Models.Payment.SupplierPaymentDetail>();
        public DbSet<PaymentMethod> PaymentMethods { get; set; } = null!;

        // ---- HR module ----
        // Department is lookup/dropdown data only — no controller, seeded below.
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>(); protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //-- Doctors and Prescription 
                modelBuilder.Entity<Prescription>()
                    .HasOne(p => p.Doctor).WithMany(d => d.Prescriptions)
                    .HasForeignKey(p => p.DoctorId).OnDelete(DeleteBehavior.Restrict);

                modelBuilder.Entity<Prescription>()
                    .HasOne(p => p.Customer).WithMany()
                    .HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);

                modelBuilder.Entity<Prescription>()
                    .HasOne(p => p.Sale).WithMany()
                    .HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.SetNull);

                modelBuilder.Entity<PrescriptionItem>()
                    .HasOne(i => i.Product).WithMany()
                    .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.SetNull);

            // ----- Product -----
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasIndex(p => p.ProductCode).IsUnique(false);
                entity.HasIndex(p => p.Barcode).IsUnique(false);
                // Powers the Brand Search page (search by brand OR generic name).
                entity.HasIndex(p => p.ProductName);
                entity.HasIndex(p => p.GenericName);
                // Powers the Allopathic/Herbal tabs on the Brand pages.
                entity.HasIndex(p => p.BrandType);

                entity.Property(p => p.BrandType)
                      .HasMaxLength(20)
                      .HasDefaultValue("Allopathic");

                entity.HasOne(p => p.Unit)
                      .WithMany(u => u.Products)
                      .HasForeignKey(p => p.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.Company)
                      .WithMany(c => c.Products)
                      .HasForeignKey(p => p.CompanyId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(p => p.DosageForm)
                      .WithMany(d => d.Products)
                      .HasForeignKey(p => p.DosageFormId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(p => p.ProductVariant)
                      .WithMany(v => v.Products)
                      .HasForeignKey(p => p.ProductVariantId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(p => p.ProductDetails)
                        .WithOne(d => d.Product)
                        .HasForeignKey<ProductDetails>(d => d.ProductId)
                        .OnDelete(DeleteBehavior.Cascade);
                            });

            modelBuilder.Entity<ProductGroup>(entity =>
            {
                entity.HasIndex(g => g.Name).IsUnique();
                entity.HasOne(g => g.Company)
                      .WithMany()
                      .HasForeignKey(g => g.CompanyId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.HasIndex(v => new { v.ProductGroupId, v.Strength, v.DosageFormId }).IsUnique();
                entity.HasOne(v => v.ProductGroup)
                      .WithMany(g => g.Variants)
                      .HasForeignKey(v => v.ProductGroupId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(v => v.DosageForm)
                      .WithMany()
                      .HasForeignKey(v => v.DosageFormId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- Company (lookup for the Brand Search "Company" filter) -----
            modelBuilder.Entity<Company>(entity =>
            {
                entity.HasIndex(c => c.Name).IsUnique();
            });

            // ----- DosageForm (lookup for the Brand Search "Dosage Form" filter) -----
            modelBuilder.Entity<DosageForm>(entity =>
            {
                entity.HasIndex(d => d.Name).IsUnique();

                entity.HasData(
                    new DosageForm { Id = 1, Name = "Tablet" },
                    new DosageForm { Id = 2, Name = "Capsule" },
                    new DosageForm { Id = 3, Name = "Oral Suspension" },
                    new DosageForm { Id = 4, Name = "Syrup" },
                    new DosageForm { Id = 5, Name = "Injection" },
                    new DosageForm { Id = 6, Name = "IV Infusion" },
                    new DosageForm { Id = 7, Name = "Suppository" },
                    new DosageForm { Id = 8, Name = "Ointment/Cream" },
                    new DosageForm { Id = 9, Name = "Drops" },
                    new DosageForm { Id = 10, Name = "Inhaler" }
                );
            });

            // ----- SupplierProduct (Supplier <-> Product many-to-many) -----
            modelBuilder.Entity<SupplierProduct>(entity =>
            {
                // One link per (Supplier, Product) pair — matches the
                // "supplier having a products list" / "product belonging to
                // many suppliers" requirement without duplicate rows.
                entity.HasIndex(sp => new { sp.SupplierId, sp.ProductId }).IsUnique();

                entity.HasOne(sp => sp.Supplier)
                      .WithMany(s => s.SupplierProducts)
                      .HasForeignKey(sp => sp.SupplierId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(sp => sp.Product)
                      .WithMany(p => p.SupplierProducts)
                      .HasForeignKey(sp => sp.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Unit>()
                      .WithMany()
                      .HasForeignKey(sp => sp.LastPurchaseUnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- SupplierProductPrice (one row per Unit per SupplierProduct) -----
            modelBuilder.Entity<SupplierProductPrice>(entity =>
            {
                entity.HasIndex(spp => new { spp.SupplierProductId, spp.UnitId }).IsUnique();

                entity.HasOne(spp => spp.SupplierProduct)
                      .WithMany(sp => sp.Prices)
                      .HasForeignKey(spp => spp.SupplierProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(spp => spp.Unit)
                      .WithMany()
                      .HasForeignKey(spp => spp.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- Unit (lookup, HAS a controller unlike CustomerType/SupplierType/
            // WarehouseType below — still seeded with fixed Ids so the dropdown
            // that every UnitId FK (Product, ProductPrice, PurchaseOrderItem,
            // PurchaseInvoiceItem, PurchaseReturnItem, Sale, SaleReturnItem,
            // DailyPurchaseRequirementTable) feeds from isn't empty on a fresh DB. -----
            modelBuilder.Entity<Unit>(entity =>
            {
                entity.HasIndex(u => u.Name).IsUnique();

                entity.HasData(
                    new Unit { Id = 1, Name = "Pcs" },
                    new Unit { Id = 2, Name = "Box" },
                    new Unit { Id = 3, Name = "Strip" },
                    new Unit { Id = 4, Name = "Bottle" },
                    new Unit { Id = 5, Name = "Vial" },
                    new Unit { Id = 6, Name = "Tube" }
                );
            });

            // ----- ProductDetails (1:1 with Product) ----


            // ----- ProductPrice (many named pack variants per Product) -----
            modelBuilder.Entity<ProductPrice>(entity =>
            {
                entity.HasIndex(pp => new { pp.ProductId, pp.UnitId, pp.DisplayName }).IsUnique();

                entity.HasOne(pp => pp.Product)
                      .WithMany(p => p.ProductPrices)
                      .HasForeignKey(pp => pp.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(pp => pp.Unit)
                      .WithMany(u => u.ProductPrices)
                      .HasForeignKey(pp => pp.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductPriceHistory>(entity =>
            {
                entity.HasOne(h => h.Product).WithMany().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(h => h.Unit).WithMany().HasForeignKey(h => h.UnitId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(h => h.Supplier).WithMany().HasForeignKey(h => h.SupplierId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(h => new { h.ProductId, h.ChangedAt });
                entity.HasIndex(h => new { h.SupplierId, h.ProductId, h.ChangedAt });
            });

            // ----- ProductRak -----
            modelBuilder.Entity<ProductRak>(entity =>
            {
                entity.HasOne(r => r.Warehouse)
                      .WithMany()
                      .HasForeignKey(r => r.WarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(r => r.ProductStocks)
                      .WithOne(s => s.ProductRak)
                      .HasForeignKey(s => s.ProductRakId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ----- ProductStock -----
            modelBuilder.Entity<ProductStock>(entity =>
            {
                entity.HasKey(s => s.Id);

                // Composite uniqueness: a batch number is unique per product
                entity.HasIndex(s => new { s.ProductId, s.BatchNumber }).IsUnique();

                entity.HasOne(s => s.Product)
                      .WithMany(p => p.ProductStocks)
                      .HasForeignKey(s => s.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.Warehouse)
                      .WithMany(w => w.ProductStocks)
                      .HasForeignKey(s => s.WarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.Supplier)
                      .WithMany(sup => sup.ProductStocks)
                      .HasForeignKey(s => s.SupplierId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(s => s.PurchaseInvoice)
                      .WithMany()
                      .HasForeignKey(s => s.PurchaseInvoiceId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ----- ExpiredProductStock -----
            modelBuilder.Entity<ExpiredProductStock>(entity =>
            {
                entity.HasOne(e => e.Product)
                      .WithMany()
                      .HasForeignKey(e => e.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ProductStock)
                      .WithMany(s => s.ExpiredProducts)
                      .HasForeignKey(e => e.ProductStockId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Warehouse)
                      .WithMany()
                      .HasForeignKey(e => e.WarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ApprovedBy)
                      .WithMany()
                      .HasForeignKey(e => e.ApprovedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- DailyPurchaseRequirementTable -----
            modelBuilder.Entity<DailyPurchaseRequirementTable>(entity =>
            {
                entity.HasKey(d => d.SerialNo);

                entity.HasOne(d => d.Product)
                      .WithMany()
                      .HasForeignKey(d => d.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Unit)
                      .WithMany()
                      .HasForeignKey(d => d.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- ChartOfAccount (self-referencing hierarchy) -----
            modelBuilder.Entity<ChartOfAccount>(entity =>
            {
                entity.HasIndex(a => a.Code).IsUnique(false);

                entity.HasOne(a => a.Parent)
                      .WithMany(a => a.Children)
                      .HasForeignKey(a => a.ParentId)
                      .OnDelete(DeleteBehavior.Restrict); // avoid self-ref cascade cycles
            });

            // ----- LedgerAccount (General Ledger postings) -----
            modelBuilder.Entity<LedgerAccount>(entity =>
            {
                entity.HasIndex(l => l.VoucherNo);
                entity.HasIndex(l => new { l.ChartOfAccountId, l.TransactionDate });

                entity.HasOne(l => l.ChartOfAccount)
                      .WithMany(a => a.LedgerAccounts)
                      .HasForeignKey(l => l.ChartOfAccountId)
                      .OnDelete(DeleteBehavior.Restrict); // never lose postings when an account is removed

                // Double-entry guard: a posting is either a debit or a credit, never both/neither.
                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_LedgerAccount_DebitXorCredit",
                    "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)"));
            });

            // ----- Role / RolePermission / AppUser -----
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasIndex(r => r.Name).IsUnique();
            });

            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasIndex(rp => new { rp.RoleId, rp.Module }).IsUnique();

                entity.HasOne(rp => rp.Role)
                      .WithMany(r => r.Permissions)
                      .HasForeignKey(rp => rp.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.HasIndex(u => u.Username).IsUnique();

                entity.HasOne(u => u.Role)
                      .WithMany(r => r.Users)
                      .HasForeignKey(u => u.RoleId)
                      .OnDelete(DeleteBehavior.SetNull);

                // AppUser.SalesCreated / PurchasesCreated / TransfersCreated are
                // unidirectional navigations with no matching property on the other
                // side. Left unconfigured, EF Core would invent a *required* shadow
                // FK on Sale/PurchaseInvoice/StockTransfer, which would then reject
                // every insert made through the controllers below (they don't set
                // it). Ignored here; StockTransfer already has a real (unmapped-nav)
                // CreatedByUserId int column that serves the same purpose.
                entity.Ignore(u => u.SalesCreated);
                entity.Ignore(u => u.PurchasesCreated);
                entity.Ignore(u => u.TransfersCreated);
            });

            // ----- Supplier / Customer -----
            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.HasIndex(s => s.SupplierName);

                entity.HasOne(s => s.SupplierType)
                      .WithMany(t => t.Suppliers)
                      .HasForeignKey(s => s.SupplierTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.Company)
                      .WithMany()
                      .HasForeignKey(s => s.CompanyId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasIndex(c => c.Phone).IsUnique();

                entity.HasOne(c => c.CustomerType)
                      .WithMany(t => t.Customers)
                      .HasForeignKey(c => c.CustomerTypeId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- CustomerType / SupplierType (lookup, no controller) -----
            modelBuilder.Entity<CustomerType>(entity =>
            {
                entity.HasIndex(t => t.Name).IsUnique();

                entity.HasData(
                    new CustomerType { Id = 1, Name = "Retail" },
                    new CustomerType { Id = 2, Name = "Wholesale" },
                    new CustomerType { Id = 3, Name = "Corporate" }
                );
            });

            modelBuilder.Entity<SupplierType>(entity =>
            {
                entity.HasIndex(t => t.Name).IsUnique();

                entity.HasData(
                    new SupplierType { Id = 1, Name = "Manufacturer" },
                    new SupplierType { Id = 2, Name = "Distributor" },
                    new SupplierType { Id = 3, Name = "Local Vendor" }
                );
            });

            // ----- CustomerAddress / SupplierContact (details) -----
            modelBuilder.Entity<CustomerAddress>(entity =>
            {
                entity.HasOne(a => a.Customer)
                      .WithMany(c => c.Addresses)
                      .HasForeignKey(a => a.CustomerId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SupplierContact>(entity =>
            {
                entity.HasOne(c => c.Supplier)
                      .WithMany(s => s.Contacts)
                      .HasForeignKey(c => c.SupplierId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ----- Warehouse -----
            modelBuilder.Entity<Warehouse>(entity =>
            {
                entity.HasIndex(w => w.Name).IsUnique();

                entity.HasOne(w => w.WarehouseType)
                      .WithMany(t => t.Warehouses)
                      .HasForeignKey(w => w.WarehouseTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                // Warehouse.Sales has no matching WarehouseId/Warehouse property on
                // Sale (Sale only tracks CustomerId/UnitId), so there's nothing to
                // map it to. Ignored here rather than letting EF silently invent a
                // shadow FK; Sale's ProductStock -> Warehouse chain is the only
                // place warehouse is actually tracked for a sale today.
                entity.Ignore(w => w.Sales);
            });

            // ----- WarehouseType (lookup, no controller) -----
            modelBuilder.Entity<WarehouseType>(entity =>
            {
                entity.HasIndex(t => t.Name).IsUnique();

                entity.HasData(
                    new WarehouseType { Id = 1, Name = "Main" },
                    new WarehouseType { Id = 2, Name = "Branch" },
                    new WarehouseType { Id = 3, Name = "Cold Storage" }
                );
            });

            // ----- StockTransfer (two FKs to the same Warehouse table -----
            // must both be Restrict, or SQL Server rejects the model with a
            // "may cause cycles or multiple cascade paths" error).
            modelBuilder.Entity<StockTransfer>(entity =>
            {
                entity.HasOne(t => t.FromWarehouse)
                      .WithMany(w => w.StockTransfersFrom)
                      .HasForeignKey(t => t.FromWarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(t => t.ToWarehouse)
                      .WithMany(w => w.StockTransfersTo)
                      .HasForeignKey(t => t.ToWarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockTransferItem>(entity =>
            {
                entity.HasOne(i => i.StockTransfer)
                      .WithMany(t => t.Items)
                      .HasForeignKey(i => i.StockTransferId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Medicine)
                      .WithMany()
                      .HasForeignKey(i => i.MedicineId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- PurchaseOrder / PurchaseOrderItem -----
            modelBuilder.Entity<PurchaseOrder>(entity =>
            {
                entity.HasIndex(o => o.OrderNo).IsUnique();

                entity.HasOne(o => o.Supplier)
                      .WithMany(s => s.PurchaseOrders)
                      .HasForeignKey(o => o.SupplierId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PurchaseOrderItem>(entity =>
            {
                entity.HasOne(i => i.PurchaseOrder)
                      .WithMany(o => o.Items)
                      .HasForeignKey(i => i.PurchaseOrderId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Product)
                      .WithMany(p => p.PurchaseItems)
                      .HasForeignKey(i => i.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.Unit)
                      .WithMany()
                      .HasForeignKey(i => i.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- PurchaseInvoice -----
            modelBuilder.Entity<PurchaseInvoice>(entity =>
            {
                entity.HasIndex(p => p.InvoiceNo).IsUnique();

                entity.HasOne(p => p.Supplier)
                      .WithMany(s => s.Purchases)
                      .HasForeignKey(p => p.SupplierId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.PurchaseOrder)
                      .WithMany(o => o.Purchases)
                      .HasForeignKey(p => p.PurchaseOrderId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(p => p.Warehouse)
                      .WithMany(w => w.Purchases)
                      .HasForeignKey(p => p.WarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

            });

            modelBuilder.Entity<PurchaseInvoiceItem>(entity =>
            {
                entity.HasOne(i => i.PurchaseInvoice)
                      .WithMany(p => p.Items)
                      .HasForeignKey(i => i.PurchaseInvoiceId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Product)
                      .WithMany()
                      .HasForeignKey(i => i.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.Unit)
                      .WithMany()
                      .HasForeignKey(i => i.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- PurchaseReturn / Item / Receive -----
            modelBuilder.Entity<PurchaseReturn>(entity =>
            {
                entity.HasIndex(r => r.ReturnNo).IsUnique();

                entity.HasOne(r => r.PurchaseInvoice)
                      .WithMany(p => p.Returns)
                      .HasForeignKey(r => r.PurchaseInvoiceId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PurchaseReturnItem>(entity =>
            {
                entity.HasOne(i => i.PurchaseReturn)
                      .WithMany(r => r.Items)
                      .HasForeignKey(i => i.PurchaseReturnId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Product)
                      .WithMany()
                      .HasForeignKey(i => i.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.Unit)
                      .WithMany()
                      .HasForeignKey(i => i.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PurchaseReturnReceive>(entity =>
            {
                entity.HasOne(r => r.PurchaseReturn)
                      .WithMany(p => p.Receives)
                      .HasForeignKey(r => r.PurchaseReturnId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ----- Sale / SaleItem -----
            modelBuilder.Entity<Sale>(entity =>
            {
                entity.HasKey(s => s.SaleId);
                entity.Property(s => s.SaleDate).HasDefaultValueSql("SYSUTCDATETIME()");
                entity.HasIndex(s => s.InvoiceNo).IsUnique();

                entity.HasOne(s => s.Customer)
                      .WithMany(c => c.Sales)
                      .HasForeignKey(s => s.CustomerId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<SaleItem>(entity =>
            {
                entity.HasKey(i => i.SaleItemId);

                // GENERATED ALWAYS AS (QUANTITY * UNIT_PRICE) — computed at the DB
                // level, so it must never be assigned from application code.
                entity.Property(i => i.TotalPrice)
                      .HasComputedColumnSql("[Quantity] * [UnitPrice]", stored: true)
                      .ValueGeneratedOnAddOrUpdate();

                entity.ToTable(t => t.HasCheckConstraint("CK_SaleItem_Quantity", "[Quantity] > 0"));

                entity.HasOne(i => i.Sale)
                      .WithMany(s => s.Items)
                      .HasForeignKey(i => i.SaleId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.ProductStocks)
                      .WithMany(s => s.SaleItems)
                      .HasForeignKey(i => i.ProductStockId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.Unit)
                      .WithMany()
                      .HasForeignKey(i => i.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalePayment>(entity =>
            {
                entity.HasOne(p => p.Sale).WithMany(s => s.Payments).HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);
                entity.ToTable(t => t.HasCheckConstraint("CK_SalePayment_Amount", "[Amount] > 0"));
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(a => new { a.EntityName, a.EntityId });
                entity.HasIndex(a => a.OccurredAt);
            });

            modelBuilder.Entity<SaleReturnRefund>(entity =>
            {
                entity.HasOne(r => r.SaleReturn).WithMany(s => s.Refunds).HasForeignKey(r => r.SaleReturnId).OnDelete(DeleteBehavior.Restrict);
                entity.ToTable(t => t.HasCheckConstraint("CK_SaleReturnRefund_Amount", "[Amount] > 0"));
            });

            // ----- SaleReturn / SaleReturnItem -----
            modelBuilder.Entity<SaleReturn>(entity =>
            {
                entity.HasIndex(r => r.ReturnNo).IsUnique();

                entity.HasOne(r => r.SaleInvoice)
                      .WithMany()
                      .HasForeignKey(r => r.SaleId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SaleReturnItem>(entity =>
            {
                entity.HasOne(i => i.SaleReturn)
                      .WithMany(r => r.Items)
                      .HasForeignKey(i => i.SaleReturnId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.Medicine)
                      .WithMany()
                      .HasForeignKey(i => i.MedicineId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.Unit)
                      .WithMany()
                      .HasForeignKey(i => i.UnitId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- SupplierPayment / SupplierPaymentDetail -----
            modelBuilder.Entity<PharmacyV2.Models.Payment.SupplierPayment>(entity =>
            {
                entity.HasIndex(p => p.PaidNo).IsUnique();

                entity.HasOne(p => p.Supplier)
                      .WithMany(s => s.Payments)
                      .HasForeignKey(p => p.SupplierId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PharmacyV2.Models.Payment.SupplierPaymentDetail>(entity =>
            {
                entity.HasOne(d => d.SupplierPayment)
                      .WithMany(p => p.Details)
                      .HasForeignKey(d => d.SupplierPaymentId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.PurchaseInvoice)
                      .WithMany(p => p.PaymentAllocations)
                      .HasForeignKey(d => d.PurchaseInvoiceId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PaymentMethod>().HasIndex(p => p.Name).IsUnique();

            // ----- Department (lookup / dropdown, no controller) -----
            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasIndex(d => d.Name).IsUnique();

                // Fixed Ids so the seed is deterministic across environments —
                // this is the data the Employee.DepartmentId dropdown is built from.
                entity.HasData(
                    new Department { Id = 1, Name = "Pharmacy" },
                    new Department { Id = 2, Name = "Sales" },
                    new Department { Id = 3, Name = "Purchasing" },
                    new Department { Id = 4, Name = "Warehouse" },
                    new Department { Id = 5, Name = "Administration" }
                );
            });

            // ----- Employee (master) -----
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.HasIndex(e => e.Email);

                entity.HasOne(e => e.Department)
                      .WithMany(d => d.Employees)
                      .HasForeignKey(e => e.DepartmentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ----- EmployeeDocument (details) -----
            modelBuilder.Entity<EmployeeDocument>(entity =>
            {
                entity.HasOne(d => d.Employee)
                      .WithMany(e => e.Documents)
                      .HasForeignKey(d => d.EmployeeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
```

### `SeedData.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Enums;
using PharmacyV2.Models.Accounts;
using PharmacyV2.Models.Branch;
using PharmacyV2.Models.HR;
using PharmacyV2.Models.Other;
using PharmacyV2.Models.Payment;
using PharmacyV2.Models.People;
using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Models.SaleInvoice;

namespace PharmacyV2.Data
{
    // Runtime seeder for TRANSACTIONAL/sample data (every table that ISN'T
    // already covered by HasData() in PharmacyDbContext — Unit, CustomerType,
    // SupplierType, WarehouseType, Department are seeded there and must not
    // be duplicated here).
    //
    // Called once from Program.cs on startup. Guarded so it never runs twice
    // or touches a DB that already has data.
    public static class SeedData
    {
        public static async Task SeedSampleDataAsync(PharmacyDbContext db)
        {
            if (await db.Customers.AnyAsync()) return; // already seeded — skip

            // ---------- 1) Roles / Users ----------
            var adminRole = new Role { Name = "Admin", Description = "Full access" };
            var cashierRole = new Role { Name = "Cashier", Description = "POS / sales only" };
            db.Roles.AddRange(adminRole, cashierRole);
            await db.SaveChangesAsync();

            db.RolePermissions.AddRange(
                new RolePermission { RoleId = adminRole.Id, Module = "Sales", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true },
                new RolePermission { RoleId = cashierRole.Id, Module = "Sales", CanView = true, CanCreate = true, CanEdit = false, CanDelete = false }
            );

            var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>();
            var adminUser = new AppUser
            {
                Username = "admin",
                FullName = "System Admin",
                Email = "admin@pharmacy.local",
                Phone = "01700000000",
                RoleId = adminRole.Id
            };
            adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, "Admin@123");
            var cashierUser = new AppUser
            {
                Username = "cashier1",
                FullName = "Jamal Uddin",
                Email = "cashier1@pharmacy.local",
                Phone = "01700000001",
                RoleId = cashierRole.Id
            };
            cashierUser.PasswordHash = passwordHasher.HashPassword(cashierUser, "Cashier@123");
            db.AppUsers.AddRange(adminUser, cashierUser);
            await db.SaveChangesAsync();

            // ---------- 2) Warehouses + Racks ----------
            var mainWarehouse = new Warehouse { Name = "Main Warehouse", Address = "Dhaka", WarehouseTypeId = 1, Capacity = 10000 };
            var branchWarehouse = new Warehouse { Name = "Narayanganj Branch", Address = "Narayanganj", WarehouseTypeId = 2, Capacity = 3000 };
            db.Warehouses.AddRange(mainWarehouse, branchWarehouse);
            await db.SaveChangesAsync();

            var rakA1 = new ProductRak { WarehouseId = mainWarehouse.Id, Name = "Rack-A1" };
            var rakA2 = new ProductRak { WarehouseId = mainWarehouse.Id, Name = "Rack-A2" };
            db.ProductRaks.AddRange(rakA1, rakA2);
            await db.SaveChangesAsync();

            // ---------- 3) Suppliers ----------
            var supplier = new Supplier
            {
                SupplierName = "Square Pharmaceuticals Ltd.",
                ContactPerson = "Karim Hossain",
                Phone = "01800000000",
                Email = "sales@squarepharma.example",
                Address = "Tejgaon, Dhaka",
                CreatedAt = DateTime.UtcNow,
                Distributor = true,
                SupplierTypeId = 1 // Manufacturer
            };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            db.SupplierContacts.Add(new SupplierContact
            {
                SupplierId = supplier.SupplierId,
                ContactName = "Karim Hossain",
                Designation = "Area Sales Manager",
                Phone = "01800000000",
                IsPrimary = true
            });

            // ---------- 4) Customers ----------
            var customer = new Customer
            {
                FirstName = "Abdur",
                LastName = "Rahman",
                Phone = "01710000000",
                CreditLimit = 5000,
                IsActive = true,
                CustomerTypeId = 1 // Retail
            };

            var customer2 = new Customer
            {
                FirstName = "Jannatul",
                LastName = "Ferdaus",
                Phone = "01710000001",
                CreditLimit = 3000,
                IsActive = true,
                CustomerTypeId = 2 // Wholesale
            };
            var customer3 = new Customer
            {
                FirstName = "Rakib",
                LastName = "Hasan",
                Phone = "01710000002",
                CreditLimit = 10000,
                IsActive = true,
                CustomerTypeId = 3 // Corporate
            };
            db.Customers.AddRange(customer, customer2, customer3);
            await db.SaveChangesAsync();

            // customer.CustomerId is only populated by EF AFTER the save
            // above, so this address row has to be added afterwards —
            // adding it earlier (before the save) would insert CustomerId=0
            // and violate the FK constraint.
            db.CustomerAddresses.Add(new CustomerAddress
            {
                CustomerId = customer.CustomerId,
                Label = "Home",
                AddressLine = "House 12, Road 5, Narayanganj",
                City = "Narayanganj",
                IsDefault = true
            });

            // ---------- 5) Companies (Brand Search "Company" filter) ----------
            // Square Pharmaceuticals is already our seeded Supplier — give it a
            // matching Company row too, so Napa's brand card shows a real
            // manufacturer instead of "Unknown company".
            var squarePharma = new Company { Name = "Square Pharmaceuticals Ltd.", IsActive = true };
            var herbalCompany = new Company { Name = "Hamdard Laboratories (Bangladesh) Ltd.", IsActive = true };
            db.Companies.AddRange(squarePharma, herbalCompany);
            await db.SaveChangesAsync();

            // ---------- 6) Products + details + prices ----------
            var product = new Product
            {
                ProductCode = "NAPA-500",
                ProductName = "Napa 500mg Tablet",
                Strength = "500mg",
                GenericName = "Paracetamol",
                Barcode = "8901234500017",
                BrandType = "Allopathic",
                CompanyId = squarePharma.Id,
                DosageFormId = 1, // Tablet
                UnitId = 1, // Pcs
                UnitPrice = 5,
                PurchasePrice = 4.5m,
                DistributorPrice = 4.0m,
                SalePrice = 5.5m,
                RegisteredDate = DateTime.UtcNow,
                IsActive = true,
                StockQuantity = 0,
                MinStockQty = 100,
                MaxStockQty = 10000
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            db.ProductDetails.Add(new ProductDetails
            {
                ProductId = product.Id,
                Manufacturer = "Square Pharmaceuticals Ltd.",
                Schedule = "OTC",
                DarNo = "DAR-00123",
                StorageConditions = "Store below 30°C, away from light",
                TemperatureMin = 15,
                TemperatureMax = 30,
                SideEffects = "Rare at recommended dose",
                PregnancyCategory = "B",
                RequiresPrescription = false,
                IsControlledDrug = false
            });

            db.ProductPrices.AddRange(
                new ProductPrice { ProductId = product.Id, UnitId = 1, PerUnitPrice = 5 },     // per Pcs
                new ProductPrice { ProductId = product.Id, UnitId = 2, PerUnitPrice = 500 }     // per Box (100 pcs)
            );
            await db.SaveChangesAsync();

            // A second, Herbal-type brand so the Brand pages' Herbal tab
            // (and the Browse menu's "Brand Names (Herbal)" link) has real
            // data to show instead of coming back empty.
            var herbalProduct = new Product
            {
                ProductCode = "SAFI-200",
                ProductName = "Safi Herbal Syrup",
                Strength = "200ml",
                GenericName = "Blood Purifier (Herbal)",
                Barcode = "8901234500024",
                BrandType = "Herbal",
                CompanyId = herbalCompany.Id,
                DosageFormId = 4, // Syrup
                UnitId = 1, // Pcs
                UnitPrice = 120,
                PurchasePrice = 95m,
                DistributorPrice = 85m,
                SalePrice = 130m,
                RegisteredDate = DateTime.UtcNow,
                IsActive = true,
                StockQuantity = 0,
                MinStockQty = 20,
                MaxStockQty = 2000
            };
            db.Products.Add(herbalProduct);
            await db.SaveChangesAsync();

            db.ProductDetails.Add(new ProductDetails
            {
                ProductId = herbalProduct.Id,
                Manufacturer = "Hamdard Laboratories (Bangladesh) Ltd.",
                Schedule = "OTC",
                StorageConditions = "Store in a cool, dry place, away from light",
                SideEffects = "None reported at recommended dose",
                RequiresPrescription = false,
                IsControlledDrug = false
            });

            db.ProductPrices.Add(new ProductPrice { ProductId = herbalProduct.Id, UnitId = 1, PerUnitPrice = 120 });
            await db.SaveChangesAsync();

            // ---------- 6) Purchase Order -> Purchase Invoice (+ items) ----------
            var purchaseOrder = new PurchaseOrder
            {
                OrderNo = "PO-000001",
                OrderDate = DateTime.Now,
                SupplierId = supplier.SupplierId,
                SupplierCategory = "Manufacturer",
                Source = PurchaseOrderSource.Manual,
                Status = PurchaseOrderStatus.Converted,
                IsUrgent = false
            };
            db.PurchaseOrders.Add(purchaseOrder);
            await db.SaveChangesAsync();

            db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = purchaseOrder.Id,
                ProductId = product.Id,
                UnitId = 1,
                LastPurchasePrice = 4.5m,
                StockQty = 0,
                RequiredQty = 1000,
                OrderQty = 1000,
                ReceivingQty = 1000,
                IsCancelled = false
            });

            var purchaseInvoice = new PurchaseInvoice
            {
                InvoiceNo = "INV-000001",
                PurchaseDate = DateTime.Now,
                SupplierId = supplier.SupplierId,
                PurchaseOrderId = purchaseOrder.Id,
                WarehouseId = mainWarehouse.Id,
                PaymentMethod = "Cash",
                Advance = 2000,
                Due = (1000 * 4.5m) - 2000,
                Total = 1000 * 4.5m,
                Discount = 0,
                TaxOrOthers = 0,
                PaymentStatus = "InComplete",
                IsSupplierWise = true
            };
            db.PurchaseInvoices.Add(purchaseInvoice);
            await db.SaveChangesAsync();

            db.PurchaseInvoiceItems.Add(new PurchaseInvoiceItem
            {
                PurchaseInvoiceId = purchaseInvoice.Id,
                ProductId = product.Id,
                UnitId = 1,
                Quantity = 1000,
                UnitCost = 4.5m,
                SubTotal = 1000 * 4.5m,
                BatchNumber = "BATCH-001",
                ExpiryDate = DateTime.Now.AddYears(2)
            });
            await db.SaveChangesAsync();

            // Seed data writes PurchaseInvoiceItems directly, bypassing
            // PurchaseInvoicesController's RecalculatePurchaseQtyAsync, so
            // Product.PurchaseQty needs to be set explicitly here too or it
            // stays 0 despite the 1000-unit purchase seeded above.
            product.PurchaseQty = 1000;

            // ---------- 7) Product Stock (batch actually usable for sales) ----------
            var stock = new ProductStock
            {
                ProductId = product.Id,
                WarehouseId = mainWarehouse.Id,
                ProductRakId = rakA1.Id,
                BatchNumber = "BATCH-001",
                Quantity = 1000,
                AvailableQuantity = 1000,
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)),
                SupplierId = supplier.SupplierId,
                PurchaseInvoiceId = purchaseInvoice.Id,
                ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            db.ProductStocks.Add(stock);
            await db.SaveChangesAsync();

            // usable ProductStockId to pick from for Sale items.
            var stock2 = new ProductStock
            {
                ProductId = product.Id,
                WarehouseId = branchWarehouse.Id,
                BatchNumber = "BATCH-002",
                Quantity = 300,
                AvailableQuantity = 300,
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SupplierId = supplier.SupplierId,
                ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10))
            };
            db.ProductStocks.Add(stock2);
            await db.SaveChangesAsync();

            var stock3 = new ProductStock
            {
                ProductId = product.Id,
                WarehouseId = mainWarehouse.Id,
                ProductRakId = rakA2.Id,
                BatchNumber = "BATCH-003",
                Quantity = 500,
                AvailableQuantity = 500,
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(18)),
                SupplierId = supplier.SupplierId,
                ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2))
            };
            db.ProductStocks.Add(stock3);
            await db.SaveChangesAsync();

            if (!await db.PaymentMethods.AnyAsync())
            {
                db.PaymentMethods.AddRange(
                    new PaymentMethod { Name = "Cash", LedgerAccountCode = "1000", IsActive = true },
                    new PaymentMethod { Name = "Card", LedgerAccountCode = "1010", IsActive = true },
                    new PaymentMethod { Name = "Mobile Banking", LedgerAccountCode = "1020", IsActive = true }
                );
                await db.SaveChangesAsync();
            }

            // ---------- 8) Supplier Payment against the invoice ----------
            var supplierPayment = new SupplierPayment
            {
                PaidNo = "PAY-000001",
                PaidDate = DateTime.Now,
                SupplierId = supplier.SupplierId,
                TotalAmount = 2000
            };
            db.SupplierPayments.Add(supplierPayment);
            await db.SaveChangesAsync();

            db.SupplierPaymentDetails.Add(new SupplierPaymentDetail
            {
                SupplierPaymentId = supplierPayment.Id,
                PurchaseInvoiceId = purchaseInvoice.Id,
                LineNo = 1,
                TotalAmount = purchaseInvoice.Total,
                DueBeforePayment = purchaseInvoice.Total,
                PrePaid = 0,
                PaidAmount = 2000
            });

            // ---------- 9) Purchase Return (small, against the same invoice) ----------
            var purchaseReturn = new PurchaseReturn
            {
                ReturnNo = "PR-000001",
                ReturnDate = DateTime.Now,
                PurchaseInvoiceId = purchaseInvoice.Id,
                ReturnTotal = 10 * 4.5m,
                Reason = "Damaged strip",
                IsCompleted = true
            };
            db.PurchaseReturns.Add(purchaseReturn);
            await db.SaveChangesAsync();

            db.PurchaseReturnItems.Add(new PurchaseReturnItem
            {
                PurchaseReturnId = purchaseReturn.Id,
                ProductId = product.Id,
                Quantity = 10,
                UnitId = 1,
                BaseQuantity = 10,
                UnitCost = 4.5m,
                SubTotal = 10 * 4.5m
            });
            db.PurchaseReturnReceives.Add(new PurchaseReturnReceive
            {
                PurchaseReturnId = purchaseReturn.Id,
                ReceivedAmount = 45,
                ReceivedDate = DateTime.Now,
                Note = "Credit note received from supplier"
            });
            await db.SaveChangesAsync();

            // ---------- 10) Sale (+ items) — decrements ProductStock like the real controller does ----------
            var sale = new Sale
            {
                InvoiceNo = $"SINV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                CustomerId = customer.CustomerId,
                SaleDate = DateTime.UtcNow,
                PaymentMethod = "Cash",
                CashierId = cashierUser.Id.ToString(),
                IsPaid = true
            };
            sale.Items.Add(new SaleItem { ProductStockId = stock.Id, UnitId = 1, Quantity = 2, UnitPrice = 5.5m });
            sale.TotalAmount = 2 * 5.5m;
            stock.AvailableQuantity -= 2;
            db.Sales.Add(sale);
            await db.SaveChangesAsync();

            // ---------- 11) Sale Return (partial return of the sale above) ----------
            var saleReturn = new SaleReturn
            {
                ReturnNo = "SR-000001",
                ReturnDate = DateTime.Now,
                SaleId = sale.SaleId,
                ReturnTotal = 5.5m,
                Reason = "Customer changed mind",
                IsDeleted = false
            };
            db.SaleReturns.Add(saleReturn);
            await db.SaveChangesAsync();

            db.SaleReturnItems.Add(new SaleReturnItem
            {
                SaleReturnId = saleReturn.Id,
                MedicineId = product.Id,
                Quantity = 1,
                UnitId = 1,
                BaseQuantity = 1,
                SalesPrice = 5.5m,
                SubTotal = 5.5m
            });

            // ---------- 12) Stock Transfer between warehouses ----------
            var transfer = new StockTransfer
            {
                InvoiceId = "ST-000001",
                TransferDate = DateTime.Now,
                FromWarehouseId = mainWarehouse.Id,
                ToWarehouseId = branchWarehouse.Id,
                TotalQty = 50,
                CreatedByUserId = adminUser.Id,
                IsReceived = false
            };
            db.StockTransfers.Add(transfer);
            await db.SaveChangesAsync();

            db.StockTransferItems.Add(new StockTransferItem
            {
                StockTransferId = transfer.Id,
                MedicineId = product.Id,
                Quantity = 50,
                ExpireDate = DateTime.Now.AddYears(2)
            });

            // ---------- 13) Expired stock write-off (separate small batch) ----------
            var expiringStock = new ProductStock
            {
                ProductId = product.Id,
                WarehouseId = mainWarehouse.Id,
                ProductRakId = rakA2.Id,
                BatchNumber = "BATCH-OLD-001",
                Quantity = 20,
                AvailableQuantity = 20,
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), // already expired
                SupplierId = supplier.SupplierId,
                ReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1))
            };
            db.ProductStocks.Add(expiringStock);
            await db.SaveChangesAsync();

            db.ExpiredProductStocks.Add(new ExpiredProductStock
            {
                ProductId = product.Id,
                ProductStockId = expiringStock.Id,
                WarehouseId = mainWarehouse.Id,
                Quantity = 20,
                DisposalMethod = "Returned to supplier",
                DisposalDate = DateTime.Now,
                ApprovedByUserId = adminUser.Id,
                Note = "Expired batch cleared from Rack-A2"
            });

            // ---------- 14) Daily purchase requirement (planning row) ----------
            db.DailyPurchaseRequirements.Add(new DailyPurchaseRequirementTable
            {
                SerialNo = Guid.NewGuid(),
                ProductId = product.Id,
                UnitId = 1,
                LastPurchaseRate = 4.5m,
                StockQty = 1000,
                RequiredQty = 500,
                OrdersQty = 500
            });

            // ---------- 15) Accounting: Chart of Accounts + Ledger ----------
            var cashAccount = new ChartOfAccount { Name = "Cash on Hand", Code = "1000", AccountType = "Asset", IsSystem = true };
            var salesRevenue = new ChartOfAccount { Name = "Sales Revenue", Code = "4000", AccountType = "Revenue", IsSystem = true };
            db.ChartOfAccounts.AddRange(cashAccount, salesRevenue,
                new ChartOfAccount { Name = "Bank Account", Code = "1010", AccountType = "Asset", IsSystem = true },
                new ChartOfAccount { Name = "Mobile Financial Services", Code = "1020", AccountType = "Asset", IsSystem = true },
                new ChartOfAccount { Name = "Accounts Receivable", Code = "1100", AccountType = "Asset", IsSystem = true },
                new ChartOfAccount { Name = "Inventory", Code = "1200", AccountType = "Asset", IsSystem = true },
                new ChartOfAccount { Name = "Fixed Assets", Code = "1500", AccountType = "Asset", IsSystem = true },
                new ChartOfAccount { Name = "Accumulated Depreciation", Code = "1600", AccountType = "Asset", IsSystem = true },
                new ChartOfAccount { Name = "Accounts Payable", Code = "2000", AccountType = "Liability", IsSystem = true },
                new ChartOfAccount { Name = "Loans and Other Liabilities", Code = "2100", AccountType = "Liability", IsSystem = true },
                new ChartOfAccount { Name = "Cost of Goods Sold", Code = "5000", AccountType = "Expense", IsSystem = true },
                new ChartOfAccount { Name = "Sales Returns", Code = "4100", AccountType = "Revenue", IsSystem = true },
                new ChartOfAccount { Name = "Damage and Expiry Loss", Code = "5100", AccountType = "Expense", IsSystem = true },
                new ChartOfAccount { Name = "Depreciation Expense", Code = "5200", AccountType = "Expense", IsSystem = true });
            await db.SaveChangesAsync();

            db.LedgerAccounts.AddRange(
                new LedgerAccount
                {
                    ChartOfAccountId = cashAccount.Id,
                    VoucherNo = $"SALE-{sale.SaleId}",
                    TransactionDate = sale.SaleDate,
                    DebitAmount = sale.TotalAmount,
                    CreditAmount = 0,
                    RunningBalance = sale.TotalAmount,
                    Description = $"Cash received for Sale #{sale.SaleId}",
                    SourceType = LedgerSourceType.Sale,
                    SourceId = sale.SaleId
                },
                new LedgerAccount
                {
                    ChartOfAccountId = salesRevenue.Id,
                    VoucherNo = $"SALE-{sale.SaleId}",
                    TransactionDate = sale.SaleDate,
                    DebitAmount = 0,
                    CreditAmount = sale.TotalAmount,
                    RunningBalance = sale.TotalAmount,
                    Description = $"Revenue for Sale #{sale.SaleId}",
                    SourceType = LedgerSourceType.Sale,
                    SourceId = sale.SaleId
                }
            );

            // ---------- 16) Company assets / liabilities ----------
            db.CompanyAssets.Add(new CompanyAsset
            {
                Name = "Delivery Van",
                Value = 1200000,
                AcquiredDate = DateTime.UtcNow.AddYears(-1),
                SalvageValue = 100000,
                UsefulLifeYears = 8,
                DepreciationRatePercent = 12.5m,
                DepreciationMethod = DepreciationMethod.StraightLine,
                AccumulatedDepreciation = 150000,
                BookValue = 1050000,
                NextDepreciationDate = DateTime.UtcNow.AddMonths(1),
                Description = "Main delivery vehicle"
            });

            db.CompanyLiabilities.Add(new CompanyLiability
            {
                Name = "Bank Loan - Main Branch",
                LiabilityType = LiabilityType.Loan,
                Amount = 500000,
                PaidAmount = 100000,
                OutstandingAmount = 400000,
                LiabilityDate = DateTime.UtcNow.AddMonths(-6),
                DueDate = DateTime.UtcNow.AddYears(2),
                InterestRate = 9.5m,
                Description = "Working capital loan"
            });

            // ---------- 17) HR: Employees + documents ----------
            var employee = new Employee
            {
                FullName = "Sadia Islam",
                Email = "sadia.islam@pharmacy.local",
                Salary = 35000,
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true,
                DepartmentId = 2 // Sales (seeded)
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();

            db.EmployeeDocuments.Add(new EmployeeDocument
            {
                EmployeeId = employee.Id,
                DocumentTitle = "National ID",
                DocumentNumber = "1990123456789",
                IssueDate = DateTime.UtcNow.AddYears(-6),
                IsVerified = true
            });

            // ---------- 18) SMS log ----------
            db.SmsLogs.Add(new SmsLog
            {
                PhoneNumber = customer.Phone ?? "01710000000",
                Message = $"Thank you for your purchase of {sale.TotalAmount:0.00} tk. Sale #{sale.SaleId}.",
                SentAt = DateTime.Now,
                IsSuccess = true,
                Response = "Delivered"
            });

            // Product.StockQuantity mirrors the sum of AvailableQuantity across
            // every ProductStock batch — same formula as RecalculateStockQuantityAsync
            // in PurchaseInvoicesController/SalesController. Seed data bypasses
            // those controllers, so it has to be set explicitly here too (same
            // reasoning as product.PurchaseQty above), otherwise it stays stuck
            // at 0 despite four batches (998 + 300 + 500 + 20) being seeded.
            product.StockQuantity = (int)(stock.AvailableQuantity + stock2.AvailableQuantity
                + stock3.AvailableQuantity + expiringStock.AvailableQuantity);

            await db.SaveChangesAsync();
        }
    }
}
```


## 7. Services (Business Logic)

### `AccountingPostingService.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.Enums;
using PharmacyV2.Models.Accounts;

namespace PharmacyV2.Services;

public interface IAccountingPostingService
{
    Task EnsureSystemAccountsAsync();
    Task PostAsync(LedgerSourceType sourceType, int sourceId, DateTime date, string description, params (string Code, decimal Debit, decimal Credit)[] lines);
    Task RecalculateRunningBalancesAsync(IEnumerable<int> chartOfAccountIds);
}

public sealed class AccountingPostingService : IAccountingPostingService
{
    private readonly PharmacyDbContext _db;
    public AccountingPostingService(PharmacyDbContext db) => _db = db;

    public async Task EnsureSystemAccountsAsync()
    {
        var accounts = new (string Code, string Name, string Type)[] { ("1000", "Cash on Hand", "Asset"), ("1010", "Bank Account", "Asset"), ("1020", "Mobile Financial Services", "Asset"), ("1100", "Accounts Receivable", "Asset"), ("1200", "Inventory", "Asset"), ("1500", "Fixed Assets", "Asset"), ("1600", "Accumulated Depreciation", "Asset"), ("2000", "Accounts Payable", "Liability"), ("2100", "Loans and Other Liabilities", "Liability"), ("4000", "Sales Revenue", "Revenue"), ("4100", "Sales Returns", "Revenue"), ("5000", "Cost of Goods Sold", "Expense"), ("5100", "Damage and Expiry Loss", "Expense"), ("5200", "Depreciation Expense", "Expense") };
        var codes = accounts.Select(x => x.Code).ToList();
        var existing = await _db.ChartOfAccounts.Where(a => codes.Contains(a.Code!)).Select(a => a.Code).ToListAsync();
        _db.ChartOfAccounts.AddRange(accounts.Where(a => !existing.Contains(a.Code)).Select(a => new ChartOfAccount { Code = a.Code, Name = a.Name, AccountType = a.Type, IsSystem = true, IsActive = true }));
        await _db.SaveChangesAsync();
    }

    public async Task PostAsync(LedgerSourceType sourceType, int sourceId, DateTime date, string description, params (string Code, decimal Debit, decimal Credit)[] lines)
    {
        if (lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit)) throw new InvalidOperationException("Automatic accounting entry is not balanced.");
        if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == sourceType && l.SourceId == sourceId)) return;
        var codes = lines.Select(x => x.Code).Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.Where(a => codes.Contains(a.Code!) && a.IsActive).ToDictionaryAsync(a => a.Code!);
        if (accounts.Count != codes.Count) throw new InvalidOperationException("Required system chart-of-account mapping is missing.");
        var voucher = $"AUTO-{sourceType}-{sourceId}";
        foreach (var line in lines.Where(x => x.Debit > 0 || x.Credit > 0))
        {
            var account = accounts[line.Code];
            _db.LedgerAccounts.Add(new LedgerAccount
            {
                ChartOfAccountId = account.Id,
                VoucherNo = voucher,
                TransactionDate = date,
                DebitAmount = line.Debit,
                CreditAmount = line.Credit,
                Description = description,
                SourceType = sourceType,
                SourceId = sourceId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "system"
            });
        }
        // Persist first so new rows have deterministic IDs. Rebuild the
        // complete account sequence, not only the latest balance: this keeps
        // statements correct when a valid backdated transaction is entered.
        await _db.SaveChangesAsync();
        await RecalculateRunningBalancesAsync(accounts.Values.Select(a => a.Id));
    }

    public async Task RecalculateRunningBalancesAsync(IEnumerable<int> chartOfAccountIds)
    {
        foreach (var accountId in chartOfAccountIds.Distinct())
        {
            decimal balance = 0;
            var rows = await _db.LedgerAccounts.Where(l => l.ChartOfAccountId == accountId)
                .OrderBy(l => l.TransactionDate).ThenBy(l => l.Id).ToListAsync();
            foreach (var row in rows)
            {
                balance += row.DebitAmount - row.CreditAmount;
                row.RunningBalance = balance;
            }
        }
        await _db.SaveChangesAsync();
    }
}
```

### `AuditService.cs`

```csharp
using PharmacyV2.Data;
using PharmacyV2.Models.Other;

namespace PharmacyV2.Services;

public interface IAuditService { Task LogAsync(string action, string entityName, int entityId, string? details, string? userId); }
public sealed class AuditService : IAuditService
{
    private readonly PharmacyDbContext _db;
    public AuditService(PharmacyDbContext db) => _db = db;
    public async Task LogAsync(string action, string entityName, int entityId, string? details, string? userId)
    {
        _db.AuditLogs.Add(new AuditLog { Action = action, EntityName = entityName, EntityId = entityId, Details = details, UserId = userId, OccurredAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }
}
```

### `CodeGeneratorService.cs`

```csharp
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
    }
}
```

### `ICodeGeneratorService.cs`

```csharp
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
    }
}
```

### `ITokenService.cs`

```csharp
using PharmacyV2.Models.Other;

namespace PharmacyV2.Services
{
    public interface ITokenService
    {
        // Builds a signed JWT for the given user (with their role, if any,
        // baked in as a claim) and returns both the token and its expiry.
        (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user);
    }
}
```

### `TokenService.cs`

```csharp
using Microsoft.IdentityModel.Tokens;
using PharmacyV2.Models.Other;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PharmacyV2.Services
{
    // Reads the "Jwt" section from appsettings.json and issues HS256-signed
    // bearer tokens. Registered as a singleton in Program.cs.
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;

        public TokenService(IConfiguration config)
        {
            _config = config;
        }

        public (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user)
        {
            var jwtSection = _config.GetSection("JwtSettings");
            var key = jwtSection["SecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var expiresMinutes = int.TryParse(jwtSection["ExpiresMinutes"], out var m) ? m : 120;

            // Standard identity claims plus the role name, so [Authorize(Roles = "Admin")]
            // works on controllers/actions without an extra DB lookup per request.
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
            };

            if (!string.IsNullOrWhiteSpace(user.Email))
                claims.Add(new Claim(ClaimTypes.Email, user.Email));

            if (user.Role is not null)
                claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(expiresMinutes);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expires,
                signingCredentials: credentials);

            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }
    }
}
```


## 8. Controllers (API Endpoints)

### `AuthController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Other;
using PharmacyV2.Services;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // Public authentication endpoints. Everything else in the solution is
    // [Authorize] by default (see Program.cs), so Register/Login are the two
    // doors into the API from Postman.
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IPasswordHasher<AppUser> _passwordHasher;

        public AuthController(PharmacyDbContext db, ITokenService tokenService, IPasswordHasher<AppUser> passwordHasher)
        {
            _db = db;
            _tokenService = tokenService;
            _passwordHasher = passwordHasher;
        }

        // POST api/auth/register
        // Creates a new AppUser with a securely hashed password (never store
        // plain text). Does NOT return a token — the client must call
        // POST /api/auth/login afterwards to obtain one.
        [Authorize(Roles = "Admin")]
        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto dto)
        {
            if (await _db.AppUsers.AnyAsync(u => u.Username == dto.Username))
                return Conflict(new { message = $"Username '{dto.Username}' is already taken." });

            if (dto.RoleId is not null && !await _db.Roles.AnyAsync(r => r.Id == dto.RoleId))
                return BadRequest(new { message = $"Role {dto.RoleId} does not exist." });

            var user = new AppUser
            {
                Username = dto.Username,
                FullName = dto.FullName,
                Email = dto.Email,
                Phone = dto.Phone,
                RoleId = dto.RoleId,
                IsActive = true
            };
            // PasswordHasher salts + hashes internally (PBKDF2) — PasswordHash
            // never contains the raw password.
            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            await _db.Entry(user).Reference(u => u.Role).LoadAsync();

            return StatusCode(201, new RegisterResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                RoleName = user.Role?.Name
            });
        }

        // POST api/auth/login
        // Verifies the username/password against the stored hash and, if
        // valid, issues a fresh JWT. This is the endpoint you call in Postman
        // to get the Bearer token for every other request.
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto)
        {
            var user = await _db.AppUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == dto.Username);

            // Same generic error for "no such user" and "wrong password" —
            // never reveal which one it was, that leaks valid usernames.
            if (user is null || !user.IsActive)
                return Unauthorized(new { message = "Invalid username or password." });

            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
            if (verifyResult == PasswordVerificationResult.Failed)
                return Unauthorized(new { message = "Invalid username or password." });

            var (token, expires) = _tokenService.CreateToken(user);

            return Ok(new AuthResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                RoleName = user.Role?.Name,
                Token = token,
                ExpiresAtUtc = expires
            });
        }

        // GET api/auth/me
        // Requires a valid Bearer token. Reads the user id out of the JWT's
        // claims (set in TokenService) and returns their profile — handy in
        // Postman to sanity-check that a token actually works.
        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserProfileDto>> Me()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var user = await _db.AppUsers
                .Include(u => u.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user is null)
                return NotFound();

            return Ok(new UserProfileDto
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                IsActive = user.IsActive,
                RoleName = user.Role?.Name
            });
        }
    }
}
```

### `Chartofaccountscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Accounts;

namespace PharmacyV2.Controllers
{
    // CRUD for ChartOfAccount, the self-referencing tree of account
    // definitions (Cash, Sales Revenue, Accounts Payable, ...) that
    // LedgerAccount postings point at. Had a model + DbSet + fluent config
    // but no controller.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChartOfAccountsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public ChartOfAccountsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/chartofaccounts — flat list, optionally ?parentId=3 for one branch's
        // direct children, or ?parentId=0 / omitted for top-level roots + everything
        // (pass parentId to page through the tree from the client).
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ChartOfAccountListItemDto>>> GetAll([FromQuery] int? parentId)
        {
            var query = _db.ChartOfAccounts.AsNoTracking().Include(a => a.Parent).AsQueryable();

            if (parentId is not null)
                query = query.Where(a => a.ParentId == parentId);

            var accounts = await query
                .Select(a => new ChartOfAccountListItemDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Code = a.Code,
                    AccountType = a.AccountType,
                    ParentId = a.ParentId,
                    ParentName = a.Parent != null ? a.Parent.Name : null,
                    IsActive = a.IsActive,
                    IsSystem = a.IsSystem,
                    BudgetAmount = a.BudgetAmount,
                    ChildCount = a.Children.Count
                })
                .ToListAsync();

            return Ok(accounts);
        }

        // GET api/chartofaccounts/5 — includes its direct children.
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ChartOfAccountReadDto>> GetById(int id)
        {
            var account = await _db.ChartOfAccounts
                .AsNoTracking()
                .Include(a => a.Parent)
                .Include(a => a.Children)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (account is null)
                return NotFoundResponse($"Account {id} not found.");

            return Ok(new ChartOfAccountReadDto
            {
                Id = account.Id,
                Name = account.Name,
                Code = account.Code,
                AccountType = account.AccountType,
                ParentId = account.ParentId,
                ParentName = account.Parent?.Name,
                IsActive = account.IsActive,
                IsSystem = account.IsSystem,
                BudgetAmount = account.BudgetAmount,
                ChildCount = account.Children.Count,
                Children = account.Children.Select(c => new ChartOfAccountListItemDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Code = c.Code,
                    AccountType = c.AccountType,
                    ParentId = c.ParentId,
                    ParentName = account.Name,
                    IsActive = c.IsActive,
                    IsSystem = c.IsSystem,
                    BudgetAmount = c.BudgetAmount,
                    ChildCount = 0
                }).ToList()
            });
        }

        // POST api/chartofaccounts
        [HttpPost]
        public async Task<ActionResult<ChartOfAccountReadDto>> Create([FromBody] ChartOfAccountWriteDto dto)
        {
            if (!IsValidAccountType(dto.AccountType)) return BadRequestResponse("AccountType must be Asset, Liability, Equity, Revenue, or Expense.");
            if (!string.IsNullOrWhiteSpace(dto.Code) && await _db.ChartOfAccounts.AnyAsync(a => a.Code == dto.Code)) return ConflictResponse($"Account code '{dto.Code}' is already in use.");
            if (dto.ParentId is not null && !await _db.ChartOfAccounts.AnyAsync(a => a.Id == dto.ParentId))
                return BadRequestResponse($"Parent account {dto.ParentId} does not exist.");

            var account = new ChartOfAccount
            {
                Name = dto.Name,
                Code = dto.Code,
                AccountType = dto.AccountType,
                ParentId = dto.ParentId,
                IsActive = dto.IsActive,
                BudgetAmount = dto.BudgetAmount
            };

            _db.ChartOfAccounts.Add(account);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = account.Id }, new ChartOfAccountReadDto
            {
                Id = account.Id,
                Name = account.Name,
                Code = account.Code,
                AccountType = account.AccountType,
                ParentId = account.ParentId,
                IsActive = account.IsActive,
                IsSystem = account.IsSystem,
                BudgetAmount = account.BudgetAmount
            });
        }

        // PUT api/chartofaccounts/5 — IsSystem accounts (seeded/built-in, e.g.
        // a default Cash or Accounts Payable account) can't be re-parented or
        // renamed, since other modules may assume they exist under a fixed
        // name/Code; toggling IsActive on them is still allowed.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ChartOfAccountWriteDto dto)
        {
            if (!IsValidAccountType(dto.AccountType)) return BadRequestResponse("AccountType must be Asset, Liability, Equity, Revenue, or Expense.");
            var account = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null)
                return NotFoundResponse($"Account {id} not found.");

            if (await _db.LedgerAccounts.AnyAsync(l => l.ChartOfAccountId == id) &&
                (account.AccountType != dto.AccountType || account.ParentId != dto.ParentId || account.Code != dto.Code))
                return ConflictResponse("An account with ledger postings cannot have its type, code, or hierarchy changed.");

            if (account.IsSystem &&
                (account.Name != dto.Name || account.AccountType != dto.AccountType || account.ParentId != dto.ParentId))
            {
                return ConflictResponse("This is a system account — only IsActive/BudgetAmount can be changed on it.");
            }

            if (dto.ParentId == id)
                return BadRequestResponse("An account cannot be its own parent.");

            if (dto.ParentId is not null)
            {
                if (!await _db.ChartOfAccounts.AnyAsync(a => a.Id == dto.ParentId))
                    return BadRequestResponse($"Parent account {dto.ParentId} does not exist.");

                if (await WouldCreateCycleAsync(id, dto.ParentId.Value))
                    return BadRequestResponse("That parent is a descendant of this account — would create a circular hierarchy.");
            }

            if (!string.IsNullOrWhiteSpace(dto.Code) && await _db.ChartOfAccounts.AnyAsync(a => a.Code == dto.Code && a.Id != id)) return ConflictResponse($"Account code '{dto.Code}' is already in use.");

            account.Name = dto.Name;
            account.Code = dto.Code;
            account.AccountType = dto.AccountType;
            account.ParentId = dto.ParentId;
            account.IsActive = dto.IsActive;
            account.BudgetAmount = dto.BudgetAmount;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/chartofaccounts/5 — blocked for system accounts, accounts
        // with children, or accounts with existing ledger postings.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var account = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null)
                return NotFoundResponse($"Account {id} not found.");

            if (account.IsSystem)
                return ConflictResponse("Cannot delete a system account.");

            if (await _db.ChartOfAccounts.AnyAsync(a => a.ParentId == id))
                return ConflictResponse("Cannot delete an account that has child accounts — remove or reassign them first.");

            if (await _db.LedgerAccounts.AnyAsync(l => l.ChartOfAccountId == id))
                return ConflictResponse("Cannot delete an account with existing ledger postings.");

            _db.ChartOfAccounts.Remove(account);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // Walks upward from candidateParentId through Parent links; if it
        // reaches accountId, setting that parent on accountId would create a cycle.
        private async Task<bool> WouldCreateCycleAsync(int accountId, int candidateParentId)
        {
            int? currentId = candidateParentId;
            var visited = new HashSet<int>();

            while (currentId is not null)
            {
                if (currentId == accountId)
                    return true;

                if (!visited.Add(currentId.Value))
                    break; // already-broken cycle elsewhere in the data; stop rather than loop forever

                currentId = await _db.ChartOfAccounts
                    .Where(a => a.Id == currentId)
                    .Select(a => a.ParentId)
                    .FirstOrDefaultAsync();
            }

            return false;
        }

        private static bool IsValidAccountType(string type) => new[] { "Asset", "Liability", "Equity", "Revenue", "Expense" }.Contains(type, StringComparer.OrdinalIgnoreCase);

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `CompaniesController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // CRUD for the Company lookup table — feeds the "Company" filter
    // dropdown on the Brand Search page and the CompanyId FK on Product.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CompaniesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public CompaniesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/companies?search=square
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanyReadDto>>> GetAll([FromQuery] string? search)
        {
            var query = _db.Companies.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search));

            var companies = await query
                .OrderBy(c => c.Name)
                .Select(c => new CompanyReadDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    IsActive = c.IsActive,
                    ProductCount = c.Products.Count
                })
                .ToListAsync();

            return Ok(companies);
        }

        // GET api/companies/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CompanyReadDto>> GetById(int id)
        {
            var company = await _db.Companies.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CompanyReadDto { Id = c.Id, Name = c.Name, IsActive = c.IsActive, ProductCount = c.Products.Count })
                .FirstOrDefaultAsync();

            if (company is null) return NotFoundResponse($"Company {id} not found.");
            return Ok(company);
        }

        // POST api/companies
        [HttpPost]
        public async Task<ActionResult<CompanyReadDto>> Create([FromBody] CompanyWriteDto dto)
        {
            if (await _db.Companies.AnyAsync(c => c.Name == dto.Name))
                return ConflictResponse($"Company '{dto.Name}' already exists.");

            var company = new Company { Name = dto.Name, IsActive = dto.IsActive };
            _db.Companies.Add(company);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = company.Id },
                new CompanyReadDto { Id = company.Id, Name = company.Name, IsActive = company.IsActive, ProductCount = 0 });
        }

        // PUT api/companies/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CompanyWriteDto dto)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is null) return NotFoundResponse($"Company {id} not found.");

            if (await _db.Companies.AnyAsync(c => c.Name == dto.Name && c.Id != id))
                return ConflictResponse($"Company '{dto.Name}' already exists.");

            company.Name = dto.Name;
            company.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/companies/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is null) return NotFoundResponse($"Company {id} not found.");

            if (await _db.Products.AnyAsync(p => p.CompanyId == id))
                return ConflictResponse("Cannot delete a company that products are using.");

            _db.Companies.Remove(company);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `Companyassetscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Enums;
using PharmacyV2.Models.Accounts;
using PharmacyV2.Services;

namespace PharmacyV2.Controllers
{
    // CRUD for CompanyAsset (fixed assets — equipment, fixtures, vehicles,
    // ...) plus a depreciation-run action that actually uses the
    // DepreciationMethod/AccumulatedDepreciation/BookValue fields the model
    // already carries. Had a model + DbSet but no controller.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CompanyAssetsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;

        public CompanyAssetsController(PharmacyDbContext db, IAccountingPostingService accounting)
        {
            _db = db;
            _accounting = accounting;
        }

        // GET api/companyassets
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanyAssetReadDto>>> GetAll()
        {
            var assets = await _db.CompanyAssets.AsNoTracking().Select(a => MapToReadDto(a)).ToListAsync();
            return Ok(assets);
        }

        // GET api/companyassets/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CompanyAssetReadDto>> GetById(int id)
        {
            var asset = await _db.CompanyAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            if (asset is null)
                return NotFoundResponse($"Asset {id} not found.");

            return Ok(MapToReadDto(asset));
        }

        // POST api/companyassets — BookValue starts equal to Value; no
        // depreciation has been run yet.
        [HttpPost]
        public async Task<ActionResult<CompanyAssetReadDto>> Create([FromBody] CompanyAssetWriteDto dto)
        {
            var asset = new CompanyAsset
            {
                Name = dto.Name,
                Value = dto.Value,
                AcquiredDate = dto.AcquiredDate,
                SalvageValue = dto.SalvageValue,
                UsefulLifeYears = dto.UsefulLifeYears,
                DepreciationRatePercent = dto.DepreciationRatePercent,
                DepreciationMethod = dto.DepreciationMethod,
                AccumulatedDepreciation = 0,
                BookValue = dto.Value,
                NextDepreciationDate = dto.AcquiredDate.AddYears(1),
                Description = dto.Description,
                IsActive = dto.IsActive
            };

            _db.CompanyAssets.Add(asset);
            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.Adjustment, asset.Id, asset.AcquiredDate, $"Asset acquired: {asset.Name}", ("1500", asset.Value, 0), ("2000", 0, asset.Value));
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = asset.Id }, MapToReadDto(asset));
        }

        // PUT api/companyassets/5 — updates the asset's own definition.
        // Depreciation fields (AccumulatedDepreciation/BookValue/NextDepreciationDate)
        // are managed only by the /depreciate action below, not this endpoint.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CompanyAssetWriteDto dto)
        {
            var asset = await _db.CompanyAssets.FirstOrDefaultAsync(a => a.Id == id);
            if (asset is null)
                return NotFoundResponse($"Asset {id} not found.");

            asset.Name = dto.Name;
            asset.Value = dto.Value;
            asset.AcquiredDate = dto.AcquiredDate;
            asset.SalvageValue = dto.SalvageValue;
            asset.UsefulLifeYears = dto.UsefulLifeYears;
            asset.DepreciationRatePercent = dto.DepreciationRatePercent;
            asset.DepreciationMethod = dto.DepreciationMethod;
            asset.Description = dto.Description;
            asset.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/companyassets/5 — no other table has a FK to
        // CompanyAsset (see LedgerAccount.SourceId comment: it's polymorphic,
        // not a real FK), so this is a plain delete.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var asset = await _db.CompanyAssets.FirstOrDefaultAsync(a => a.Id == id);
            if (asset is null)
                return NotFoundResponse($"Asset {id} not found.");

            _db.CompanyAssets.Remove(asset);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/companyassets/5/depreciate — runs exactly one annual
        // depreciation period (StraightLine: (Value - SalvageValue) /
        // UsefulLifeYears; ReducingBalance: BookValue * DepreciationRatePercent%),
        // capped so BookValue never drops below SalvageValue, and advances
        // NextDepreciationDate by one year. Call it once per period (e.g. from
        // a yearly scheduled job) rather than repeatedly in the same period.
        [HttpPost("{id:int}/depreciate")]
        public async Task<ActionResult<CompanyAssetReadDto>> Depreciate(int id)
        {
            var asset = await _db.CompanyAssets.FirstOrDefaultAsync(a => a.Id == id);
            if (asset is null)
                return NotFoundResponse($"Asset {id} not found.");

            if (!asset.IsActive || asset.DisposalDate is not null)
                return ConflictResponse("Cannot depreciate an inactive or disposed asset.");

            if (asset.BookValue <= asset.SalvageValue)
                return ConflictResponse("Asset is already fully depreciated to its salvage value.");

            decimal depreciationAmount = asset.DepreciationMethod switch
            {
                DepreciationMethod.StraightLine => asset.UsefulLifeYears > 0
                    ? (asset.Value - asset.SalvageValue) / asset.UsefulLifeYears
                    : 0,
                DepreciationMethod.ReducingBalance => asset.BookValue * (asset.DepreciationRatePercent / 100m),
                _ => 0
            };

            // Don't depreciate past salvage value.
            decimal maxAllowed = asset.BookValue - asset.SalvageValue;
            if (depreciationAmount > maxAllowed)
                depreciationAmount = maxAllowed;

            asset.AccumulatedDepreciation += depreciationAmount;
            asset.BookValue -= depreciationAmount;
            asset.NextDepreciationDate = (asset.NextDepreciationDate ?? asset.AcquiredDate).AddYears(1);

            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.AssetDepreciation, asset.Id, DateTime.UtcNow, $"Depreciation: {asset.Name}", ("5200", depreciationAmount, 0), ("1600", 0, depreciationAmount));
            await _db.SaveChangesAsync();
            return Ok(MapToReadDto(asset));
        }

        // POST api/companyassets/5/dispose — marks the asset disposed and
        // inactive; no further depreciation runs after this.
        [HttpPost("{id:int}/dispose")]
        public async Task<IActionResult> Dispose(int id, [FromBody] DateTime? disposalDate)
        {
            var asset = await _db.CompanyAssets.FirstOrDefaultAsync(a => a.Id == id);
            if (asset is null)
                return NotFoundResponse($"Asset {id} not found.");

            asset.DisposalDate = disposalDate ?? DateTime.Now;
            asset.IsActive = false;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static CompanyAssetReadDto MapToReadDto(CompanyAsset a) => new()
        {
            Id = a.Id,
            Name = a.Name,
            Value = a.Value,
            AcquiredDate = a.AcquiredDate,
            SalvageValue = a.SalvageValue,
            UsefulLifeYears = a.UsefulLifeYears,
            DepreciationRatePercent = a.DepreciationRatePercent,
            DepreciationMethod = a.DepreciationMethod,
            AccumulatedDepreciation = a.AccumulatedDepreciation,
            BookValue = a.BookValue,
            NextDepreciationDate = a.NextDepreciationDate,
            DisposalDate = a.DisposalDate,
            Description = a.Description,
            IsActive = a.IsActive
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `Companyliabilitiescontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Accounts;
using PharmacyV2.Services;
using PharmacyV2.Enums;

namespace PharmacyV2.Controllers
{
    // CRUD for CompanyLiability (loans, credit cards, leases, tax payable,
    // ...) plus a payment action that keeps PaidAmount/OutstandingAmount in
    // sync — same "action endpoint, not raw field edits" shape as
    // SupplierPayments elsewhere in this project. Had a model + DbSet but no
    // controller.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CompanyLiabilitiesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;

        public CompanyLiabilitiesController(PharmacyDbContext db, IAccountingPostingService accounting)
        {
            _db = db;
            _accounting = accounting;
        }

        // GET api/companyliabilities
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanyLiabilityReadDto>>> GetAll()
        {
            var liabilities = await _db.CompanyLiabilities.AsNoTracking().Select(l => MapToReadDto(l)).ToListAsync();
            return Ok(liabilities);
        }

        // GET api/companyliabilities/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CompanyLiabilityReadDto>> GetById(int id)
        {
            var liability = await _db.CompanyLiabilities.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
            if (liability is null)
                return NotFoundResponse($"Liability {id} not found.");

            return Ok(MapToReadDto(liability));
        }

        // POST api/companyliabilities — OutstandingAmount starts equal to Amount.
        [HttpPost]
        public async Task<ActionResult<CompanyLiabilityReadDto>> Create([FromBody] CompanyLiabilityWriteDto dto)
        {
            var liability = new CompanyLiability
            {
                Name = dto.Name,
                LiabilityType = dto.LiabilityType,
                Amount = dto.Amount,
                PaidAmount = 0,
                OutstandingAmount = dto.Amount,
                LiabilityDate = dto.LiabilityDate,
                DueDate = dto.DueDate,
                InterestRate = dto.InterestRate,
                Description = dto.Description,
                IsActive = dto.IsActive
            };

            _db.CompanyLiabilities.Add(liability);
            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.Adjustment, liability.Id, liability.LiabilityDate, $"Liability created: {liability.Name}", ("1000", liability.Amount, 0), ("2100", 0, liability.Amount));
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = liability.Id }, MapToReadDto(liability));
        }

        // PUT api/companyliabilities/5 — updates the liability's own terms.
        // PaidAmount/OutstandingAmount are managed only by /pay below.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CompanyLiabilityWriteDto dto)
        {
            var liability = await _db.CompanyLiabilities.FirstOrDefaultAsync(l => l.Id == id);
            if (liability is null)
                return NotFoundResponse($"Liability {id} not found.");

            if (dto.Amount < liability.PaidAmount)
                return BadRequestResponse($"New amount ({dto.Amount}) can't be less than what's already been paid ({liability.PaidAmount}).");

            liability.Name = dto.Name;
            liability.LiabilityType = dto.LiabilityType;
            liability.Amount = dto.Amount;
            liability.OutstandingAmount = dto.Amount - liability.PaidAmount;
            liability.LiabilityDate = dto.LiabilityDate;
            liability.DueDate = dto.DueDate;
            liability.InterestRate = dto.InterestRate;
            liability.Description = dto.Description;
            liability.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/companyliabilities/5 — blocked once any payment has
        // been recorded against it, to keep the payment history meaningful.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var liability = await _db.CompanyLiabilities.FirstOrDefaultAsync(l => l.Id == id);
            if (liability is null)
                return NotFoundResponse($"Liability {id} not found.");

            if (liability.PaidAmount > 0)
                return ConflictResponse("Cannot delete a liability that already has payments recorded against it.");

            _db.CompanyLiabilities.Remove(liability);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/companyliabilities/5/pay — records a payment, bumping
        // PaidAmount and shrinking OutstandingAmount; rejects overpayment.
        [HttpPost("{id:int}/pay")]
        public async Task<ActionResult<CompanyLiabilityReadDto>> Pay(int id, [FromBody] LiabilityPaymentDto dto)
        {
            var liability = await _db.CompanyLiabilities.FirstOrDefaultAsync(l => l.Id == id);
            if (liability is null)
                return NotFoundResponse($"Liability {id} not found.");

            if (dto.Amount > liability.OutstandingAmount)
                return BadRequestResponse($"Payment ({dto.Amount}) exceeds outstanding balance ({liability.OutstandingAmount}).");

            liability.PaidAmount += dto.Amount;
            liability.OutstandingAmount -= dto.Amount;

            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.Adjustment, 1_000_000 + liability.Id, DateTime.UtcNow, $"Liability payment: {liability.Name}", ("2100", dto.Amount, 0), ("1000", 0, dto.Amount));
            await _db.SaveChangesAsync();
            return Ok(MapToReadDto(liability));
        }

        private static CompanyLiabilityReadDto MapToReadDto(CompanyLiability l) => new()
        {
            Id = l.Id,
            Name = l.Name,
            LiabilityType = l.LiabilityType,
            Amount = l.Amount,
            PaidAmount = l.PaidAmount,
            OutstandingAmount = l.OutstandingAmount,
            LiabilityDate = l.LiabilityDate,
            DueDate = l.DueDate,
            InterestRate = l.InterestRate,
            Description = l.Description,
            IsActive = l.IsActive
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `CustomersController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    // Master (Customer) + details (CustomerAddress) CRUD.
    // CustomerTypeId is the relational/dropdown field — CustomerType itself
    // has no controller, it's seeded lookup data (see PharmacyDbContext).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;

        // Folder (under wwwroot) where customer photos physically live.
        private const string CustomerImagesRelativeFolder = "images/customers";

        public CustomersController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // wwwroot may not exist yet on a fresh clone. Creates
        // "wwwroot/images/customers" on demand the first time it's needed.
        private string GetCustomerImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "customers");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/customers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerListItemDto>>> GetAll()
        {
            var customers = await _db.Customers
                .AsNoTracking()
                .Include(c => c.CustomerType)
                .Include(c => c.Addresses)
                .Select(c => new CustomerListItemDto
                {
                    CustomerId = c.CustomerId,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    Phone = c.Phone,
                    IsActive = c.IsActive,
                    HasPhoto = c.Photo != null || c.PhotoPath != null,
                    PhotoPath = c.PhotoPath,
                    CustomerTypeId = c.CustomerTypeId,
                    CustomerTypeName = c.CustomerType.Name,
                    Addresses = c.Addresses.Select(a => new CustomerAddressReadDto
                    {
                        Id = a.Id,
                        Label = a.Label,
                        AddressLine = a.AddressLine,
                        City = a.City,
                        IsDefault = a.IsDefault
                    }).ToList()
                })
                .ToListAsync();

            return Ok(customers);
        }

        // GET api/customers/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CustomerReadDto>> GetById(int id)
        {
            var customer = await _db.Customers
                .AsNoTracking()
                .Include(c => c.CustomerType)
                .Include(c => c.Addresses)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer is null)
                return NotFoundResponse($"Customer {id} not found.");

            return Ok(MapToReadDto(customer));
        }

        // POST api/customers — phone number must be unique if provided.
        [HttpPost]
        public async Task<ActionResult<CustomerReadDto>> Create([FromBody] CustomerCreateDto dto)
        {
            var typeExists = await _db.CustomerTypes.AnyAsync(t => t.Id == dto.CustomerTypeId);
            if (!typeExists)
                return BadRequestResponse($"Customer type {dto.CustomerTypeId} does not exist.");

            if (!string.IsNullOrWhiteSpace(dto.Phone) &&
                await _db.Customers.AnyAsync(c => c.Phone == dto.Phone))
            {
                return ConflictResponse($"Phone '{dto.Phone}' is already registered.");
            }

            var customer = new Customer
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Phone = dto.Phone,
                CreditLimit = dto.CreditLimit,
                DateOfBirth = dto.DateOfBirth,
                IsActive = dto.IsActive,
                Photo = dto.Photo,
                PhotoContentType = dto.PhotoContentType,
                PhotoPath = dto.PhotoPath,
                CustomerTypeId = dto.CustomerTypeId
            };

            if (dto.Addresses is { Count: > 0 })
            {
                foreach (var addressDto in dto.Addresses)
                {
                    customer.Addresses.Add(new CustomerAddress
                    {
                        Label = addressDto.Label,
                        AddressLine = addressDto.AddressLine,
                        City = addressDto.City,
                        IsDefault = addressDto.IsDefault
                    });
                }

                NormalizeDefaultAddress(customer.Addresses);
            }

            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            await _db.Entry(customer).Reference(c => c.CustomerType).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = customer.CustomerId }, MapToReadDto(customer));
        }

        // PUT api/customers/5 — full update of master fields, with a full
        // add/update/delete sync of the Addresses collection.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CustomerUpdateDto dto)
        {
            var customer = await _db.Customers
                .Include(c => c.Addresses)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer is null)
                return NotFoundResponse($"Customer {id} not found.");

            var typeExists = await _db.CustomerTypes.AnyAsync(t => t.Id == dto.CustomerTypeId);
            if (!typeExists)
                return BadRequestResponse($"Customer type {dto.CustomerTypeId} does not exist.");

            if (!string.IsNullOrWhiteSpace(dto.Phone) &&
                await _db.Customers.AnyAsync(c => c.Phone == dto.Phone && c.CustomerId != id))
            {
                return ConflictResponse($"Phone '{dto.Phone}' is already registered.");
            }

            customer.FirstName = dto.FirstName;
            customer.LastName = dto.LastName;
            customer.Phone = dto.Phone;
            customer.CreditLimit = dto.CreditLimit;
            customer.DateOfBirth = dto.DateOfBirth;
            customer.IsActive = dto.IsActive;
            if (dto.Photo is not null) customer.Photo = dto.Photo;
            if (dto.PhotoContentType is not null) customer.PhotoContentType = dto.PhotoContentType;
            if (dto.PhotoPath is not null) customer.PhotoPath = dto.PhotoPath;
            customer.CustomerTypeId = dto.CustomerTypeId;

            if (dto.Addresses is not null)
            {
                var incomingIds = dto.Addresses.Where(a => a.Id is > 0).Select(a => a.Id!.Value).ToHashSet();

                var toRemove = customer.Addresses.Where(a => !incomingIds.Contains(a.Id)).ToList();
                foreach (var address in toRemove)
                    customer.Addresses.Remove(address);

                foreach (var addressDto in dto.Addresses)
                {
                    if (addressDto.Id is > 0)
                    {
                        var existingAddress = customer.Addresses.FirstOrDefault(a => a.Id == addressDto.Id);
                        if (existingAddress is null)
                            return BadRequestResponse($"Address {addressDto.Id} does not belong to customer {id}.");

                        existingAddress.Label = addressDto.Label;
                        existingAddress.AddressLine = addressDto.AddressLine;
                        existingAddress.City = addressDto.City;
                        existingAddress.IsDefault = addressDto.IsDefault;
                    }
                    else
                    {
                        customer.Addresses.Add(new CustomerAddress
                        {
                            Label = addressDto.Label,
                            AddressLine = addressDto.AddressLine,
                            City = addressDto.City,
                            IsDefault = addressDto.IsDefault
                        });
                    }
                }

                NormalizeDefaultAddress(customer.Addresses);
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/customers/5 — blocked once the customer has sales history;
        // addresses cascade-delete with the customer otherwise.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer is null)
                return NotFoundResponse($"Customer {id} not found.");

            if (await _db.Sales.AnyAsync(s => s.CustomerId == id))
                return ConflictResponse("Cannot delete a customer with existing sales.");

            _db.Customers.Remove(customer);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/customers/5/photo — upload/replace customer photo as an
        // actual file (multipart/form-data). In Postman: Body -> form-data ->
        // key "file", type "File" -> pick an image from disk.
        [HttpPost("{id:int}/photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
        {
            var customer = await _db.Customers.FindAsync(id);
            if (customer is null) return NotFoundResponse($"Customer {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            bool isAllowed = false;
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            if (!isAllowed)
                return BadRequestResponse("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp.");

            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            string folderPath = GetCustomerImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteCustomerPhotoFile(customer.PhotoPath);

            customer.Photo = null; // stop keeping raw bytes in DB now that we have a file on disk
            customer.PhotoContentType = file.ContentType;
            customer.PhotoPath = "/" + CustomerImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { customer.CustomerId, customer.PhotoPath });
        }

        // DELETE api/customers/5/photo — remove the customer's photo, both
        // the physical file and the DB fields.
        [HttpDelete("{id:int}/photo")]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            var customer = await _db.Customers.FindAsync(id);
            if (customer is null) return NotFoundResponse($"Customer {id} not found.");

            DeleteCustomerPhotoFile(customer.PhotoPath);

            customer.PhotoPath = null;
            customer.Photo = null;
            customer.PhotoContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/customers/5/addresses
        [HttpPost("{customerId:int}/addresses")]
        public async Task<ActionResult<CustomerAddressReadDto>> AddAddress(int customerId, [FromBody] CustomerAddressWriteDto dto)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (customer is null)
                return NotFoundResponse($"Customer {customerId} not found.");

            var address = new CustomerAddress
            {
                CustomerId = customerId,
                Label = dto.Label,
                AddressLine = dto.AddressLine,
                City = dto.City,
                IsDefault = dto.IsDefault
            };

            _db.CustomerAddresses.Add(address);
            await _db.SaveChangesAsync();

            if (address.IsDefault)
            {
                await ClearOtherDefaultAddressesAsync(customerId, address.Id);
                await _db.SaveChangesAsync();
            }

            return CreatedAtAction(nameof(GetById), new { id = customerId }, MapAddressToReadDto(address));
        }

        // PUT api/customers/5/addresses/9
        [HttpPut("{customerId:int}/addresses/{addressId:int}")]
        public async Task<IActionResult> UpdateAddress(int customerId, int addressId, [FromBody] CustomerAddressWriteDto dto)
        {
            var address = await _db.CustomerAddresses
                .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);

            if (address is null)
                return NotFoundResponse($"Address {addressId} not found for customer {customerId}.");

            address.Label = dto.Label;
            address.AddressLine = dto.AddressLine;
            address.City = dto.City;
            address.IsDefault = dto.IsDefault;

            await _db.SaveChangesAsync();
            if (address.IsDefault)
            {
                await ClearOtherDefaultAddressesAsync(customerId, address.Id);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }

        // DELETE api/customers/5/addresses/9
        [HttpDelete("{customerId:int}/addresses/{addressId:int}")]
        public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
        {
            var address = await _db.CustomerAddresses
                .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);

            if (address is null)
                return NotFoundResponse($"Address {addressId} not found for customer {customerId}.");

            _db.CustomerAddresses.Remove(address);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- helpers ----------

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));

        private static void NormalizeDefaultAddress(ICollection<CustomerAddress> addresses)
        {
            var defaultAddress = addresses.LastOrDefault(a => a.IsDefault) ?? addresses.FirstOrDefault();
            foreach (var address in addresses)
                address.IsDefault = ReferenceEquals(address, defaultAddress);
        }

        private async Task ClearOtherDefaultAddressesAsync(int customerId, int keepAddressId)
        {
            var others = await _db.CustomerAddresses
                .Where(a => a.CustomerId == customerId && a.Id != keepAddressId && a.IsDefault)
                .ToListAsync();
            foreach (var other in others)
                other.IsDefault = false;
        }

        // Deletes the physical file behind a PhotoPath like
        // "/images/customers/5_a1b2...c1.jpg", if it exists. Safe to call
        // with null/empty/unrelated paths — it just does nothing then.
        private void DeleteCustomerPhotoFile(string? photoPath)
        {
            if (string.IsNullOrWhiteSpace(photoPath))
                return;

            if (photoPath.IndexOf(CustomerImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(photoPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetCustomerImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private static CustomerReadDto MapToReadDto(Customer customer) => new()
        {
            CustomerId = customer.CustomerId,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Phone = customer.Phone,
            CreditLimit = customer.CreditLimit,
            DateOfBirth = customer.DateOfBirth,
            IsActive = customer.IsActive,
            Photo = customer.Photo,
            PhotoContentType = customer.PhotoContentType,
            PhotoPath = customer.PhotoPath,
            CustomerTypeId = customer.CustomerTypeId,
            CustomerTypeName = customer.CustomerType?.Name ?? string.Empty,
            Addresses = customer.Addresses.Select(MapAddressToReadDto).ToList()
        };

        private static CustomerAddressReadDto MapAddressToReadDto(CustomerAddress address) => new()
        {
            Id = address.Id,
            Label = address.Label,
            AddressLine = address.AddressLine,
            City = address.City,
            IsDefault = address.IsDefault
        };
    }
}
```

### `Customertypescontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    // CRUD for the CustomerType lookup that Customer.CustomerTypeId feeds
    // from. Previously seed-only (Retail/Wholesale/Corporate).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CustomerTypesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public CustomerTypesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/customertypes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerTypeReadDto>>> GetAll()
        {
            var types = await _db.CustomerTypes
                .AsNoTracking()
                .Select(t => new CustomerTypeReadDto { Id = t.Id, Name = t.Name, CustomerCount = t.Customers.Count })
                .ToListAsync();

            return Ok(types);
        }

        // GET api/customertypes/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CustomerTypeReadDto>> GetById(int id)
        {
            var type = await _db.CustomerTypes
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new CustomerTypeReadDto { Id = t.Id, Name = t.Name, CustomerCount = t.Customers.Count })
                .FirstOrDefaultAsync();

            if (type is null)
                return NotFoundResponse($"Customer type {id} not found.");

            return Ok(type);
        }

        // POST api/customertypes — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<CustomerTypeReadDto>> Create([FromBody] CustomerTypeWriteDto dto)
        {
            if (await _db.CustomerTypes.AnyAsync(t => t.Name == dto.Name))
                return ConflictResponse($"Customer type '{dto.Name}' already exists.");

            var type = new CustomerType { Name = dto.Name };

            _db.CustomerTypes.Add(type);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = type.Id },
                new CustomerTypeReadDto { Id = type.Id, Name = type.Name, CustomerCount = 0 });
        }

        // PUT api/customertypes/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CustomerTypeWriteDto dto)
        {
            var type = await _db.CustomerTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Customer type {id} not found.");

            if (await _db.CustomerTypes.AnyAsync(t => t.Name == dto.Name && t.Id != id))
                return ConflictResponse($"Customer type '{dto.Name}' already exists.");

            type.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/customertypes/5 — blocked while any customer still references it.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await _db.CustomerTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Customer type {id} not found.");

            if (await _db.Customers.AnyAsync(c => c.CustomerTypeId == id))
                return ConflictResponse("Cannot delete a customer type with customers assigned to it.");

            _db.CustomerTypes.Remove(type);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `Dailypurchaserequirementscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // API for DailyPurchaseRequirementTable — the staging list of products
    // that need reordering, which PurchaseOrderSource.Auto ("created from
    // Auto-Requirement -> DailyPurchaseRequirementTable", per the enum's own
    // comment) is meant to read from. Had a model + DbSet + fluent config but
    // no controller, and nothing populated it.
    //
    // Wiring "convert these rows into an actual PurchaseOrder" is left to
    // PurchaseOrdersController (out of scope here) — this controller covers
    // maintaining the requirement list itself: reading it, editing rows by
    // hand, and regenerating it from current stock vs Product.MinStockQty.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DailyPurchaseRequirementsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public DailyPurchaseRequirementsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/dailypurchaserequirements
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PurchaseRequirementReadDto>>> GetAll()
        {
            var rows = await _db.DailyPurchaseRequirements
                .AsNoTracking()
                .Include(d => d.Product)
                .Include(d => d.Unit)
                .OrderByDescending(d => d.RequiredQty)
                .Select(d => MapToReadDto(d))
                .ToListAsync();

            return Ok(rows);
        }

        // GET api/dailypurchaserequirements/{serialNo}
        [HttpGet("{serialNo:guid}")]
        public async Task<ActionResult<PurchaseRequirementReadDto>> GetById(Guid serialNo)
        {
            var row = await _db.DailyPurchaseRequirements
                .AsNoTracking()
                .Include(d => d.Product)
                .Include(d => d.Unit)
                .FirstOrDefaultAsync(d => d.SerialNo == serialNo);

            if (row is null)
                return NotFoundResponse($"Requirement row {serialNo} not found.");

            return Ok(MapToReadDto(row));
        }

        // POST api/dailypurchaserequirements — add one row by hand.
        [HttpPost]
        public async Task<ActionResult<PurchaseRequirementReadDto>> Create([FromBody] PurchaseRequirementWriteDto dto)
        {
            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequestResponse($"Product {dto.ProductId} does not exist.");
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequestResponse($"Unit {dto.UnitId} does not exist.");

            var row = new DailyPurchaseRequirementTable
            {
                SerialNo = Guid.NewGuid(),
                ProductId = dto.ProductId,
                UnitId = dto.UnitId,
                LastPurchaseRate = dto.LastPurchaseRate,
                StockQty = dto.StockQty,
                RequiredQty = dto.RequiredQty,
                OrdersQty = dto.OrdersQty
            };

            _db.DailyPurchaseRequirements.Add(row);
            await _db.SaveChangesAsync();

            await _db.Entry(row).Reference(d => d.Product).LoadAsync();
            await _db.Entry(row).Reference(d => d.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { serialNo = row.SerialNo }, MapToReadDto(row));
        }

        // PUT api/dailypurchaserequirements/{serialNo} — e.g. bump RequiredQty
        // or set OrdersQty once a purchase order covering this row is placed.
        [HttpPut("{serialNo:guid}")]
        public async Task<IActionResult> Update(Guid serialNo, [FromBody] PurchaseRequirementWriteDto dto)
        {
            var row = await _db.DailyPurchaseRequirements.FirstOrDefaultAsync(d => d.SerialNo == serialNo);
            if (row is null)
                return NotFoundResponse($"Requirement row {serialNo} not found.");

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequestResponse($"Product {dto.ProductId} does not exist.");
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequestResponse($"Unit {dto.UnitId} does not exist.");

            row.ProductId = dto.ProductId;
            row.UnitId = dto.UnitId;
            row.LastPurchaseRate = dto.LastPurchaseRate;
            row.StockQty = dto.StockQty;
            row.RequiredQty = dto.RequiredQty;
            row.OrdersQty = dto.OrdersQty;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/dailypurchaserequirements/{serialNo} — e.g. once it's
        // been converted into a purchase order, or is no longer needed.
        [HttpDelete("{serialNo:guid}")]
        public async Task<IActionResult> Delete(Guid serialNo)
        {
            var row = await _db.DailyPurchaseRequirements.FirstOrDefaultAsync(d => d.SerialNo == serialNo);
            if (row is null)
                return NotFoundResponse($"Requirement row {serialNo} not found.");

            _db.DailyPurchaseRequirements.Remove(row);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/dailypurchaserequirements/generate — rebuilds the list from
        // current stock: for every product whose total AvailableQuantity
        // (summed across its ProductStock batches) has fallen below
        // MinStockQty, (re)writes one row with RequiredQty = MaxStockQty -
        // current stock, so the target is a full restock rather than just
        // scraping back over the minimum. Rows not yet acted on (OrdersQty ==
        // 0) for products that no longer qualify are cleared out first.
        [HttpPost("generate")]
        public async Task<ActionResult<IEnumerable<PurchaseRequirementReadDto>>> Generate()
        {
            var stockByProduct = await _db.ProductStocks
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, TotalAvailable = g.Sum(s => s.AvailableQuantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.TotalAvailable);

            var products = await _db.Products
                .Where(p => p.IsActive != false)
                .ToListAsync();

            var staleRows = await _db.DailyPurchaseRequirements
                .Where(d => d.OrdersQty == 0)
                .ToListAsync();
            _db.DailyPurchaseRequirements.RemoveRange(staleRows);

            var generated = new List<DailyPurchaseRequirementTable>();

            foreach (var product in products)
            {
                decimal currentStock = stockByProduct.TryGetValue(product.Id, out var qty) ? qty : 0;

                if (currentStock >= product.MinStockQty)
                    continue;

                decimal requiredQty = product.MaxStockQty - currentStock;
                if (requiredQty <= 0)
                    continue;

                generated.Add(new DailyPurchaseRequirementTable
                {
                    SerialNo = Guid.NewGuid(),
                    ProductId = product.Id,
                    UnitId = product.UnitId,
                    LastPurchaseRate = product.PurchasePrice,
                    StockQty = currentStock,
                    RequiredQty = requiredQty,
                    OrdersQty = 0
                });
            }

            _db.DailyPurchaseRequirements.AddRange(generated);
            await _db.SaveChangesAsync();

            foreach (var row in generated)
            {
                await _db.Entry(row).Reference(d => d.Product).LoadAsync();
                await _db.Entry(row).Reference(d => d.Unit).LoadAsync();
            }

            return Ok(generated.Select(MapToReadDto));
        }

        private static PurchaseRequirementReadDto MapToReadDto(DailyPurchaseRequirementTable d) => new()
        {
            SerialNo = d.SerialNo,
            ProductId = d.ProductId,
            ProductName = d.Product?.ProductName ?? string.Empty,
            UnitId = d.UnitId,
            UnitName = d.Unit?.Name ?? string.Empty,
            LastPurchaseRate = d.LastPurchaseRate,
            StockQty = d.StockQty,
            RequiredQty = d.RequiredQty,
            OrdersQty = d.OrdersQty
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
    }
}
```

### `Departmentscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.HR;

namespace PharmacyV2.Controllers
{
    // CRUD for the Department lookup table that Employee.DepartmentId feeds
    // from. Previously seed-only (see PharmacyDbContext) with no way to
    // add/rename/remove departments except editing the seed data by hand.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DepartmentsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public DepartmentsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/departments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DepartmentReadDto>>> GetAll()
        {
            var departments = await _db.Departments
                .AsNoTracking()
                .Select(d => new DepartmentReadDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    EmployeeCount = d.Employees.Count
                })
                .ToListAsync();

            return Ok(departments);
        }

        // GET api/departments/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DepartmentReadDto>> GetById(int id)
        {
            var department = await _db.Departments
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new DepartmentReadDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    EmployeeCount = d.Employees.Count
                })
                .FirstOrDefaultAsync();

            if (department is null)
                return NotFoundResponse($"Department {id} not found.");

            return Ok(department);
        }

        // POST api/departments — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<DepartmentReadDto>> Create([FromBody] DepartmentWriteDto dto)
        {
            if (await _db.Departments.AnyAsync(d => d.Name == dto.Name))
                return ConflictResponse($"Department '{dto.Name}' already exists.");

            var department = new Department { Name = dto.Name };

            _db.Departments.Add(department);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = department.Id },
                new DepartmentReadDto { Id = department.Id, Name = department.Name, EmployeeCount = 0 });
        }

        // PUT api/departments/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DepartmentWriteDto dto)
        {
            var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
            if (department is null)
                return NotFoundResponse($"Department {id} not found.");

            if (await _db.Departments.AnyAsync(d => d.Name == dto.Name && d.Id != id))
                return ConflictResponse($"Department '{dto.Name}' already exists.");

            department.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/departments/5 — blocked while any employee still
        // references it (FK is Restrict, so this mirrors the DB behavior
        // with a clearer message).
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
            if (department is null)
                return NotFoundResponse($"Department {id} not found.");

            if (await _db.Employees.AnyAsync(e => e.DepartmentId == id))
                return ConflictResponse("Cannot delete a department with employees assigned to it.");

            _db.Departments.Remove(department);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `DoctorsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DoctorsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        public DoctorsController(PharmacyDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DoctorReadDto>>> GetAll([FromQuery] string? search)
        {
            var query = _db.Doctors.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.Name.Contains(search) || (d.Hospital != null && d.Hospital.Contains(search)));

            var doctors = await query.OrderBy(d => d.Name).ToListAsync();
            var counts = await _db.Prescriptions.AsNoTracking()
                .GroupBy(p => p.DoctorId)
                .Select(g => new { DoctorId = g.Key, Count = g.Count() })
                .ToListAsync();
            var countMap = counts.ToDictionary(x => x.DoctorId, x => x.Count);

            return Ok(doctors.Select(d => ToDto(d, countMap.GetValueOrDefault(d.DoctorId))));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<DoctorReadDto>> GetById(int id)
        {
            var doctor = await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor is null) return NotFound(new ApiError($"Doctor {id} not found."));
            var count = await _db.Prescriptions.CountAsync(p => p.DoctorId == id);
            return Ok(ToDto(doctor, count));
        }

        [HttpGet("{id:int}/prescriptions")]
        public async Task<ActionResult<IEnumerable<PrescriptionReadDto>>> GetPrescriptions(int id)
        {
            if (!await _db.Doctors.AnyAsync(d => d.DoctorId == id))
                return NotFound(new ApiError($"Doctor {id} not found."));

            var prescriptions = await _db.Prescriptions.AsNoTracking()
                .Include(p => p.Doctor).Include(p => p.Customer).Include(p => p.Items).ThenInclude(i => i.Product)
                .Where(p => p.DoctorId == id)
                .OrderByDescending(p => p.PrescriptionDate)
                .ToListAsync();

            return Ok(prescriptions.Select(PrescriptionsController.ToPrescriptionDto));
        }

        [HttpPost]
        public async Task<ActionResult<DoctorReadDto>> Create([FromBody] DoctorWriteDto dto)
        {
            var doctor = new Doctor
            {
                Name = dto.Name,
                Specialization = dto.Specialization,
                RegistrationNo = dto.RegistrationNo,
                Hospital = dto.Hospital,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address,
                IsActive = dto.IsActive
            };
            _db.Doctors.Add(doctor);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = doctor.DoctorId }, ToDto(doctor, 0));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DoctorWriteDto dto)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor is null) return NotFound(new ApiError($"Doctor {id} not found."));

            doctor.Name = dto.Name;
            doctor.Specialization = dto.Specialization;
            doctor.RegistrationNo = dto.RegistrationNo;
            doctor.Hospital = dto.Hospital;
            doctor.Phone = dto.Phone;
            doctor.Email = dto.Email;
            doctor.Address = dto.Address;
            doctor.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor is null) return NotFound(new ApiError($"Doctor {id} not found."));

            if (await _db.Prescriptions.AnyAsync(p => p.DoctorId == id))
                return Conflict(new ApiError("Cannot delete this doctor because prescriptions exist against them. Deactivate instead."));

            _db.Doctors.Remove(doctor);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static DoctorReadDto ToDto(Doctor d, int prescriptionCount) => new()
        {
            DoctorId = d.DoctorId,
            Name = d.Name,
            Specialization = d.Specialization,
            RegistrationNo = d.RegistrationNo,
            Hospital = d.Hospital,
            Phone = d.Phone,
            Email = d.Email,
            Address = d.Address,
            IsActive = d.IsActive,
            PrescriptionCount = prescriptionCount
        };
    }
}
```

### `DosageFormsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // CRUD for the DosageForm lookup table — feeds the "Dosage Form" filter
    // dropdown on the Brand Search page and the DosageFormId FK on Product.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DosageFormsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public DosageFormsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/dosageforms
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DosageFormReadDto>>> GetAll()
        {
            var forms = await _db.DosageForms.AsNoTracking()
                .OrderBy(d => d.Name)
                .Select(d => new DosageFormReadDto { Id = d.Id, Name = d.Name, ProductCount = d.Products.Count })
                .ToListAsync();

            return Ok(forms);
        }

        // GET api/dosageforms/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DosageFormReadDto>> GetById(int id)
        {
            var form = await _db.DosageForms.AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new DosageFormReadDto { Id = d.Id, Name = d.Name, ProductCount = d.Products.Count })
                .FirstOrDefaultAsync();

            if (form is null) return NotFoundResponse($"Dosage form {id} not found.");
            return Ok(form);
        }

        // POST api/dosageforms
        [HttpPost]
        public async Task<ActionResult<DosageFormReadDto>> Create([FromBody] DosageFormWriteDto dto)
        {
            if (await _db.DosageForms.AnyAsync(d => d.Name == dto.Name))
                return ConflictResponse($"Dosage form '{dto.Name}' already exists.");

            var form = new DosageForm { Name = dto.Name };
            _db.DosageForms.Add(form);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = form.Id },
                new DosageFormReadDto { Id = form.Id, Name = form.Name, ProductCount = 0 });
        }

        // PUT api/dosageforms/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DosageFormWriteDto dto)
        {
            var form = await _db.DosageForms.FirstOrDefaultAsync(d => d.Id == id);
            if (form is null) return NotFoundResponse($"Dosage form {id} not found.");

            if (await _db.DosageForms.AnyAsync(d => d.Name == dto.Name && d.Id != id))
                return ConflictResponse($"Dosage form '{dto.Name}' already exists.");

            form.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/dosageforms/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var form = await _db.DosageForms.FirstOrDefaultAsync(d => d.Id == id);
            if (form is null) return NotFoundResponse($"Dosage form {id} not found.");

            if (await _db.Products.AnyAsync(p => p.DosageFormId == id))
                return ConflictResponse("Cannot delete a dosage form that products are using.");

            _db.DosageForms.Remove(form);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `EmployeesController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.HR;

namespace PharmacyV2.Controllers
{
    // Master (Employee) + details (EmployeeDocument) CRUD.
    // DepartmentId is the relational/dropdown field — Department itself has
    // no controller, it's seeded lookup data (see PharmacyDbContext).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;

        // Folder (under wwwroot) where employee photos physically live, and
        // the matching public URL prefix used to build PhotoPath. Same
        // pattern as ProductsController.ProductImagesRelativeFolder.
        private const string EmployeeImagesRelativeFolder = "images/employees";

        public EmployeesController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // wwwroot may not exist yet on a fresh clone. Creates
        // "wwwroot/images/employees" on demand the first time it's needed.
        private string GetEmployeeImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "employees");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/employees?search=jane&page=1&pageSize=20
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeListItemDto>>> GetAll(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

            var query = _db.Employees.AsNoTracking().Include(e => e.Department).Include(e => e.Documents).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(e =>
                    e.FullName.Contains(search) ||
                    (e.Email != null && e.Email.Contains(search)));
            }

            var employees = await query
                .OrderBy(e => e.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new EmployeeListItemDto
                {
                    Id = e.Id,
                    FullName = e.FullName,
                    Email = e.Email,
                    Salary = e.Salary,
                    HireDate = e.HireDate,
                    IsActive = e.IsActive,
                    DepartmentId = e.DepartmentId,
                    DepartmentName = e.Department.Name,
                    HasPhoto = e.Photo != null || e.PhotoPath != null,
                    PhotoPath = e.PhotoPath,
                    Documents = e.Documents.Select(d => new EmployeeDocumentReadDto
                    {
                        Id = d.Id,
                        DocumentTitle = d.DocumentTitle,
                        DocumentNumber = d.DocumentNumber,
                        IssueDate = d.IssueDate,
                        ExpiryDate = d.ExpiryDate,
                        IsVerified = d.IsVerified
                    }).ToList()
                })
                .ToListAsync();

            return Ok(employees);
        }

        // GET api/employees/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<EmployeeReadDto>> GetById(int id)
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.Department)
                .Include(e => e.Documents)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee is null)
                return NotFoundResponse($"Employee {id} not found.");

            return Ok(MapToReadDto(employee));
        }

        // POST api/employees — create the master, optionally with nested documents.
        [HttpPost]
        public async Task<ActionResult<EmployeeReadDto>> Create([FromBody] EmployeeCreateDto dto)
        {
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == dto.DepartmentId);
            if (!departmentExists)
                return BadRequestResponse($"Department {dto.DepartmentId} does not exist.");

            var employee = new Employee
            {
                FullName = dto.FullName,
                Email = dto.Email,
                Salary = dto.Salary,
                HireDate = dto.HireDate,
                IsActive = dto.IsActive,
                Photo = dto.Photo,
                PhotoContentType = dto.PhotoContentType,
                PhotoPath = dto.PhotoPath,
                DepartmentId = dto.DepartmentId
            };

            if (dto.Documents is { Count: > 0 })
            {
                foreach (var docDto in dto.Documents)
                {
                    employee.Documents.Add(new EmployeeDocument
                    {
                        DocumentTitle = docDto.DocumentTitle,
                        DocumentNumber = docDto.DocumentNumber,
                        IssueDate = docDto.IssueDate,
                        ExpiryDate = docDto.ExpiryDate,
                        IsVerified = docDto.IsVerified
                    });
                }
            }

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync();

            await _db.Entry(employee).Reference(e => e.Department).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = employee.Id }, MapToReadDto(employee));
        }

        // PUT api/employees/5 — full update of master fields, with a full
        // add/update/delete sync of the Documents collection.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] EmployeeUpdateDto dto)
        {
            var employee = await _db.Employees
                .Include(e => e.Documents)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee is null)
                return NotFoundResponse($"Employee {id} not found.");

            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == dto.DepartmentId);
            if (!departmentExists)
                return BadRequestResponse($"Department {dto.DepartmentId} does not exist.");

            employee.FullName = dto.FullName;
            employee.Email = dto.Email;
            employee.Salary = dto.Salary;
            employee.HireDate = dto.HireDate;
            employee.IsActive = dto.IsActive;
            if (dto.Photo is not null) employee.Photo = dto.Photo;
            if (dto.PhotoContentType is not null) employee.PhotoContentType = dto.PhotoContentType;
            if (dto.PhotoPath is not null) employee.PhotoPath = dto.PhotoPath;
            employee.DepartmentId = dto.DepartmentId;

            if (dto.Documents is not null)
            {
                var incomingIds = dto.Documents.Where(d => d.Id is > 0).Select(d => d.Id!.Value).ToHashSet();

                // delete documents that were left out of the payload
                var toRemove = employee.Documents.Where(d => !incomingIds.Contains(d.Id)).ToList();
                foreach (var doc in toRemove)
                    employee.Documents.Remove(doc);

                foreach (var docDto in dto.Documents)
                {
                    if (docDto.Id is > 0)
                    {
                        var existingDoc = employee.Documents.FirstOrDefault(d => d.Id == docDto.Id);
                        if (existingDoc is null)
                            return BadRequestResponse($"Document {docDto.Id} does not belong to employee {id}.");

                        existingDoc.DocumentTitle = docDto.DocumentTitle;
                        existingDoc.DocumentNumber = docDto.DocumentNumber;
                        existingDoc.IssueDate = docDto.IssueDate;
                        existingDoc.ExpiryDate = docDto.ExpiryDate;
                        existingDoc.IsVerified = docDto.IsVerified;
                    }
                    else
                    {
                        employee.Documents.Add(new EmployeeDocument
                        {
                            DocumentTitle = docDto.DocumentTitle,
                            DocumentNumber = docDto.DocumentNumber,
                            IssueDate = docDto.IssueDate,
                            ExpiryDate = docDto.ExpiryDate,
                            IsVerified = docDto.IsVerified
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/employees/5 — documents cascade-delete with the employee.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (employee is null)
                return NotFoundResponse($"Employee {id} not found.");

            _db.Employees.Remove(employee);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/employees/5/photo — upload/replace the employee's photo as
        // an actual file (multipart/form-data), instead of hand-encoding
        // base64 into the JSON body. In Postman: Body -> form-data -> key
        // "file", type "File" -> pick an image from disk.
        //
        // Saves the file physically under wwwroot/images/employees and
        // stores the short public path in Employee.PhotoPath, e.g.
        // "/images/employees/5_a1b2c3d4.jpg", so it can be loaded straight
        // from <img src="https://your-host/images/employees/5_a1b2c3d4.jpg">
        // once app.UseStaticFiles() is serving wwwroot.
        [HttpPost("{id:int}/photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
        {
            var employee = await _db.Employees.FindAsync(id);
            if (employee is null) return NotFoundResponse($"Employee {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            bool isAllowed = false;
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            if (!isAllowed)
                return BadRequestResponse("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp.");

            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            string folderPath = GetEmployeeImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteEmployeePhotoFile(employee.PhotoPath);

            employee.Photo = null; // stop keeping raw bytes in the DB now that we have a file on disk
            employee.PhotoContentType = file.ContentType;
            employee.PhotoPath = "/" + EmployeeImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { employee.Id, employee.PhotoPath });
        }

        // DELETE api/employees/5/photo — remove the employee's photo, both the
        // physical file under wwwroot/images/employees and the DB fields.
        [HttpDelete("{id:int}/photo")]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            var employee = await _db.Employees.FindAsync(id);
            if (employee is null) return NotFoundResponse($"Employee {id} not found.");

            DeleteEmployeePhotoFile(employee.PhotoPath);

            employee.PhotoPath = null;
            employee.Photo = null;
            employee.PhotoContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------
        // Handy in Postman when you only want to add/change/remove one
        // document without resending the whole employee payload.

        // POST api/employees/5/documents
        [HttpPost("{employeeId:int}/documents")]
        public async Task<ActionResult<EmployeeDocumentReadDto>> AddDocument(int employeeId, [FromBody] EmployeeDocumentWriteDto dto)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
            if (employee is null)
                return NotFoundResponse($"Employee {employeeId} not found.");

            var document = new EmployeeDocument
            {
                EmployeeId = employeeId,
                DocumentTitle = dto.DocumentTitle,
                DocumentNumber = dto.DocumentNumber,
                IssueDate = dto.IssueDate,
                ExpiryDate = dto.ExpiryDate,
                IsVerified = dto.IsVerified
            };

            _db.EmployeeDocuments.Add(document);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = employeeId }, MapDocumentToReadDto(document));
        }

        // PUT api/employees/5/documents/9
        [HttpPut("{employeeId:int}/documents/{documentId:int}")]
        public async Task<IActionResult> UpdateDocument(int employeeId, int documentId, [FromBody] EmployeeDocumentWriteDto dto)
        {
            var document = await _db.EmployeeDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId);

            if (document is null)
                return NotFoundResponse($"Document {documentId} not found for employee {employeeId}.");

            document.DocumentTitle = dto.DocumentTitle;
            document.DocumentNumber = dto.DocumentNumber;
            document.IssueDate = dto.IssueDate;
            document.ExpiryDate = dto.ExpiryDate;
            document.IsVerified = dto.IsVerified;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/employees/5/documents/9
        [HttpDelete("{employeeId:int}/documents/{documentId:int}")]
        public async Task<IActionResult> DeleteDocument(int employeeId, int documentId)
        {
            var document = await _db.EmployeeDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId);

            if (document is null)
                return NotFoundResponse($"Document {documentId} not found for employee {employeeId}.");

            _db.EmployeeDocuments.Remove(document);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- helpers ----------

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));

        // Deletes the physical file behind a PhotoPath like
        // "/images/employees/5_a1b2...c1.jpg", if it exists. Safe to call
        // with null/empty/unrelated paths — it just does nothing then.
        private void DeleteEmployeePhotoFile(string? photoPath)
        {
            if (string.IsNullOrWhiteSpace(photoPath))
                return;

            if (photoPath.IndexOf(EmployeeImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(photoPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetEmployeeImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private static EmployeeReadDto MapToReadDto(Employee employee) => new()
        {
            Id = employee.Id,
            FullName = employee.FullName,
            Email = employee.Email,
            Salary = employee.Salary,
            HireDate = employee.HireDate,
            IsActive = employee.IsActive,
            PhotoPath = employee.PhotoPath,
            PhotoContentType = employee.PhotoContentType,
            DepartmentId = employee.DepartmentId,
            DepartmentName = employee.Department?.Name ?? string.Empty,
            Documents = employee.Documents.Select(MapDocumentToReadDto).ToList()
        };

        private static EmployeeDocumentReadDto MapDocumentToReadDto(EmployeeDocument document) => new()
        {
            Id = document.Id,
            DocumentTitle = document.DocumentTitle,
            DocumentNumber = document.DocumentNumber,
            IssueDate = document.IssueDate,
            ExpiryDate = document.ExpiryDate,
            IsVerified = document.IsVerified
        };
    }
}
```

### `Expiredproductstockscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;
using System.Security.Claims;
using PharmacyV2.Services;
using PharmacyV2.Enums;

namespace PharmacyV2.Controllers
{
    // API for ExpiredProductStock — the write-off/disposal record made
    // against an expired ProductStock batch. Had a model + DbSet + fluent
    // config (4 Restrict FKs) but no controller.
    //
    // No PUT here: like SupplierPayment/LedgerAccount elsewhere, this is an
    // approval record, not an editable row. Delete is allowed only as an
    // "undo a mis-recorded disposal" action, and puts the quantity back.
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class ExpiredProductStocksController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;

        public ExpiredProductStocksController(PharmacyDbContext db, IAccountingPostingService accounting)
        {
            _db = db;
            _accounting = accounting;
        }

        // GET api/expiredproductstocks — optionally ?warehouseId= / ?productId=
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ExpiredProductStockReadDto>>> GetAll(
            [FromQuery] int? warehouseId, [FromQuery] int? productId)
        {
            var query = _db.ExpiredProductStocks
                .AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.ProductStock)
                .Include(e => e.Warehouse)
                .Include(e => e.ApprovedBy)
                .AsQueryable();

            if (warehouseId is not null)
                query = query.Where(e => e.WarehouseId == warehouseId);
            if (productId is not null)
                query = query.Where(e => e.ProductId == productId);

            var records = await query
                .OrderByDescending(e => e.DisposalDate)
                .Select(e => MapToReadDto(e))
                .ToListAsync();

            return Ok(records);
        }

        // GET api/expiredproductstocks/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ExpiredProductStockReadDto>> GetById(int id)
        {
            var record = await _db.ExpiredProductStocks
                .AsNoTracking()
                .Include(e => e.Product)
                .Include(e => e.ProductStock)
                .Include(e => e.Warehouse)
                .Include(e => e.ApprovedBy)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (record is null)
                return NotFoundResponse($"Expired stock record {id} not found.");

            return Ok(MapToReadDto(record));
        }

        // POST api/expiredproductstocks — records a disposal and deducts the
        // quantity from the batch's AvailableQuantity. ProductId/WarehouseId
        // are derived from the ProductStock batch itself (not client-supplied)
        // so they can never disagree with it.
        [HttpPost]
        public async Task<ActionResult<ExpiredProductStockReadDto>> Create([FromBody] ExpiredProductStockCreateDto dto)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var approvedByUserId))
                return Unauthorized();
            var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == dto.ProductStockId);
            if (stock is null)
                return BadRequestResponse($"Product stock batch {dto.ProductStockId} does not exist.");

            if (!string.Equals(dto.DisposalMethod, "Damaged", StringComparison.OrdinalIgnoreCase) && stock.ExpiryDate > DateOnly.FromDateTime(DateTime.Today))
                return BadRequestResponse($"Batch '{stock.BatchNumber}' has not expired yet (expires {stock.ExpiryDate:yyyy-MM-dd}).");

            if (dto.Quantity > stock.AvailableQuantity)
                return BadRequestResponse($"Disposal quantity ({dto.Quantity}) exceeds available quantity in this batch ({stock.AvailableQuantity}).");

            var record = new ExpiredProductStock
            {
                ProductId = stock.ProductId,
                ProductStockId = stock.Id,
                WarehouseId = stock.WarehouseId,
                Quantity = dto.Quantity,
                UnitCost = stock.UnitCost,
                TotalCost = dto.Quantity * stock.UnitCost,
                DisposalMethod = dto.DisposalMethod,
                DisposalDate = DateTime.Now,
                ApprovedByUserId = approvedByUserId,
                Note = dto.Note
            };

            stock.AvailableQuantity -= dto.Quantity;

            _db.ExpiredProductStocks.Add(record);
            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.Adjustment, record.Id, record.DisposalDate, $"{dto.DisposalMethod}: {stock.BatchNumber}",
                ("5100", record.TotalCost, 0), ("1200", 0, record.TotalCost));
            await _db.SaveChangesAsync();

            await _db.Entry(record).Reference(e => e.Product).LoadAsync();
            await _db.Entry(record).Reference(e => e.ProductStock).LoadAsync();
            await _db.Entry(record).Reference(e => e.Warehouse).LoadAsync();
            await _db.Entry(record).Reference(e => e.ApprovedBy).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = record.Id }, MapToReadDto(record));
        }

        // DELETE api/expiredproductstocks/5 — undoes the disposal, restoring
        // the quantity back onto the batch.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var record = await _db.ExpiredProductStocks.FirstOrDefaultAsync(e => e.Id == id);
            if (record is null)
                return NotFoundResponse($"Expired stock record {id} not found.");

            var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == record.ProductStockId);
            if (stock is not null)
                stock.AvailableQuantity += record.Quantity;

            _db.ExpiredProductStocks.Remove(record);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static ExpiredProductStockReadDto MapToReadDto(ExpiredProductStock e) => new()
        {
            Id = e.Id,
            ProductId = e.ProductId,
            ProductName = e.Product?.ProductName ?? string.Empty,
            ProductStockId = e.ProductStockId,
            BatchNumber = e.ProductStock?.BatchNumber ?? string.Empty,
            ExpiryDate = e.ProductStock?.ExpiryDate ?? default,
            WarehouseId = e.WarehouseId,
            WarehouseName = e.Warehouse?.Name ?? string.Empty,
            Quantity = e.Quantity,
            UnitCost = e.UnitCost,
            TotalCost = e.TotalCost,
            DisposalMethod = e.DisposalMethod,
            DisposalDate = e.DisposalDate,
            ApprovedByUserId = e.ApprovedByUserId,
            ApprovedByName = e.ApprovedBy?.FullName ?? string.Empty,
            Note = e.Note
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
    }
}
```

### `Ledgeraccountscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Enums;
using PharmacyV2.Models.Accounts;
using PharmacyV2.Services;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // API for LedgerAccount, the General Ledger. Had a model + DbSet + fluent
    // config (including the CK_LedgerAccount_DebitXorCredit check constraint)
    // but no controller.
    //
    // Deliberately NOT a plain CRUD controller: a posted ledger line is an
    // accounting record, not an editable row, so there is no PUT and no hard
    // DELETE here — only Create (as a balanced multi-line journal entry) and
    // Reverse (which posts an offsetting entry rather than mutating history).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LedgerAccountsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;

        public LedgerAccountsController(PharmacyDbContext db, IAccountingPostingService accounting)
        {
            _db = db;
            _accounting = accounting;
        }

        // GET api/ledgeraccounts — filterable posting list.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<LedgerLineReadDto>>> GetAll(
            [FromQuery] int? chartOfAccountId,
            [FromQuery] string? voucherNo,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var query = _db.LedgerAccounts.AsNoTracking().Include(l => l.ChartOfAccount).AsQueryable();

            if (chartOfAccountId is not null)
                query = query.Where(l => l.ChartOfAccountId == chartOfAccountId);
            if (!string.IsNullOrWhiteSpace(voucherNo))
                query = query.Where(l => l.VoucherNo == voucherNo);
            if (fromDate is not null)
                query = query.Where(l => l.TransactionDate >= fromDate);
            if (toDate is not null)
                query = query.Where(l => l.TransactionDate <= toDate);

            var lines = await query
                .OrderByDescending(l => l.TransactionDate)
                .Select(l => MapLine(l))
                .ToListAsync();

            return Ok(lines);
        }

        // GET api/ledgeraccounts/5 — a single posting line.
        [HttpGet("{id:int}")]
        public async Task<ActionResult<LedgerLineReadDto>> GetById(int id)
        {
            var line = await _db.LedgerAccounts
                .AsNoTracking()
                .Include(l => l.ChartOfAccount)
                .Where(l => l.Id == id)
                .Select(l => MapLine(l))
                .FirstOrDefaultAsync();

            if (line is null)
                return NotFoundResponse($"Ledger posting {id} not found.");

            return Ok(line);
        }

        // GET api/ledgeraccounts/voucher/JV-20260101-abcd1234 — every line of
        // one journal entry, i.e. the full transaction.
        [HttpGet("voucher/{voucherNo}")]
        public async Task<ActionResult<JournalEntryReadDto>> GetByVoucher(string voucherNo)
        {
            var lines = await _db.LedgerAccounts
                .AsNoTracking()
                .Include(l => l.ChartOfAccount)
                .Where(l => l.VoucherNo == voucherNo)
                .OrderBy(l => l.Id)
                .ToListAsync();

            if (lines.Count == 0)
                return NotFoundResponse($"No ledger postings found for voucher '{voucherNo}'.");

            return Ok(new JournalEntryReadDto
            {
                VoucherNo = voucherNo,
                TransactionDate = lines[0].TransactionDate,
                Lines = lines.Select(MapLine).ToList(),
                TotalDebit = lines.Sum(l => l.DebitAmount),
                TotalCredit = lines.Sum(l => l.CreditAmount)
            });
        }

        // POST api/ledgeraccounts/journal-entries — posts one balanced
        // double-entry transaction: 2+ lines sharing a VoucherNo, each line
        // strictly a debit OR a credit, total debits == total credits.
        [Authorize(Roles = "Admin")]
        [HttpPost("journal-entries")]
        public async Task<ActionResult<JournalEntryReadDto>> CreateJournalEntry([FromBody] JournalEntryCreateDto dto)
        {
            var postedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(postedBy)) return Unauthorized();
            if (dto.Lines.Count < 2)
                return BadRequestResponse("A journal entry needs at least two lines (one debit, one credit).");

            foreach (var line in dto.Lines)
            {
                bool isDebit = line.DebitAmount > 0 && line.CreditAmount == 0;
                bool isCredit = line.CreditAmount > 0 && line.DebitAmount == 0;
                if (!isDebit && !isCredit)
                    return BadRequestResponse("Each line must have exactly one of DebitAmount or CreditAmount greater than zero, not both or neither.");

                var account = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == line.ChartOfAccountId);
                if (account is null || !account.IsActive) return BadRequestResponse($"Account {line.ChartOfAccountId} does not exist or is inactive.");
                if (await _db.ChartOfAccounts.AnyAsync(a => a.ParentId == line.ChartOfAccountId)) return BadRequestResponse($"Account {line.ChartOfAccountId} is a header account and cannot receive ledger postings.");
            }

            decimal totalDebit = dto.Lines.Sum(l => l.DebitAmount);
            decimal totalCredit = dto.Lines.Sum(l => l.CreditAmount);
            if (totalDebit != totalCredit)
                return BadRequestResponse($"Entry is not balanced: total debits {totalDebit} != total credits {totalCredit}.");

            string voucherNo = string.IsNullOrWhiteSpace(dto.VoucherNo)
                ? $"JV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}"
                : dto.VoucherNo;

            if (await _db.LedgerAccounts.AnyAsync(l => l.VoucherNo == voucherNo))
                return ConflictResponse($"Voucher '{voucherNo}' already has postings — use a different VoucherNo.");

            var transactionDate = dto.TransactionDate ?? DateTime.Now;
            var postedLines = new List<LedgerAccount>();

            foreach (var lineDto in dto.Lines)
            {
                decimal previousBalance = await _db.LedgerAccounts
                    .Where(l => l.ChartOfAccountId == lineDto.ChartOfAccountId)
                    .OrderByDescending(l => l.TransactionDate).ThenByDescending(l => l.Id)
                    .Select(l => l.RunningBalance)
                    .FirstOrDefaultAsync();

                // Running balance = previous + debit - credit. This treats every
                // account on a debit-normal basis; for a credit-normal account
                // (revenue/liability/equity) a "rising" balance will show as
                // more negative — consistent, just invert the sign when you
                // display statements for those account types.
                decimal newBalance = previousBalance + lineDto.DebitAmount - lineDto.CreditAmount;

                var posted = new LedgerAccount
                {
                    ChartOfAccountId = lineDto.ChartOfAccountId,
                    VoucherNo = voucherNo,
                    TransactionDate = transactionDate,
                    DebitAmount = lineDto.DebitAmount,
                    CreditAmount = lineDto.CreditAmount,
                    RunningBalance = newBalance,
                    Description = lineDto.Description ?? dto.Description,
                    SourceType = dto.SourceType,
                    SourceId = dto.SourceId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = postedBy
                };

                postedLines.Add(posted);
                _db.LedgerAccounts.Add(posted);
            }

            await _db.SaveChangesAsync();
            await _accounting.RecalculateRunningBalancesAsync(postedLines.Select(l => l.ChartOfAccountId));

            foreach (var line in postedLines)
                await _db.Entry(line).Reference(l => l.ChartOfAccount).LoadAsync();

            return CreatedAtAction(nameof(GetByVoucher), new { voucherNo }, new JournalEntryReadDto
            {
                VoucherNo = voucherNo,
                TransactionDate = transactionDate,
                Lines = postedLines.Select(MapLine).ToList(),
                TotalDebit = totalDebit,
                TotalCredit = totalCredit
            });
        }

        // POST api/ledgeraccounts/voucher/JV-.../reverse — posts a new,
        // opposite-sign journal entry that cancels this one out, and flags
        // the original lines IsReversed so they're excluded from open
        // balances going forward. History is kept, nothing is edited/deleted.
        [Authorize(Roles = "Admin")]
        [HttpPost("voucher/{voucherNo}/reverse")]
        public async Task<ActionResult<JournalEntryReadDto>> ReverseVoucher(string voucherNo)
        {
            var reversedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(reversedBy)) return Unauthorized();
            var originalLines = await _db.LedgerAccounts
                .Where(l => l.VoucherNo == voucherNo)
                .ToListAsync();

            if (originalLines.Count == 0)
                return NotFoundResponse($"No ledger postings found for voucher '{voucherNo}'.");

            if (originalLines.Any(l => l.IsReversed))
                return ConflictResponse($"Voucher '{voucherNo}' has already been reversed.");

            string reversalVoucherNo = $"REV-{voucherNo}";
            if (await _db.LedgerAccounts.AnyAsync(l => l.VoucherNo == reversalVoucherNo))
                return ConflictResponse($"A reversal for voucher '{voucherNo}' already exists.");

            var reversalLines = new List<LedgerAccount>();

            foreach (var original in originalLines)
            {
                decimal previousBalance = await _db.LedgerAccounts
                    .Where(l => l.ChartOfAccountId == original.ChartOfAccountId)
                    .OrderByDescending(l => l.TransactionDate).ThenByDescending(l => l.Id)
                    .Select(l => l.RunningBalance)
                    .FirstOrDefaultAsync();

                // Swap debit <-> credit to cancel the original out.
                decimal newBalance = previousBalance + original.CreditAmount - original.DebitAmount;

                var reversal = new LedgerAccount
                {
                    ChartOfAccountId = original.ChartOfAccountId,
                    VoucherNo = reversalVoucherNo,
                    TransactionDate = DateTime.Now,
                    DebitAmount = original.CreditAmount,
                    CreditAmount = original.DebitAmount,
                    RunningBalance = newBalance,
                    Description = $"Reversal of {voucherNo}" + (original.Description is null ? "" : $" — {original.Description}"),
                    SourceType = original.SourceType,
                    SourceId = original.SourceId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = reversedBy
                };

                reversalLines.Add(reversal);
                _db.LedgerAccounts.Add(reversal);
                original.IsReversed = true;
            }

            await _db.SaveChangesAsync();
            await _accounting.RecalculateRunningBalancesAsync(reversalLines.Select(l => l.ChartOfAccountId));

            foreach (var line in reversalLines)
                await _db.Entry(line).Reference(l => l.ChartOfAccount).LoadAsync();

            return Ok(new JournalEntryReadDto
            {
                VoucherNo = reversalVoucherNo,
                TransactionDate = reversalLines[0].TransactionDate,
                Lines = reversalLines.Select(MapLine).ToList(),
                TotalDebit = reversalLines.Sum(l => l.DebitAmount),
                TotalCredit = reversalLines.Sum(l => l.CreditAmount)
            });
        }

        private static LedgerLineReadDto MapLine(LedgerAccount l) => new()
        {
            Id = l.Id,
            ChartOfAccountId = l.ChartOfAccountId,
            ChartOfAccountName = l.ChartOfAccount != null ? l.ChartOfAccount.Name : string.Empty,
            VoucherNo = l.VoucherNo,
            TransactionDate = l.TransactionDate,
            DebitAmount = l.DebitAmount,
            CreditAmount = l.CreditAmount,
            RunningBalance = l.RunningBalance,
            Description = l.Description,
            SourceType = l.SourceType,
            SourceId = l.SourceId,
            IsReversed = l.IsReversed,
            CreatedAt = l.CreatedAt,
            CreatedByUserId = l.CreatedByUserId
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `PaymentMethodsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Payment;

namespace PharmacyV2.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentMethodsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        public PaymentMethodsController(PharmacyDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaymentMethodReadDto>>> GetAll()
        {
            var items = await _db.PaymentMethods.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            return Ok(items.Select(ToDto));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<PaymentMethodReadDto>> GetById(int id)
        {
            var item = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            return item is null ? NotFound(new ApiError($"Payment method {id} not found.")) : Ok(ToDto(item));
        }

        [HttpPost]
        public async Task<ActionResult<PaymentMethodReadDto>> Create([FromBody] PaymentMethodWriteDto dto)
        {
            if (await _db.PaymentMethods.AnyAsync(p => p.Name == dto.Name))
                return Conflict(new ApiError($"Payment method '{dto.Name}' already exists."));

            var item = new PaymentMethod { Name = dto.Name, LedgerAccountCode = dto.LedgerAccountCode, IsActive = dto.IsActive };
            _db.PaymentMethods.Add(item);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = item.Id }, ToDto(item));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PaymentMethodWriteDto dto)
        {
            var item = await _db.PaymentMethods.FirstOrDefaultAsync(p => p.Id == id);
            if (item is null) return NotFound(new ApiError($"Payment method {id} not found."));

            if (await _db.PaymentMethods.AnyAsync(p => p.Name == dto.Name && p.Id != id))
                return Conflict(new ApiError($"Payment method '{dto.Name}' already exists."));

            item.Name = dto.Name;
            item.LedgerAccountCode = dto.LedgerAccountCode;
            item.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.PaymentMethods.FirstOrDefaultAsync(p => p.Id == id);
            if (item is null) return NotFound(new ApiError($"Payment method {id} not found."));

            // Sale.PaymentMethod / PurchaseInvoice.PaymentMethod free-text string কলাম (FK না),
            // তাই "already in use" চেক করার সহজ উপায় নেই — হার্ড ডিলিট না করে deactivate করাই নিরাপদ।
            item.IsActive = false;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static PaymentMethodReadDto ToDto(PaymentMethod p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            LedgerAccountCode = p.LedgerAccountCode,
            IsActive = p.IsActive
        };
    }
}
```

### `PrescriptionsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PrescriptionsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;
        private const string PrescriptionImagesRelativeFolder = "images/prescriptions";

        public PrescriptionsController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private string GetImagesFolderPath()
        {
            string webRoot = string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
            string folderPath = Path.Combine(webRoot, "images", "prescriptions");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PrescriptionReadDto>>> GetAll([FromQuery] int? customerId, [FromQuery] int? doctorId)
        {
            var query = _db.Prescriptions.AsNoTracking()
                .Include(p => p.Doctor).Include(p => p.Customer).Include(p => p.Items).ThenInclude(i => i.Product)
                .AsQueryable();
            if (customerId is not null) query = query.Where(p => p.CustomerId == customerId);
            if (doctorId is not null) query = query.Where(p => p.DoctorId == doctorId);

            var list = await query.OrderByDescending(p => p.PrescriptionDate).ToListAsync();
            return Ok(list.Select(ToPrescriptionDto));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<PrescriptionReadDto>> GetById(int id)
        {
            var p = await _db.Prescriptions.AsNoTracking()
                .Include(x => x.Doctor).Include(x => x.Customer).Include(x => x.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));
            return Ok(ToPrescriptionDto(p));
        }

        [HttpPost]
        public async Task<ActionResult<PrescriptionReadDto>> Create([FromBody] PrescriptionCreateDto dto)
        {
            if (!await _db.Doctors.AnyAsync(d => d.DoctorId == dto.DoctorId))
                return BadRequest(new ApiError($"Doctor {dto.DoctorId} does not exist."));
            if (!await _db.Customers.AnyAsync(c => c.CustomerId == dto.CustomerId))
                return BadRequest(new ApiError($"Customer {dto.CustomerId} does not exist."));
            if (dto.SaleId is not null && !await _db.Sales.AnyAsync(s => s.SaleId == dto.SaleId))
                return BadRequest(new ApiError($"Sale {dto.SaleId} does not exist."));

            var prescription = new Prescription
            {
                DoctorId = dto.DoctorId,
                CustomerId = dto.CustomerId,
                PrescriptionDate = dto.PrescriptionDate,
                Diagnosis = dto.Diagnosis,
                Notes = dto.Notes,
                SaleId = dto.SaleId,
                CreatedAt = DateTime.UtcNow
            };

            if (dto.Items is { Count: > 0 })
            {
                foreach (var itemDto in dto.Items)
                {
                    if (itemDto.ProductId is not null && !await _db.Products.AnyAsync(p => p.Id == itemDto.ProductId))
                        return BadRequest(new ApiError($"Product {itemDto.ProductId} does not exist."));

                    prescription.Items.Add(new PrescriptionItem
                    {
                        ProductId = itemDto.ProductId,
                        MedicineName = itemDto.MedicineName,
                        Dosage = itemDto.Dosage,
                        Duration = itemDto.Duration,
                        Instructions = itemDto.Instructions
                    });
                }
            }

            _db.Prescriptions.Add(prescription);
            await _db.SaveChangesAsync();

            await _db.Entry(prescription).Reference(p => p.Doctor).LoadAsync();
            await _db.Entry(prescription).Reference(p => p.Customer).LoadAsync();
            await _db.Entry(prescription).Collection(p => p.Items).Query().Include(i => i.Product).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = prescription.PrescriptionId }, ToPrescriptionDto(prescription));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PrescriptionUpdateDto dto)
        {
            var existing = await _db.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.PrescriptionId == id);
            if (existing is null) return NotFound(new ApiError($"Prescription {id} not found."));

            if (!await _db.Doctors.AnyAsync(d => d.DoctorId == dto.DoctorId))
                return BadRequest(new ApiError($"Doctor {dto.DoctorId} does not exist."));
            if (!await _db.Customers.AnyAsync(c => c.CustomerId == dto.CustomerId))
                return BadRequest(new ApiError($"Customer {dto.CustomerId} does not exist."));
            if (dto.SaleId is not null && !await _db.Sales.AnyAsync(s => s.SaleId == dto.SaleId))
                return BadRequest(new ApiError($"Sale {dto.SaleId} does not exist."));

            existing.DoctorId = dto.DoctorId;
            existing.CustomerId = dto.CustomerId;
            existing.PrescriptionDate = dto.PrescriptionDate ?? existing.PrescriptionDate;
            existing.Diagnosis = dto.Diagnosis;
            existing.Notes = dto.Notes;
            existing.SaleId = dto.SaleId;

            if (dto.Items is not null)
            {
                var incomingIds = dto.Items.Where(i => i.Id is > 0).Select(i => i.Id!.Value).ToHashSet();

                var toRemove = existing.Items.Where(i => !incomingIds.Contains(i.Id)).ToList();
                foreach (var item in toRemove)
                {
                    _db.PrescriptionItems.Remove(item);
                    existing.Items.Remove(item);
                }

                foreach (var itemDto in dto.Items.Where(i => i.Id is > 0))
                {
                    var item = existing.Items.FirstOrDefault(i => i.Id == itemDto.Id);
                    if (item is null) continue;
                    item.ProductId = itemDto.ProductId;
                    item.MedicineName = itemDto.MedicineName;
                    item.Dosage = itemDto.Dosage;
                    item.Duration = itemDto.Duration;
                    item.Instructions = itemDto.Instructions;
                }

                foreach (var itemDto in dto.Items.Where(i => i.Id is null or 0))
                {
                    existing.Items.Add(new PrescriptionItem
                    {
                        ProductId = itemDto.ProductId,
                        MedicineName = itemDto.MedicineName,
                        Dosage = itemDto.Dosage,
                        Duration = itemDto.Duration,
                        Instructions = itemDto.Instructions
                    });
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var p = await _db.Prescriptions.FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));

            DeleteImageFile(p.ImagePath);
            _db.Prescriptions.Remove(p);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id:int}/image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImage(int id, IFormFile file)
        {
            var p = await _db.Prescriptions.FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));
            if (file is null || file.Length == 0) return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            string[] allowed = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            if (!allowed.Contains(extension.ToLowerInvariant()))
                return BadRequest(new ApiError("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            string folderPath = GetImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N")[..8];
            string fileName = $"{id}_{shortGuid}{extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folderPath, fileName), ms.ToArray());

            DeleteImageFile(p.ImagePath);
            p.Image = null;
            p.ImageContentType = file.ContentType;
            p.ImagePath = "/" + PrescriptionImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { p.PrescriptionId, p.ImagePath });
        }

        [HttpDelete("{id:int}/image")]
        public async Task<IActionResult> DeleteImage(int id)
        {
            var p = await _db.Prescriptions.FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));

            DeleteImageFile(p.ImagePath);
            p.ImagePath = null;
            p.Image = null;
            p.ImageContentType = null;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private void DeleteImageFile(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return;
            if (imagePath.IndexOf(PrescriptionImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0) return;
            string fileName = Path.GetFileName(imagePath);
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string fullPath = Path.Combine(GetImagesFolderPath(), fileName);
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }

        public static PrescriptionReadDto ToPrescriptionDto(Prescription p) => new()
        {
            PrescriptionId = p.PrescriptionId,
            DoctorId = p.DoctorId,
            DoctorName = p.Doctor?.Name,
            CustomerId = p.CustomerId,
            CustomerName = p.Customer is null ? null : $"{p.Customer.FirstName} {p.Customer.LastName}".Trim(),
            PrescriptionDate = p.PrescriptionDate,
            Diagnosis = p.Diagnosis,
            Notes = p.Notes,
            ImagePath = p.ImagePath,
            ImageContentType = p.ImageContentType,
            SaleId = p.SaleId,
            CreatedAt = p.CreatedAt,
            Items = p.Items.Select(i => new PrescriptionItemReadDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product?.ProductName,
                MedicineName = i.MedicineName,
                Dosage = i.Dosage,
                Duration = i.Duration,
                Instructions = i.Instructions
            }).ToList()
        };
    }
}
```

### `ProductGroupsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers;

[Authorize]
[Route("api/productgroups")]
[ApiController]
public class ProductGroupsController : ControllerBase
{
    private readonly PharmacyDbContext _db;
    public ProductGroupsController(PharmacyDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductGroupReadDto>>> GetAll()
        => Ok(await _db.ProductGroups.AsNoTracking().OrderBy(g => g.Name).Select(g => new ProductGroupReadDto
        {
            Id = g.Id, Name = g.Name, GenericName = g.GenericName, CompanyId = g.CompanyId,
            CompanyName = g.Company != null ? g.Company.Name : null, VariantCount = g.Variants.Count
        }).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductGroupDetailDto>> GetById(int id)
    {
        var group = await _db.ProductGroups.AsNoTracking().Where(g => g.Id == id)
            .Select(g => new ProductGroupDetailDto
            {
                Id = g.Id, Name = g.Name, GenericName = g.GenericName, CompanyId = g.CompanyId,
                CompanyName = g.Company != null ? g.Company.Name : null, VariantCount = g.Variants.Count,
                Variants = g.Variants.OrderBy(v => v.Strength).Select(v => new ProductVariantReadDto
                {
                    Id = v.Id,
                    ProductGroupId = v.ProductGroupId,
                    Strength = v.Strength,
                    DosageFormId = v.DosageFormId,
                    DosageFormName = v.DosageForm.Name
                }).ToList()
            }).FirstOrDefaultAsync();
        return group is null ? NotFound(new ApiError($"Product group {id} not found.")) : Ok(group);
    }

    [HttpPost]
    public async Task<ActionResult<ProductGroupReadDto>> Create(ProductGroupWriteDto dto)
    {
        if (await _db.ProductGroups.AnyAsync(g => g.Name == dto.Name)) return Conflict(new ApiError("A product group with this name already exists."));
        if (dto.CompanyId is not null && !await _db.Companies.AnyAsync(c => c.Id == dto.CompanyId)) return BadRequest(new ApiError("Selected company does not exist."));
        var group = new ProductGroup { Name = dto.Name.Trim(), GenericName = dto.GenericName?.Trim(), CompanyId = dto.CompanyId };
        _db.ProductGroups.Add(group); await _db.SaveChangesAsync();
        await _db.Entry(group).Reference(g => g.Company).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = group.Id }, ToRead(group));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ProductGroupWriteDto dto)
    {
        var group = await _db.ProductGroups.FindAsync(id);
        if (group is null) return NotFound(new ApiError($"Product group {id} not found."));
        if (await _db.ProductGroups.AnyAsync(g => g.Name == dto.Name && g.Id != id)) return Conflict(new ApiError("A product group with this name already exists."));
        if (dto.CompanyId is not null && !await _db.Companies.AnyAsync(c => c.Id == dto.CompanyId)) return BadRequest(new ApiError("Selected company does not exist."));
        group.Name = dto.Name.Trim(); group.GenericName = dto.GenericName?.Trim(); group.CompanyId = dto.CompanyId;
        await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.ProductGroups.Include(g => g.Variants).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound(new ApiError($"Product group {id} not found."));
        if (await _db.Products.AnyAsync(p => p.ProductVariant != null && p.ProductVariant.ProductGroupId == id))
            return Conflict(new ApiError("This group has variants already used by products."));
        _db.ProductGroups.Remove(group); await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpPost("{groupId:int}/variants")]
    public async Task<ActionResult<ProductVariantReadDto>> CreateVariant(int groupId, ProductVariantWriteDto dto)
    {
        if (!await _db.ProductGroups.AnyAsync(g => g.Id == groupId)) return NotFound(new ApiError($"Product group {groupId} not found."));
        if (!await _db.DosageForms.AnyAsync(d => d.Id == dto.DosageFormId)) return BadRequest(new ApiError("Selected dosage form does not exist."));
        if (await _db.ProductVariants.AnyAsync(v => v.ProductGroupId == groupId && v.Strength == dto.Strength && v.DosageFormId == dto.DosageFormId)) return Conflict(new ApiError("This strength and dosage form already exists in the group."));
        var variant = new ProductVariant { ProductGroupId = groupId, Strength = dto.Strength.Trim(), DosageFormId = dto.DosageFormId };
        _db.ProductVariants.Add(variant); await _db.SaveChangesAsync(); await _db.Entry(variant).Reference(v => v.DosageForm).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = groupId }, ToVariant(variant));
    }

    [HttpPut("{groupId:int}/variants/{id:int}")]
    public async Task<IActionResult> UpdateVariant(int groupId, int id, ProductVariantWriteDto dto)
    {
        var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == id && v.ProductGroupId == groupId);
        if (variant is null) return NotFound(new ApiError("Product variant not found."));
        if (!await _db.DosageForms.AnyAsync(d => d.Id == dto.DosageFormId)) return BadRequest(new ApiError("Selected dosage form does not exist."));
        if (await _db.ProductVariants.AnyAsync(v => v.Id != id && v.ProductGroupId == groupId && v.Strength == dto.Strength && v.DosageFormId == dto.DosageFormId)) return Conflict(new ApiError("This strength and dosage form already exists in the group."));
        variant.Strength = dto.Strength.Trim(); variant.DosageFormId = dto.DosageFormId; await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpDelete("{groupId:int}/variants/{id:int}")]
    public async Task<IActionResult> DeleteVariant(int groupId, int id)
    {
        var variant = await _db.ProductVariants.Include(v => v.Products).FirstOrDefaultAsync(v => v.Id == id && v.ProductGroupId == groupId);
        if (variant is null) return NotFound(new ApiError("Product variant not found."));
        if (variant.Products.Any()) return Conflict(new ApiError("Cannot delete a variant used by products."));
        _db.ProductVariants.Remove(variant); await _db.SaveChangesAsync(); return NoContent();
    }

    private static ProductGroupReadDto ToRead(ProductGroup g) => new() { Id = g.Id, Name = g.Name, GenericName = g.GenericName, CompanyId = g.CompanyId, CompanyName = g.Company?.Name, VariantCount = g.Variants.Count };
    private static ProductVariantReadDto ToVariant(ProductVariant v) => new() { Id = v.Id, ProductGroupId = v.ProductGroupId, Strength = v.Strength, DosageFormId = v.DosageFormId, DosageFormName = v.DosageForm.Name };
}
```

### `Productrakscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // CRUD for ProductRak — the shelf/rack a ProductStock batch physically
    // sits on within a Warehouse. Had a model + DbSet but no controller.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ProductRaksController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public ProductRaksController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/productraks — optionally ?warehouseId=3 to filter one warehouse's racks.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductRakReadDto>>> GetAll([FromQuery] int? warehouseId)
        {
            var query = _db.ProductRaks.AsNoTracking().Include(r => r.Warehouse).AsQueryable();

            if (warehouseId is not null)
                query = query.Where(r => r.WarehouseId == warehouseId);

            var raks = await query
                .Select(r => new ProductRakReadDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    IsActive = r.IsActive,
                    WarehouseId = r.WarehouseId,
                    WarehouseName = r.Warehouse.Name,
                    StockRowCount = r.ProductStocks.Count
                })
                .ToListAsync();

            return Ok(raks);
        }

        // GET api/productraks/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProductRakReadDto>> GetById(int id)
        {
            var rak = await _db.ProductRaks
                .AsNoTracking()
                .Include(r => r.Warehouse)
                .Where(r => r.Id == id)
                .Select(r => new ProductRakReadDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    IsActive = r.IsActive,
                    WarehouseId = r.WarehouseId,
                    WarehouseName = r.Warehouse.Name,
                    StockRowCount = r.ProductStocks.Count
                })
                .FirstOrDefaultAsync();

            if (rak is null)
                return NotFoundResponse($"Rack {id} not found.");

            return Ok(rak);
        }

        // POST api/productraks — Name isn't globally unique (no DB index),
        // but two racks with the same name in the same warehouse would be
        // confusing, so that combination is checked here.
        [HttpPost]
        public async Task<ActionResult<ProductRakReadDto>> Create([FromBody] ProductRakWriteDto dto)
        {
            if (!await _db.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId))
                return BadRequestResponse($"Warehouse {dto.WarehouseId} does not exist.");

            if (await _db.ProductRaks.AnyAsync(r => r.WarehouseId == dto.WarehouseId && r.Name == dto.Name))
                return ConflictResponse($"Rack '{dto.Name}' already exists in this warehouse.");

            var rak = new ProductRak
            {
                Name = dto.Name,
                IsActive = dto.IsActive,
                WarehouseId = dto.WarehouseId
            };

            _db.ProductRaks.Add(rak);
            await _db.SaveChangesAsync();

            await _db.Entry(rak).Reference(r => r.Warehouse).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = rak.Id }, new ProductRakReadDto
            {
                Id = rak.Id,
                Name = rak.Name,
                IsActive = rak.IsActive,
                WarehouseId = rak.WarehouseId,
                WarehouseName = rak.Warehouse.Name,
                StockRowCount = 0
            });
        }

        // PUT api/productraks/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductRakWriteDto dto)
        {
            var rak = await _db.ProductRaks.FirstOrDefaultAsync(r => r.Id == id);
            if (rak is null)
                return NotFoundResponse($"Rack {id} not found.");

            if (!await _db.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId))
                return BadRequestResponse($"Warehouse {dto.WarehouseId} does not exist.");

            if (await _db.ProductRaks.AnyAsync(r => r.WarehouseId == dto.WarehouseId && r.Name == dto.Name && r.Id != id))
                return ConflictResponse($"Rack '{dto.Name}' already exists in this warehouse.");

            rak.Name = dto.Name;
            rak.IsActive = dto.IsActive;
            rak.WarehouseId = dto.WarehouseId;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/productraks/5 — allowed even if stock batches still
        // point at it: ProductStock.ProductRakId is SetNull on delete, so
        // those batches just become "unassigned to a rack" rather than blocked.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var rak = await _db.ProductRaks.FirstOrDefaultAsync(r => r.Id == id);
            if (rak is null)
                return NotFoundResponse($"Rack {id} not found.");

            _db.ProductRaks.Remove(rak);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `ProductsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;
using PharmacyV2.Services;

namespace PharmacyV2.Controllers
{
    // Master-detail pairs: Product -> ProductDetails (1:1) and
    // Product -> ProductPrice (1:many, one row per Unit).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ICodeGeneratorService _codeGenerator;

        // Folder (under wwwroot) where product images physically live, and
        // the matching public URL prefix used to build ImagePath.
        private const string ProductImagesRelativeFolder = "images/products";

        public ProductsController(PharmacyDbContext db, IWebHostEnvironment env, ICodeGeneratorService codeGenerator)
        {
            _db = db;
            _env = env;
            _codeGenerator = codeGenerator;
        }

        // wwwroot may not exist yet on a fresh clone (no folder is
        // checked in to git). This makes sure "wwwroot/images/products"
        // is created on demand instead of throwing a DirectoryNotFoundException
        // the first time someone uploads a product image.
        private string GetProductImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "products");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/products?search=napa&companyId=&dosageFormId=&strength=&page=1&pageSize=20
        // Backs the Brand Search page (P2): "search" matches brand name OR
        // generic name (e.g. searching "Paracetamol" finds Napa, Ace, A-One...),
        // and companyId/dosageFormId/strength narrow it down further —
        // mirrors medex.com.bd's Company / Strength / Dosage form filters.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductListItemDto>>> GetAll(
            [FromQuery] string? search,
            [FromQuery] int? companyId,
            [FromQuery] int? dosageFormId,
            [FromQuery] string? strength,
            [FromQuery] string? brandType,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

            var query = _db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .Include(p => p.Company)
                .Include(p => p.DosageForm)
                .Include(p => p.ProductDetails)
                .Include(p => p.ProductPrices).ThenInclude(pp => pp.Unit)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    p.ProductName.Contains(search) ||
                    (p.GenericName != null && p.GenericName.Contains(search)) ||
                    (p.Company != null && p.Company.Name.Contains(search)) ||
                    (p.ProductCode != null && p.ProductCode.Contains(search)) ||
                    (p.Barcode != null && p.Barcode.Contains(search)));
            }

            if (companyId is not null)
                query = query.Where(p => p.CompanyId == companyId);

            if (dosageFormId is not null)
                query = query.Where(p => p.DosageFormId == dosageFormId);

            if (!string.IsNullOrWhiteSpace(strength))
                query = query.Where(p => p.Strength.Contains(strength));

            if (!string.IsNullOrWhiteSpace(brandType))
                query = query.Where(p => p.BrandType == brandType);

            var rows = await query
                .OrderBy(p => p.ProductName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    p.ProductCode,
                    p.ProductName,
                    p.Strength,
                    p.GenericName,
                    p.BrandType,
                    p.CompanyId,
                    CompanyName = p.Company != null ? p.Company.Name : null,
                    p.DosageFormId,
                    DosageFormName = p.DosageForm != null ? p.DosageForm.Name : null,
                    p.UnitId,
                    UnitName = p.Unit.Name,
                    p.UnitPrice,
                    p.PurchasePrice,
                    SalePrice = p.SalePrice,
                    p.StockQuantity,
                    p.ImagePath,
                    Details = p.ProductDetails, // null when no details row exists (1:1 nav)
                    Prices = p.ProductPrices.ToList()
                })
                .ToListAsync();

            var products = rows.Select(r => new ProductListItemDto
            {
                Id = r.Id,
                ProductCode = r.ProductCode,
                ProductName = r.ProductName,
                Strength = r.Strength,
                GenericName = r.GenericName,
                BrandType = r.BrandType,
                CompanyId = r.CompanyId,
                CompanyName = r.CompanyName,
                DosageFormId = r.DosageFormId,
                DosageFormName = r.DosageFormName,
                UnitId = r.UnitId,
                UnitName = r.UnitName,
                UnitPrice = r.UnitPrice,
                PurchasePrice = r.PurchasePrice,
                SalePrice = r.SalePrice,
                StockQuantity = r.StockQuantity,
                ImagePath = r.ImagePath,

                // Flattened 1:1 details row — null-safe, so products with no
                // ProductDetails row simply come back with these as null.
                Manufacturer = r.Details?.Manufacturer,
                Schedule = r.Details?.Schedule,
                DarNo = r.Details?.DarNo,
                StorageConditions = r.Details?.StorageConditions,
                TemperatureMin = r.Details?.TemperatureMin,
                TemperatureMax = r.Details?.TemperatureMax,
                SideEffects = r.Details?.SideEffects,
                PregnancyCategory = r.Details?.PregnancyCategory,
                RequiresPrescription = r.Details?.RequiresPrescription,
                IsControlledDrug = r.Details?.IsControlledDrug,
                Prices = r.Prices.Select(MapPriceToReadDto).ToList()
            }).ToList();

            return Ok(products);
        }

        // GET api/products/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProductReadDto>> GetById(int id)
        {
            var product = await LoadProductAsync(id, tracking: false);
            if (product is null)
                return NotFoundResponse($"Product {id} not found.");

            return Ok(MapToReadDto(product));
        }

        // GET api/products/brands?letter=A&brandType=Allopathic&page=1&pageSize=60
        // "List of Brand Names" page (P3) — every brand, alphabetical,
        // optionally jumped to a starting letter (the A-Z strip on
        // medex.com.bd/brands), and optionally narrowed to "Allopathic" or
        // "Herbal" (the medex.com.bd Browse menu split). Omit "letter" /
        // "brandType" to page through everything.
        [HttpGet("brands")]
        public async Task<ActionResult<IEnumerable<ProductBrandListItemDto>>> GetBrandList(
            [FromQuery] string? letter,
            [FromQuery] string? brandType,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 60)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 500 ? 60 : pageSize;

            var query = _db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .Include(p => p.Company)
                .Include(p => p.DosageForm)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(letter))
            {
                var l = letter.Trim().Substring(0, 1);
                query = query.Where(p => p.ProductName.StartsWith(l));
            }

            if (!string.IsNullOrWhiteSpace(brandType))
                query = query.Where(p => p.BrandType == brandType);

            var rows = await query
                .OrderBy(p => p.ProductName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProductBrandListItemDto
                {
                    Id = p.Id,
                    ProductName = p.ProductName,
                    Strength = p.Strength,
                    GenericName = p.GenericName,
                    BrandType = p.BrandType,
                    ImagePath = p.ImagePath,
                    UnitName = p.Unit.Name,
                    CompanyName = p.Company != null ? p.Company.Name : null,
                    DosageFormName = p.DosageForm != null ? p.DosageForm.Name : null
                })
                .ToListAsync();

            return Ok(rows);
        }

        // GET api/products/strengths?search=&companyId=&dosageFormId=&brandType=
        // Distinct strength values (e.g. "500mg", "1000mg"), for the Brand
        // Search page's Strength dropdown (P2, picture 1) — a picklist of
        // real values instead of a free-text box. Narrowed by whatever
        // filters are already active so the list only shows strengths that
        // actually exist for the current search/company/dosage form/type.
        [HttpGet("strengths")]
        public async Task<ActionResult<IEnumerable<string>>> GetStrengths(
            [FromQuery] string? search,
            [FromQuery] int? companyId,
            [FromQuery] int? dosageFormId,
            [FromQuery] string? brandType)
        {
            var query = _db.Products.AsNoTracking()
                .Where(p => p.Strength != null && p.Strength != string.Empty)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    p.ProductName.Contains(search) ||
                    (p.GenericName != null && p.GenericName.Contains(search)));
            }

            if (companyId is not null)
                query = query.Where(p => p.CompanyId == companyId);

            if (dosageFormId is not null)
                query = query.Where(p => p.DosageFormId == dosageFormId);

            if (!string.IsNullOrWhiteSpace(brandType))
                query = query.Where(p => p.BrandType == brandType);

            var strengths = await query
                .Select(p => p.Strength)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            return Ok(strengths);
        }

        // GET api/products/5/effective-price?unitId=3&supplierId=7
        // The single lookup a Purchase Invoice line item form should call the
        // moment Product + Unit (+ Supplier, if already picked) are known, so
        // UnitCost/SalePrice auto-fill from the RIGHT unit's price instead of
        // the product's flat master price. Checks the supplier-specific price
        // first (SupplierProductPrice), then falls back to the product-level
        // ProductPrice for that unit, then to the master Product fields.
        [HttpGet("{id:int}/effective-price")]
        public async Task<ActionResult<EffectiveUnitPriceDto>> GetEffectivePrice(int id, [FromQuery] int unitId, [FromQuery] int? supplierId)
        {
            var product = await _db.Products.AsNoTracking().Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return NotFoundResponse($"Product {id} not found.");

            var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId);
            if (unit is null) return BadRequestResponse($"Unit {unitId} does not exist.");

            if (supplierId is not null)
            {
                var supplierPrice = await _db.SupplierProductPrices.AsNoTracking()
                    .Include(spp => spp.SupplierProduct)
                    .FirstOrDefaultAsync(spp =>
                        spp.UnitId == unitId &&
                        spp.SupplierProduct.ProductId == id &&
                        spp.SupplierProduct.SupplierId == supplierId);

                if (supplierPrice is not null)
                {
                    return Ok(new EffectiveUnitPriceDto
                    {
                        ProductId = id,
                        UnitId = unitId,
                        UnitName = unit.Name,
                        BaseQuantity = supplierPrice.BaseQuantity,
                        PurchasePrice = supplierPrice.PurchasePrice,
                        SalePrice = supplierPrice.SalePrice,
                        UnitPrice = supplierPrice.UnitPrice,
                        DistributorPrice = supplierPrice.DistributorPrice,
                        Source = "Supplier"
                    });
                }
            }

            var productPrice = await _db.ProductPrices.AsNoTracking()
                .FirstOrDefaultAsync(pp => pp.ProductId == id && pp.UnitId == unitId);

            if (productPrice is not null)
            {
                return Ok(new EffectiveUnitPriceDto
                {
                    ProductId = id,
                    UnitId = unitId,
                    UnitName = unit.Name,
                    BaseQuantity = productPrice.BaseQuantity,
                    PurchasePrice = productPrice.PurchasePrice ?? productPrice.PerUnitPrice,
                    SalePrice = productPrice.SalePrice,
                    UnitPrice = productPrice.PerUnitPrice,
                    DistributorPrice = productPrice.DistributorPrice,
                    Source = "Product"
                });
            }

            // Last resort: no per-unit row at all yet — fall back to the
            // master Product fields (only correct when unitId == product's
            // own base UnitId, but keeps the endpoint from ever 404ing on a
            // product that hasn't had ProductPrice rows set up).
            return Ok(new EffectiveUnitPriceDto
            {
                ProductId = id,
                UnitId = unitId,
                UnitName = unit.Name,
                BaseQuantity = 1,
                PurchasePrice = product.PurchasePrice,
                SalePrice = product.SalePrice,
                UnitPrice = product.UnitPrice,
                DistributorPrice = product.DistributorPrice,
                Source = "Product"
            });
        }

        [HttpGet("{id:int}/price-history")]
        public async Task<ActionResult<IEnumerable<ProductPriceHistoryReadDto>>> GetPriceHistory(int id)
        {
            if (!await _db.Products.AnyAsync(p => p.Id == id)) return NotFoundResponse($"Product {id} not found.");
            var rows = await _db.ProductPriceHistories.AsNoTracking().Include(h => h.Unit)
                .Where(h => h.ProductId == id).OrderByDescending(h => h.ChangedAt).ToListAsync();
            return Ok(rows.Select(h => new ProductPriceHistoryReadDto
            {
                Id = h.Id,
                PriceType = h.PriceType,
                UnitName = h.Unit?.Name,
                PreviousPrice = h.PreviousPrice,
                NewPrice = h.NewPrice,
                PreviousBaseQuantity = h.PreviousBaseQuantity,
                NewBaseQuantity = h.NewBaseQuantity,
                ChangedAt = h.ChangedAt
            }));
        }

        // POST api/products — header + optional Details (1:1) + optional Prices in one call.
        [HttpPost]
        public async Task<ActionResult<ProductReadDto>> Create([FromBody] ProductCreateDto dto)
        {
            var unitExists = await _db.Units.AnyAsync(u => u.Id == dto.UnitId);
            if (!unitExists)
                return BadRequestResponse($"Unit {dto.UnitId} does not exist.");

            if (dto.CompanyId is not null && !await _db.Companies.AnyAsync(c => c.Id == dto.CompanyId))
                return BadRequestResponse($"Company {dto.CompanyId} does not exist.");

            if (dto.DosageFormId is not null && !await _db.DosageForms.AnyAsync(d => d.Id == dto.DosageFormId))
                return BadRequestResponse($"Dosage form {dto.DosageFormId} does not exist.");

            if (dto.ProductVariantId is not null && !await _db.ProductVariants.AnyAsync(v => v.Id == dto.ProductVariantId))
                return BadRequestResponse($"Product variant {dto.ProductVariantId} does not exist.");

            // ProductCode is always system-generated (GUID-backed, see
            // ICodeGeneratorService) — any ProductCode the client sends is
            // ignored here on purpose. This guarantees uniqueness and format
            // without relying on the caller to type/paste a code correctly.
            var generatedProductCode = await _codeGenerator.GenerateProductCodeAsync();

            if (dto.Prices is { Count: > 0 })
            {
                var unitIds = dto.Prices.Select(p => p.UnitId).ToList();
                if (dto.Prices.Select(p => new { p.UnitId, DisplayName = (p.DisplayName ?? string.Empty).Trim() }).Distinct().Count() != dto.Prices.Count)
                    return BadRequestResponse("Duplicate packaging name for the same unit.");
                var existingUnits = await _db.Units.Where(u => unitIds.Contains(u.Id)).CountAsync();
                if (existingUnits != unitIds.Distinct().Count())
                    return BadRequestResponse("One or more Prices[].UnitId do not exist.");
            }

            var product = new Product
            {
                ProductCode = generatedProductCode,
                ProductName = dto.ProductName,
                Strength = dto.Strength,
                GenericName = dto.GenericName,
                Barcode = dto.Barcode,
                BrandType = string.IsNullOrWhiteSpace(dto.BrandType) ? "Allopathic" : dto.BrandType,
                CompanyId = dto.CompanyId,
                DosageFormId = dto.DosageFormId,
                ProductVariantId = dto.ProductVariantId,
                UnitId = dto.UnitId,
                UnitPrice = dto.UnitPrice,
                PurchasePrice = dto.PurchasePrice,
                DistributorPrice = dto.DistributorPrice,
                SalePrice = dto.SalePrice,
                ImagePath = dto.ImagePath,
                ProductImage = dto.ProductImage,
                ProductImageContentType = dto.ProductImageContentType,
                RegisteredDate = dto.RegisteredDate ?? DateTime.UtcNow,
                IsActive = dto.IsActive,
                MinStockQty = dto.MinStockQty,
                MaxStockQty = dto.MaxStockQty
            };

            if (dto.Details is not null)
            {
                product.ProductDetails = MapDetails(dto.Details);
            }

            if (dto.Prices is { Count: > 0 })
            {
                foreach (var priceDto in dto.Prices)
                {
                    product.ProductPrices.Add(new ProductPrice
                    {
                        UnitId = priceDto.UnitId,
                        DisplayName = (priceDto.DisplayName ?? string.Empty).Trim(),
                        PerUnitPrice = priceDto.PerUnitPrice,
                        BaseQuantity = priceDto.BaseQuantity,
                        PurchasePrice = priceDto.PurchasePrice,
                        SalePrice = priceDto.SalePrice,
                        DistributorPrice = priceDto.DistributorPrice
                    });
                }
            }

            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            // Record the initial effective prices too, so the audit timeline
            // starts with product creation rather than the first edit.
            AddHistoryIfChanged(product, "Base", null, null, product.UnitPrice);
            AddHistoryIfChanged(product, "Purchase", null, null, product.PurchasePrice);
            if (product.SalePrice is not null) AddHistoryIfChanged(product, "Sale", null, null, product.SalePrice);
            foreach (var price in product.ProductPrices)
                AddHistoryIfChanged(product, "Packaging", price.UnitId, null, price.PerUnitPrice, null, price.BaseQuantity);
            await _db.SaveChangesAsync();

            // reload with navigation data for a clean response
            await _db.Entry(product).Reference(p => p.Unit).LoadAsync();
            if (product.CompanyId is not null) await _db.Entry(product).Reference(p => p.Company).LoadAsync();
            if (product.DosageFormId is not null) await _db.Entry(product).Reference(p => p.DosageForm).LoadAsync();
            if (product.ProductVariantId is not null)
            {
                await _db.Entry(product).Reference(p => p.ProductVariant).LoadAsync();
                if (product.ProductVariant is not null)
                    await _db.Entry(product.ProductVariant).Reference(v => v.ProductGroup).LoadAsync();
            }
            await _db.Entry(product).Reference(p => p.ProductDetails).LoadAsync();
            await _db.Entry(product).Collection(p => p.ProductPrices).Query().Include(pp => pp.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = product.Id }, MapToReadDto(product));
        }

        // PUT api/products/5 — updates header fields AND, if provided, upserts
        // the 1:1 Details row and fully syncs the Prices collection (add/update/
        // remove to match what's sent), same "full sync" convention used by
        // CustomerUpdateDto.Addresses / EmployeeUpdateDto.Documents. Omit
        // Details/Prices entirely to leave them untouched.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductUpdateDto dto)
        {
            var product = await LoadProductAsync(id, tracking: true);
            if (product is null)
                return NotFoundResponse($"Product {id} not found.");

            var unitExists = await _db.Units.AnyAsync(u => u.Id == dto.UnitId);
            if (!unitExists)
                return BadRequestResponse($"Unit {dto.UnitId} does not exist.");

            if (dto.CompanyId is not null && !await _db.Companies.AnyAsync(c => c.Id == dto.CompanyId))
                return BadRequestResponse($"Company {dto.CompanyId} does not exist.");

            if (dto.DosageFormId is not null && !await _db.DosageForms.AnyAsync(d => d.Id == dto.DosageFormId))
                return BadRequestResponse($"Dosage form {dto.DosageFormId} does not exist.");

            if (dto.ProductVariantId is not null && !await _db.ProductVariants.AnyAsync(v => v.Id == dto.ProductVariantId))
                return BadRequestResponse($"Product variant {dto.ProductVariantId} does not exist.");

            // ProductCode is system-generated once at creation and never
            // changes afterward — intentionally not read from dto here, even
            // if the client still sends one.
            AddHistoryIfChanged(product, "Base", null, product.UnitPrice, dto.UnitPrice);
            AddHistoryIfChanged(product, "Purchase", null, product.PurchasePrice, dto.PurchasePrice);
            AddHistoryIfChanged(product, "Sale", null, product.SalePrice, dto.SalePrice);
            product.ProductName = dto.ProductName;
            product.Strength = dto.Strength;
            product.GenericName = dto.GenericName;
            product.Barcode = dto.Barcode;
            if (!string.IsNullOrWhiteSpace(dto.BrandType)) product.BrandType = dto.BrandType;
            product.CompanyId = dto.CompanyId;
            product.DosageFormId = dto.DosageFormId;
            product.ProductVariantId = dto.ProductVariantId;
            product.UnitId = dto.UnitId;
            product.UnitPrice = dto.UnitPrice;
            product.PurchasePrice = dto.PurchasePrice;
            product.DistributorPrice = dto.DistributorPrice;
            product.SalePrice = dto.SalePrice;
            if (dto.ImagePath is not null) product.ImagePath = dto.ImagePath;
            if (dto.ProductImage is not null) product.ProductImage = dto.ProductImage;
            if (dto.ProductImageContentType is not null) product.ProductImageContentType = dto.ProductImageContentType;

            // Only overwrite RegisteredDate if the caller actually sent one —
            // otherwise the original registration date was getting wiped out
            // to DateTime.MinValue on every update.
            if (dto.RegisteredDate.HasValue)
            {
                product.RegisteredDate = dto.RegisteredDate.Value;
            }

            product.IsActive = dto.IsActive;
            product.MinStockQty = dto.MinStockQty;
            product.MaxStockQty = dto.MaxStockQty;

            if (dto.Details is not null)
            {
                if (product.ProductDetails is null)
                {
                    product.ProductDetails = MapDetails(dto.Details);
                }
                else
                {
                    ApplyDetails(product.ProductDetails, dto.Details);
                }
            }

            if (dto.Prices is not null)
            {
                var unitIds = dto.Prices.Select(p => p.UnitId).ToList();
                if (dto.Prices.Select(p => new { p.UnitId, DisplayName = (p.DisplayName ?? string.Empty).Trim() }).Distinct().Count() != dto.Prices.Count)
                    return BadRequestResponse("Duplicate packaging name for the same unit.");

                if (unitIds.Count > 0)
                {
                    var existingUnits = await _db.Units.Where(u => unitIds.Contains(u.Id)).CountAsync();
                    if (existingUnits != unitIds.Distinct().Count())
                        return BadRequestResponse("One or more Prices[].UnitId do not exist.");
                }

                // Full sync: incoming Id => update, no Id/unmatched Id => insert,
                // any existing row not present in the payload => delete.
                var incomingIds = dto.Prices.Where(p => p.Id is > 0).Select(p => p.Id!.Value).ToHashSet();
                var toRemove = product.ProductPrices.Where(pp => !incomingIds.Contains(pp.Id)).ToList();
                foreach (var priceToRemove in toRemove)
                {
                    _db.ProductPrices.Remove(priceToRemove);
                    product.ProductPrices.Remove(priceToRemove);
                }

                foreach (var priceDto in dto.Prices)
                {
                    var existingPrice = priceDto.Id is > 0
                        ? product.ProductPrices.FirstOrDefault(pp => pp.Id == priceDto.Id)
                        : null;

                    if (existingPrice is not null)
                    {
                        AddHistoryIfChanged(product, "Packaging", existingPrice.UnitId, existingPrice.PerUnitPrice, priceDto.PerUnitPrice, existingPrice.BaseQuantity, priceDto.BaseQuantity);
                        existingPrice.UnitId = priceDto.UnitId;
                        existingPrice.DisplayName = (priceDto.DisplayName ?? string.Empty).Trim();
                        existingPrice.PerUnitPrice = priceDto.PerUnitPrice;
                        existingPrice.BaseQuantity = priceDto.BaseQuantity;
                        existingPrice.PurchasePrice = priceDto.PurchasePrice;
                        existingPrice.SalePrice = priceDto.SalePrice;
                        existingPrice.DistributorPrice = priceDto.DistributorPrice;
                    }
                    else
                    {
                        AddHistoryIfChanged(product, "Packaging", priceDto.UnitId, null, priceDto.PerUnitPrice, null, priceDto.BaseQuantity);
                        product.ProductPrices.Add(new ProductPrice
                        {
                            UnitId = priceDto.UnitId,
                            DisplayName = (priceDto.DisplayName ?? string.Empty).Trim(),
                            PerUnitPrice = priceDto.PerUnitPrice,
                            BaseQuantity = priceDto.BaseQuantity,
                            PurchasePrice = priceDto.PurchasePrice,
                            SalePrice = priceDto.SalePrice,
                            DistributorPrice = priceDto.DistributorPrice
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/products/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product is null)
                return NotFoundResponse($"Product {id} not found.");

            var hasStock = await _db.ProductStocks.AnyAsync(s => s.ProductId == id && s.AvailableQuantity > 0);
            if (hasStock)
                return ConflictResponse("Cannot delete a product that still has available stock.");

            string? imagePathToDelete = product.ImagePath;

            _db.Products.Remove(product); // ProductDetails/ProductPrices cascade-delete with the product
            await _db.SaveChangesAsync();

            DeleteProductImageFile(imagePathToDelete);

            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // PUT api/products/5/details — upsert the single ProductDetails row (1:1).
        [HttpPut("{id:int}/details")]
        public async Task<ActionResult<ProductDetailsReadDto>> UpsertDetails(int id, [FromBody] ProductDetailsWriteDto dto)
        {
            var product = await _db.Products.Include(p => p.ProductDetails).FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return NotFoundResponse($"Product {id} not found.");

            var details = product.ProductDetails;
            if (details is null)
            {
                details = MapDetails(dto);
                product.ProductDetails = details;
            }
            else
            {
                ApplyDetails(details, dto);
            }

            await _db.SaveChangesAsync();
            return Ok(MapDetailsToReadDto(details));
        }

        // DELETE api/products/5/details — remove the details row.
        [HttpDelete("{id:int}/details")]
        public async Task<IActionResult> DeleteDetails(int id)
        {
            var product = await _db.Products.Include(p => p.ProductDetails).FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return NotFoundResponse($"Product {id} not found.");

            var details = product.ProductDetails;
            if (details is null) return NotFoundResponse($"Product {id} has no details row.");

            _db.ProductDetails.Remove(details);
            product.ProductDetails = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/products/5/prices — add one per-unit price.
        [HttpPost("{id:int}/prices")]
        public async Task<ActionResult<ProductPriceReadDto>> AddPrice(int id, [FromBody] ProductPriceWriteDto dto)
        {
            var product = await _db.Products.Include(p => p.ProductPrices).FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return NotFoundResponse($"Product {id} not found.");

            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequestResponse($"Unit {dto.UnitId} does not exist.");

            var displayName = (dto.DisplayName ?? string.Empty).Trim();
            if (product.ProductPrices.Any(pp => pp.UnitId == dto.UnitId && pp.DisplayName == displayName))
                return ConflictResponse($"Product {id} already has a packaging with that name.");

            var price = new ProductPrice
            {
                ProductId = id,
                UnitId = dto.UnitId,
                DisplayName = displayName,
                PerUnitPrice = dto.PerUnitPrice,
                BaseQuantity = dto.BaseQuantity,
                PurchasePrice = dto.PurchasePrice,
                SalePrice = dto.SalePrice,
                DistributorPrice = dto.DistributorPrice
            };
            _db.ProductPrices.Add(price);
            AddHistoryIfChanged(product, "Packaging", dto.UnitId, null, dto.PerUnitPrice, null, dto.BaseQuantity);
            await _db.SaveChangesAsync();

            await _db.Entry(price).Reference(pp => pp.Unit).LoadAsync();
            return CreatedAtAction(nameof(GetById), new { id }, MapPriceToReadDto(price));
        }

        // PUT api/products/5/prices/9 — update one price row.
        [HttpPut("{id:int}/prices/{priceId:int}")]
        public async Task<IActionResult> UpdatePrice(int id, int priceId, [FromBody] ProductPriceWriteDto dto)
        {
            var price = await _db.ProductPrices.FirstOrDefaultAsync(pp => pp.Id == priceId && pp.ProductId == id);
            if (price is null) return NotFoundResponse($"Price {priceId} not found for product {id}.");

            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequestResponse($"Unit {dto.UnitId} does not exist.");

            var displayName = (dto.DisplayName ?? string.Empty).Trim();
            if (await _db.ProductPrices.AnyAsync(pp => pp.ProductId == id && pp.UnitId == dto.UnitId && pp.DisplayName == displayName && pp.Id != priceId))
                return ConflictResponse($"Product {id} already has a packaging with that name.");

            AddHistoryIfChanged(new Product { Id = id }, "Packaging", price.UnitId, price.PerUnitPrice, dto.PerUnitPrice, price.BaseQuantity, dto.BaseQuantity);
            price.UnitId = dto.UnitId;
            price.DisplayName = displayName;
            price.PerUnitPrice = dto.PerUnitPrice;
            price.BaseQuantity = dto.BaseQuantity;
            price.PurchasePrice = dto.PurchasePrice;
            price.SalePrice = dto.SalePrice;
            price.DistributorPrice = dto.DistributorPrice;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/products/5/prices/9 — remove one price row.
        [HttpDelete("{id:int}/prices/{priceId:int}")]
        public async Task<IActionResult> DeletePrice(int id, int priceId)
        {
            var price = await _db.ProductPrices.FirstOrDefaultAsync(pp => pp.Id == priceId && pp.ProductId == id);
            if (price is null) return NotFoundResponse($"Price {priceId} not found for product {id}.");

            _db.ProductPrices.Remove(price);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/products/5/image — upload/replace the product image as an
        // actual file (multipart/form-data), instead of hand-encoding base64
        // into the JSON body via PUT. In Postman: Body -> form-data -> key
        // "file", type "File" -> pick an image from disk.
        //
        // Saves the file physically under wwwroot/images/products (creating
        // that folder the first time it's needed) and stores the public path
        // in Product.ImagePath, e.g. "/images/products/12_3f9a...c1.jpg",
        // so it can be loaded straight from <img src="https://your-host/images/products/12_3f9a...c1.jpg">
        // once app.UseStaticFiles() is serving wwwroot. The raw bytes are
        // still kept on ProductImage too, for callers that prefer that.
        [HttpPost("{id:int}/image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImage(int id, IFormFile file)
        {
            var product = await _db.Products.FindAsync(id);
            if (product is null) return NotFoundResponse($"Product {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            // Windows/image tools commonly save JPEGs as .jfif. It is still
            // JPEG image data, so normalize its saved filename to .jpg rather
            // than rejecting a photo the browser correctly identifies as an
            // image.
            if (string.Equals(extension, ".jfif", StringComparison.OrdinalIgnoreCase))
                extension = ".jpg";

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            bool isAllowed = false;
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            if (!isAllowed)
                return BadRequestResponse("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp.");

            // Read the bytes once and reuse them for both the DB column and the file on disk.
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            string folderPath = GetProductImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteProductImageFile(product.ImagePath);

            product.ProductImage = fileBytes;
            product.ProductImageContentType = file.ContentType;
            product.ImagePath = "/" + ProductImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { product.Id, product.ImagePath });
        }

        // DELETE api/products/5/image — remove the product's image, both the
        // physical file under wwwroot/images/products and the DB fields.
        [HttpDelete("{id:int}/image")]
        public async Task<IActionResult> DeleteImage(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product is null) return NotFoundResponse($"Product {id} not found.");

            DeleteProductImageFile(product.ImagePath);

            product.ImagePath = null;
            product.ProductImage = null;
            product.ProductImageContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("filter-options")]
        public async Task<ActionResult<ProductFilterOptionsDto>> GetFilterOptions([FromQuery] int? companyId)
        {
            var query = _db.Products.AsNoTracking().Where(p => p.IsActive != false).AsQueryable();
            if (companyId is not null) query = query.Where(p => p.CompanyId == companyId);

            var dosageForms = await query
                .Where(p => p.DosageFormId != null)
                .Select(p => new { Id = p.DosageFormId!.Value, Name = p.DosageForm!.Name })
                .Distinct()
                .ToListAsync();

            var genericNames = await query
                .Where(p => !string.IsNullOrEmpty(p.GenericName))
                .Select(p => p.GenericName!)
                .Distinct()
                .ToListAsync();

            return Ok(new ProductFilterOptionsDto
            {
                DosageForms = dosageForms
                    .Select(d => new DosageFormOptionDto { Id = d.Id, Name = d.Name })
                    .OrderBy(d => d.Name).ToList(),
                GenericNames = genericNames.OrderBy(n => n).ToList()
            });
        }

        // GET api/products/by-barcode/8901234500017 — barcode scanner দিয়ে scan
        // করার পর সরাসরি product খুঁজে বের করার জন্য (Purchase page-এর
        // "barode scanner or manually type" requirement)। না পেলে 404, তখন
        // frontend manual entry ফর্ম দেখাবে ধরে নেওয়া নতুন product হিসেবে।
        [HttpGet("by-barcode/{barcode}")]
        public async Task<ActionResult<ProductListItemDto>> GetByBarcode(string barcode)
        {
            var product = await _db.Products.AsNoTracking()
                .Include(p => p.Unit).Include(p => p.Company).Include(p => p.DosageForm)
                .Include(p => p.ProductDetails).Include(p => p.ProductPrices).ThenInclude(pp => pp.Unit)
                .FirstOrDefaultAsync(p => p.Barcode == barcode);

            if (product is null) return NotFoundResponse($"No product found with barcode '{barcode}'.");

            return Ok(new ProductListItemDto
            {
                Id = product.Id,
                ProductCode = product.ProductCode,
                ProductName = product.ProductName,
                Strength = product.Strength,
                GenericName = product.GenericName,
                BrandType = product.BrandType,
                CompanyId = product.CompanyId,
                CompanyName = product.Company?.Name,
                DosageFormId = product.DosageFormId,
                DosageFormName = product.DosageForm?.Name,
                UnitId = product.UnitId,
                UnitName = product.Unit.Name,
                UnitPrice = product.UnitPrice,
                PurchasePrice = product.PurchasePrice,
                SalePrice = product.SalePrice,
                StockQuantity = product.StockQuantity,
                ImagePath = product.ImagePath,
                Manufacturer = product.ProductDetails?.Manufacturer,
                RequiresPrescription = product.ProductDetails?.RequiresPrescription,
                IsControlledDrug = product.ProductDetails?.IsControlledDrug,
                Prices = product.ProductPrices.Select(MapPriceToReadDto).ToList()
            });
        }
        // ---------- helpers ----------

        // Deletes the physical file behind an ImagePath like
        // "/images/products/12_3f9a...c1.jpg", if it exists. Safe to call
        // with null/empty/unrelated paths — it just does nothing then.
        private void DeleteProductImageFile(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return;

            if (imagePath.IndexOf(ProductImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(imagePath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetProductImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private async Task<Product?> LoadProductAsync(int id, bool tracking)
        {
            var query = _db.Products
                .Include(p => p.Unit)
                .Include(p => p.Company)
                .Include(p => p.DosageForm)
                .Include(p => p.ProductVariant).ThenInclude(v => v!.ProductGroup)
                .Include(p => p.ProductDetails)
                .Include(p => p.ProductPrices).ThenInclude(pp => pp.Unit)
                .AsQueryable();

            if (!tracking) query = query.AsNoTracking();

            return await query.FirstOrDefaultAsync(p => p.Id == id);
        }

        private static ProductDetails MapDetails(ProductDetailsWriteDto dto) => new()
        {
            Manufacturer = dto.Manufacturer,
            Schedule = dto.Schedule,
            DarNo = dto.DarNo,
            StorageConditions = dto.StorageConditions,
            TemperatureMin = dto.TemperatureMin,
            TemperatureMax = dto.TemperatureMax,
            SideEffects = dto.SideEffects,
            PregnancyCategory = dto.PregnancyCategory,
            RequiresPrescription = dto.RequiresPrescription,
            IsControlledDrug = dto.IsControlledDrug
        };

        private static void ApplyDetails(ProductDetails details, ProductDetailsWriteDto dto)
        {
            details.Manufacturer = dto.Manufacturer;
            details.Schedule = dto.Schedule;
            details.DarNo = dto.DarNo;
            details.StorageConditions = dto.StorageConditions;
            details.TemperatureMin = dto.TemperatureMin;
            details.TemperatureMax = dto.TemperatureMax;
            details.SideEffects = dto.SideEffects;
            details.PregnancyCategory = dto.PregnancyCategory;
            details.RequiresPrescription = dto.RequiresPrescription;
            details.IsControlledDrug = dto.IsControlledDrug;
        }

        private static ProductDetailsReadDto MapDetailsToReadDto(ProductDetails d) => new()
        {
            Manufacturer = d.Manufacturer,
            Schedule = d.Schedule,
            DarNo = d.DarNo,
            StorageConditions = d.StorageConditions,
            TemperatureMin = d.TemperatureMin,
            TemperatureMax = d.TemperatureMax,
            SideEffects = d.SideEffects,
            PregnancyCategory = d.PregnancyCategory,
            RequiresPrescription = d.RequiresPrescription,
            IsControlledDrug = d.IsControlledDrug
        };

        private static ProductPriceReadDto MapPriceToReadDto(ProductPrice pp) => new()
        {
            Id = pp.Id,
            UnitId = pp.UnitId,
            UnitName = pp.Unit?.Name ?? string.Empty,
            DisplayName = pp.DisplayName,
            PerUnitPrice = pp.PerUnitPrice,
            BaseQuantity = pp.BaseQuantity,
            PurchasePrice = pp.PurchasePrice,
            SalePrice = pp.SalePrice,
            DistributorPrice = pp.DistributorPrice
        };

        private void AddHistoryIfChanged(Product product, string type, int? unitId, decimal? previousPrice, decimal? newPrice, decimal? previousBaseQuantity = null, decimal? newBaseQuantity = null)
        {
            if (previousPrice == newPrice && previousBaseQuantity == newBaseQuantity) return;
            _db.ProductPriceHistories.Add(new ProductPriceHistory
            {
                ProductId = product.Id,
                PriceType = type,
                UnitId = unitId,
                PreviousPrice = previousPrice,
                NewPrice = newPrice ?? 0,
                PreviousBaseQuantity = previousBaseQuantity,
                NewBaseQuantity = newBaseQuantity
            });
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));

        private static ProductReadDto MapToReadDto(Product product)
        {
            return new ProductReadDto
            {
                Id = product.Id,
                ProductCode = product.ProductCode,
                ProductName = product.ProductName,
                Strength = product.Strength,
                GenericName = product.GenericName,
                Barcode = product.Barcode,
                BrandType = product.BrandType,
                CompanyId = product.CompanyId,
                CompanyName = product.Company?.Name,
                DosageFormId = product.DosageFormId,
                DosageFormName = product.DosageForm?.Name,
                ProductVariantId = product.ProductVariantId,
                ProductGroupId = product.ProductVariant?.ProductGroupId,
                ProductGroupName = product.ProductVariant?.ProductGroup?.Name,
                UnitId = product.UnitId,
                UnitName = product.Unit?.Name ?? string.Empty,
                UnitPrice = product.UnitPrice,
                PurchasePrice = product.PurchasePrice,
                DistributorPrice = product.DistributorPrice,
                SalePrice = product.SalePrice,
                ImagePath = product.ImagePath,
                ProductImageContentType = product.ProductImageContentType,
                RegisteredDate = product.RegisteredDate,
                IsActive = product.IsActive,
                StockQuantity = product.StockQuantity,
                MinStockQty = product.MinStockQty,
                MaxStockQty = product.MaxStockQty,
                PurchaseQty = product.PurchaseQty,
                Details = product.ProductDetails is null ? null : MapDetailsToReadDto(product.ProductDetails),
                Prices = product.ProductPrices.Select(MapPriceToReadDto).ToList()
            };
        }
    }
}
```

### `Productstockscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ProductStocksController : ControllerBase
{
    private readonly PharmacyDbContext _db;
    public ProductStocksController(PharmacyDbContext db) => _db = db;

    // Batches are always stored in the product's base unit. Packagings only
    // describe conversion/display and never create a second stock balance.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductStockListItemDto>>> GetAll(
    [FromQuery] string? search,
    [FromQuery] int? warehouseId,
    [FromQuery] int? companyId,
    [FromQuery] int? dosageFormId,
    [FromQuery] string? genericName)
    {
        var query = _db.ProductStocks.AsNoTracking().Include(s => s.Product).ThenInclude(p => p.Unit)
            .Include(s => s.Product).ThenInclude(p => p.Company)
            .Include(s => s.Product).ThenInclude(p => p.DosageForm)
            .Include(s => s.Product).ThenInclude(p => p.ProductDetails)
            .Include(s => s.Product).ThenInclude(p => p.ProductPrices).ThenInclude(pp => pp.Unit)
            .Include(s => s.Warehouse).Include(s => s.Supplier)
            .Where(s => s.AvailableQuantity > 0 && s.ExpiryDate > DateOnly.FromDateTime(DateTime.Today)).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => s.Product.ProductName.Contains(search) || s.BatchNumber.Contains(search));
        if (warehouseId.HasValue) query = query.Where(s => s.WarehouseId == warehouseId.Value);
        if (companyId.HasValue) query = query.Where(s => s.Product.CompanyId == companyId.Value);
        if (dosageFormId.HasValue) query = query.Where(s => s.Product.DosageFormId == dosageFormId.Value);
        if (!string.IsNullOrWhiteSpace(genericName)) query = query.Where(s => s.Product.GenericName == genericName);
        var stocks = await query.OrderBy(s => s.Product.ProductName).ThenBy(s => s.BatchNumber).ToListAsync();

        // Total units ever sold per Product, so the Sale form's Alternate
        // Brands list can offer a "Popularity" sort alongside Name/Price.
        var productIds = stocks.Select(s => s.ProductId).Distinct().ToList();
        var soldTotals = await _db.SaleItems.AsNoTracking()
            .Where(si => productIds.Contains(si.ProductStocks.ProductId))
            .GroupBy(si => si.ProductStocks.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToListAsync();
        var soldMap = soldTotals.ToDictionary(x => x.ProductId, x => x.Total);

        return Ok(stocks.Select(s =>
        {
            var dto = MapToListItem(s);
            dto.PopularityScore = soldMap.GetValueOrDefault(s.ProductId);
            return dto;
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductStockListItemDto>> GetById(int id)
    {
        var stock = await LoadStockAsync(id, tracking: false);
        if (stock is null) return NotFoundResponse($"Product stock batch {id} not found.");
        return Ok(MapToListItem(stock));
    }

    [HttpPost]
    public async Task<ActionResult<ProductStockListItemDto>> Create([FromBody] ProductStockWriteDto dto)
    {
        var validation = await ValidateWriteDtoAsync(dto, null);
        if (validation is not null) return validation;

        var availableQuantity = dto.AvailableQuantity ?? dto.Quantity;
        if (availableQuantity > dto.Quantity)
            return BadRequestResponse("Available quantity cannot be greater than received quantity.");

        var stock = new ProductStock
        {
            ProductId = dto.ProductId,
            WarehouseId = dto.WarehouseId,
            ProductRakId = dto.ProductRakId,
            BatchNumber = dto.BatchNumber.Trim(),
            Quantity = dto.Quantity,
            AvailableQuantity = availableQuantity,
            ExpiryDate = dto.ExpiryDate,
            ReceivedDate = dto.ReceivedDate ?? DateOnly.FromDateTime(DateTime.Today),
            UnitCost = dto.UnitCost,
            SupplierId = dto.SupplierId
        };

        _db.ProductStocks.Add(stock);
        await _db.SaveChangesAsync();
        await RecalculateStockQuantityAsync(new[] { stock.ProductId });

        var saved = await LoadStockAsync(stock.Id, tracking: false);
        return CreatedAtAction(nameof(GetById), new { id = stock.Id }, MapToListItem(saved!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductStockWriteDto dto)
    {
        var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == id);
        if (stock is null) return NotFoundResponse($"Product stock batch {id} not found.");

        var oldProductId = stock.ProductId;
        var validation = await ValidateWriteDtoAsync(dto, id);
        if (validation is not null) return validation;

        var availableQuantity = dto.AvailableQuantity ?? dto.Quantity;
        if (availableQuantity > dto.Quantity)
            return BadRequestResponse("Available quantity cannot be greater than received quantity.");

        stock.ProductId = dto.ProductId;
        stock.WarehouseId = dto.WarehouseId;
        stock.ProductRakId = dto.ProductRakId;
        stock.BatchNumber = dto.BatchNumber.Trim();
        stock.Quantity = dto.Quantity;
        stock.AvailableQuantity = availableQuantity;
        stock.ExpiryDate = dto.ExpiryDate;
        stock.ReceivedDate = dto.ReceivedDate ?? stock.ReceivedDate;
        stock.UnitCost = dto.UnitCost;
        stock.SupplierId = dto.SupplierId;

        await _db.SaveChangesAsync();
        await RecalculateStockQuantityAsync(new[] { oldProductId, stock.ProductId });
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == id);
        if (stock is null) return NotFoundResponse($"Product stock batch {id} not found.");

        if (await _db.SaleItems.AnyAsync(i => i.ProductStockId == id))
            return ConflictResponse("Cannot delete this stock batch because sales already used it. Set the available quantity to 0 instead.");
        if (await _db.ExpiredProductStocks.AnyAsync(e => e.ProductStockId == id))
            return ConflictResponse("Cannot delete this stock batch because disposal records exist for it.");
        if (await _db.StockTransferItems.AnyAsync(i => i.SourceProductStockId == id))
            return ConflictResponse("Cannot delete this stock batch because stock transfer records exist for it.");

        var productId = stock.ProductId;
        _db.ProductStocks.Remove(stock);
        await _db.SaveChangesAsync();
        await RecalculateStockQuantityAsync(new[] { productId });
        return NoContent();
    }

    [HttpGet("expired")]
    public async Task<ActionResult<IEnumerable<ProductStockListItemDto>>> GetExpired()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var stocks = await _db.ProductStocks.AsNoTracking().Include(s => s.Product).ThenInclude(p => p.Unit)
            .Include(s => s.Warehouse).Where(s => s.AvailableQuantity > 0 && s.ExpiryDate <= today).OrderBy(s => s.ExpiryDate).ToListAsync();
        return Ok(stocks.Select(MapToListItem));
    }

    private async Task<ActionResult?> ValidateWriteDtoAsync(ProductStockWriteDto dto, int? stockId)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
            return BadRequestResponse($"Product {dto.ProductId} does not exist.");
        if (!await _db.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId))
            return BadRequestResponse($"Warehouse {dto.WarehouseId} does not exist.");
        if (dto.SupplierId.HasValue && !await _db.Suppliers.AnyAsync(s => s.SupplierId == dto.SupplierId.Value))
            return BadRequestResponse($"Supplier {dto.SupplierId.Value} does not exist.");
        if (dto.ProductRakId.HasValue && !await _db.ProductRaks.AnyAsync(r => r.Id == dto.ProductRakId.Value && r.WarehouseId == dto.WarehouseId))
            return BadRequestResponse($"Rack {dto.ProductRakId.Value} does not exist in warehouse {dto.WarehouseId}.");
        if (await _db.ProductStocks.AnyAsync(s => s.ProductId == dto.ProductId && s.BatchNumber == dto.BatchNumber.Trim() && (!stockId.HasValue || s.Id != stockId.Value)))
            return ConflictResponse($"Batch '{dto.BatchNumber}' already exists for this product.");
        return null;
    }

    private async Task<ProductStock?> LoadStockAsync(int id, bool tracking)
    {
        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p.Unit)
            .Include(s => s.Product).ThenInclude(p => p.Company)
            .Include(s => s.Product).ThenInclude(p => p.DosageForm)
            .Include(s => s.Product).ThenInclude(p => p.ProductPrices).ThenInclude(pp => pp.Unit)
            .Include(s => s.Warehouse)
            .AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.Id == id);
    }

    private static ProductStockListItemDto MapToListItem(ProductStock s) => new()
    {
        Id = s.Id,
        ProductId = s.ProductId,
        ProductName = s.Product.ProductName,
        ProductCode = s.Product.ProductCode,
        ProductImagePath = s.Product.ImagePath,
        GenericName = s.Product.GenericName,
        Strength = s.Product.Strength,
        BrandType = s.Product.BrandType,
        CompanyId = s.Product.CompanyId,
        CompanyName = s.Product.Company?.Name,
        DosageFormId = s.Product.DosageFormId,
        DosageFormName = s.Product.DosageForm?.Name,
        SupplierId = s.SupplierId,
        SupplierName = s.Supplier?.SupplierName,
        RequiresPrescription = s.Product.ProductDetails?.RequiresPrescription ?? false,
        UnitId = s.Product.UnitId,
        UnitName = s.Product.Unit.Name,
        WarehouseId = s.WarehouseId,
        WarehouseName = s.Warehouse.Name,
        BatchNumber = s.BatchNumber,
        Quantity = s.Quantity,
        AvailableQuantity = s.AvailableQuantity,
        ExpiryDate = s.ExpiryDate,
        ReceivedDate = s.ReceivedDate,
        UnitCost = s.UnitCost,
        SalePrice = s.Product.SalePrice ?? s.Product.UnitPrice,
        Packagings = s.Product.ProductPrices.Select(p => new ProductPriceReadDto
        {
            Id = p.Id,
            UnitId = p.UnitId,
            UnitName = p.Unit.Name,
            DisplayName = p.DisplayName,
            PerUnitPrice = p.PerUnitPrice,
            BaseQuantity = p.BaseQuantity
        }).ToList()
    };

    private async Task RecalculateStockQuantityAsync(IEnumerable<int> productIds)
    {
        foreach (var productId in productIds.Distinct())
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
            if (product is null) continue;

            var total = await _db.ProductStocks
                .Where(s => s.ProductId == productId)
                .SumAsync(s => (decimal?)s.AvailableQuantity) ?? 0;
            product.StockQuantity = (int)total;
        }
        await _db.SaveChangesAsync();
    }

    private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
    private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
    private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
}
```

### `PurchaseInvoicesController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Services;
using PharmacyV2.Enums;

namespace PharmacyV2.Controllers
{
    // The actual recorded purchase (goods received + supplier invoice), as
    // opposed to PurchaseOrder which is the request that preceded it.
    // Master-detail pair: PurchaseInvoice -> PurchaseInvoiceItem.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PurchaseInvoicesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IAccountingPostingService _accounting;
        private readonly ICodeGeneratorService _codeGenerator;

        // Folder (under wwwroot) where receipt images physically live, and
        // the matching public URL prefix used to build ReceiptImagePath.
        // Same pattern as SalesController.SaleReceiptImagesRelativeFolder /
        // EmployeesController.EmployeeImagesRelativeFolder.
        private const string PurchaseReceiptImagesRelativeFolder = "images/purchase-receipts";

        public PurchaseInvoicesController(PharmacyDbContext db, IWebHostEnvironment env, IAccountingPostingService accounting, ICodeGeneratorService codeGenerator)
        {
            _db = db;
            _env = env;
            _accounting = accounting;
            _codeGenerator = codeGenerator;
        }

        // wwwroot may not exist yet on a fresh clone. Creates
        // "wwwroot/images/purchase-receipts" on demand the first time it's needed.
        private string GetPurchaseReceiptImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "purchase-receipts");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/purchaseinvoices?supplierId=3 — list invoices, optionally by supplier.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PurchaseInvoiceReadDto>>> GetAll([FromQuery] int? supplierId)
        {
            var query = _db.PurchaseInvoices
                .Include(p => p.Supplier)
                .Include(p => p.Warehouse)
                .Include(p => p.Items).ThenInclude(i => i.Product)
                .Include(p => p.Items).ThenInclude(i => i.Unit)
                .AsNoTracking().AsQueryable();
            if (supplierId is not null) query = query.Where(p => p.SupplierId == supplierId);

            // Multiple invoices can share the same purchase date. Break that
            // tie by the database ID so the invoice saved most recently is
            // always first instead of appearing in an arbitrary middle row.
            var invoices = await query
                .OrderByDescending(p => p.PurchaseDate)
                .ThenByDescending(p => p.Id)
                .ToListAsync();
            return Ok(invoices.Select(ToDto));
        }

        // GET api/purchaseinvoices/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PurchaseInvoiceReadDto>> GetById(int id)
        {
            var invoice = await _db.PurchaseInvoices
                .Include(p => p.Supplier)
                .Include(p => p.Warehouse)
                .Include(p => p.Items).ThenInclude(i => i.Product)
                .Include(p => p.Items).ThenInclude(i => i.Unit)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
            return invoice is null ? NotFound(new ApiError($"Purchase invoice {id} not found.")) : Ok(ToDto(invoice));
        }

        // POST api/purchaseinvoices — record a new purchase, header + line
        // items in one call (same convention as PurchaseOrders). InvoiceNo
        // must be unique; Supplier, Warehouse and each item's Product/Unit
        // must already exist. Due is computed as Total - Advance - Discount +
        // TaxOrOthers if the caller doesn't set it explicitly. Each item's
        // SubTotal is always computed server-side as Quantity * UnitCost.
        [HttpPost]
        public async Task<ActionResult<PurchaseInvoiceReadDto>> Create([FromBody] PurchaseInvoiceCreateDto dto)
        {
            // InvoiceNo is always system-generated (GUID-backed, see
            // ICodeGeneratorService) — any InvoiceNo the client sends is
            // ignored on purpose, so there's nothing left to conflict-check here.
            var generatedInvoiceNo = await _codeGenerator.GeneratePurchaseInvoiceNoAsync();

            if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == dto.SupplierId))
                return BadRequest(new ApiError($"Supplier {dto.SupplierId} does not exist."));

            if (!await _db.Warehouses.AnyAsync(w => w.Id == dto.WarehouseId))
                return BadRequest(new ApiError($"Warehouse {dto.WarehouseId} does not exist."));

            if (dto.PurchaseOrderId is not null && !await _db.PurchaseOrders.AnyAsync(o => o.Id == dto.PurchaseOrderId))
                return BadRequest(new ApiError($"Purchase order {dto.PurchaseOrderId} does not exist."));

            // ProductStock has a DB-level UNIQUE index on (ProductId, BatchNumber) —
            // a batch number must be one-of-a-kind for a given product, full stop,
            // regardless of which invoice it's attached to. Two guards, same as
            // the Update() item-replace path above:
            //   1) two lines in THIS SAME payload resolving to the same
            //      (ProductId, BatchNumber) — would collide with each other.
            //   2) a line's (ProductId, BatchNumber) already used by a batch
            //      from ANY other invoice (or seed data) already in the DB.
            // Both are caught here with a clean 409/400 instead of letting
            // SyncStockBatchForItemAsync's insert hit the DB constraint and
            // blow up as an unhandled DbUpdateException later.
            if (dto.Items is { Count: > 0 })
            {
                var seenKeys = new HashSet<(int ProductId, string BatchNumber)>();
                foreach (var itemDto in dto.Items.Where(i => !string.IsNullOrWhiteSpace(i.BatchNumber)))
                {
                    var key = (itemDto.ProductId, itemDto.BatchNumber!);
                    if (!seenKeys.Add(key))
                        return BadRequest(new ApiError(
                            $"Duplicate line items for product {itemDto.ProductId} with batch number '{itemDto.BatchNumber}' — a batch number must be unique per product."));

                    if (await _db.ProductStocks.AnyAsync(s => s.ProductId == itemDto.ProductId && s.BatchNumber == itemDto.BatchNumber))
                        return Conflict(new ApiError(
                            $"Batch number '{itemDto.BatchNumber}' is already in use for product {itemDto.ProductId}. " +
                            "Each batch number must be unique per product — use a different batch number, or omit it to have one generated automatically."));
                }
            }

            // Everything below writes across three related tables (invoice+items,
            // ProductStock batches, Product roll-up totals) via several separate
            // SaveChangesAsync calls. Wrapped in one transaction so that if
            // anything past the header/items save still fails, the invoice+items
            // don't get left behind as an orphan row with no stock behind it.
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var invoice = new PurchaseInvoice
            {
                InvoiceNo = generatedInvoiceNo,
                PurchaseDate = dto.PurchaseDate,
                SupplierId = dto.SupplierId,
                PurchaseOrderId = dto.PurchaseOrderId,
                WarehouseId = dto.WarehouseId,
                PaymentMethod = dto.PaymentMethod,
                Advance = dto.Advance,
                Total = dto.Total,
                Discount = dto.Discount,
                TaxOrOthers = dto.TaxOrOthers,
                IsSupplierWise = dto.IsSupplierWise,
                CreatedAt = DateTime.UtcNow
            };

            if (dto.Items is { Count: > 0 })
            {
                foreach (var itemDto in dto.Items)
                {
                    if (!await _db.Products.AnyAsync(p => p.Id == itemDto.ProductId))
                        return BadRequest(new ApiError($"Product {itemDto.ProductId} does not exist."));
                    if (!await _db.Units.AnyAsync(u => u.Id == itemDto.UnitId))
                        return BadRequest(new ApiError($"Unit {itemDto.UnitId} does not exist."));

                    invoice.Items.Add(new PurchaseInvoiceItem
                    {
                        ProductId = itemDto.ProductId,
                        UnitId = itemDto.UnitId,
                        Quantity = itemDto.Quantity,
                        UnitCost = itemDto.UnitCost,
                        SubTotal = itemDto.Quantity * itemDto.UnitCost,
                        // BatchNumber is GUID-generated when the caller
                        // doesn't supply one, instead of the old
                        // "{InvoiceNo}-{item.Id}" placeholder — manually
                        // typing the manufacturer's own batch/lot number is
                        // still supported by simply sending BatchNumber.
                        BatchNumber = string.IsNullOrWhiteSpace(itemDto.BatchNumber)
                            ? await _codeGenerator.GenerateBatchNumberAsync()
                            : itemDto.BatchNumber,
                        ExpiryDate = itemDto.ExpiryDate
                    });
                }
            }

            invoice.Total = invoice.Items.Count > 0
                ? invoice.Items.Sum(i => i.SubTotal) - invoice.Discount + invoice.TaxOrOthers
                : invoice.Total - invoice.Discount + invoice.TaxOrOthers;
            invoice.Due = Math.Max(0, invoice.Total - invoice.Advance);
            invoice.PaymentStatus = invoice.Due <= 0 ? "Complete" : "InComplete";

            _db.PurchaseInvoices.Add(invoice);
            await _db.SaveChangesAsync();

            var affectedProductIds = invoice.Items.Select(i => i.ProductId).ToList();

            // Every line item on a posted invoice is real purchase history —
            // make sure the Supplier<->Product link exists and its per-unit
            // purchase price is current, instead of leaving that relationship
            // to be maintained by hand elsewhere.
            foreach (var item in invoice.Items)
                await UpsertSupplierProductFromInvoiceItemAsync(invoice.SupplierId, item, invoice.PurchaseDate);
            await _db.SaveChangesAsync();

            // Items are now persisted, so this query can see them —
            // recalc runs after the fact and needs a second SaveChangesAsync.
            await RecalculatePurchaseQtyAsync(affectedProductIds);

            // This is the actual "goods receipt" moment — each item becomes a
            // real, sellable ProductStock batch. Without this, SalesController
            // has nothing to sell against except seeded data.
            foreach (var item in invoice.Items)
                await SyncStockBatchForItemAsync(item, invoice);
            await _db.SaveChangesAsync();

            // Now that the batches exist, roll them up into Product.StockQuantity.
            await RecalculateStockQuantityAsync(affectedProductIds);
            await _db.SaveChangesAsync();

            var paidCode = invoice.PaymentMethod == "Card" ? "1010" : invoice.PaymentMethod == "Mobile Banking" ? "1020" : "1000";
            var payable = Math.Max(0, invoice.Due);
            var paid = invoice.Total - payable;
            await _accounting.PostAsync(LedgerSourceType.Purchase, invoice.Id, invoice.PurchaseDate, $"Purchase {invoice.InvoiceNo}",
                ("1200", invoice.Total, 0), (paidCode, 0, paid), ("2000", 0, payable));
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _db.Entry(invoice).Reference(i => i.Supplier).LoadAsync();
            await _db.Entry(invoice).Reference(i => i.Warehouse).LoadAsync();
            await _db.Entry(invoice).Collection(i => i.Items).Query()
                .Include(i => i.Product).Include(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, ToDto(invoice));
        }

        // PUT api/purchaseinvoices/5 — update header fields (amounts/payment method),
        // and OPTIONALLY the full item set in the same call.
        // NOTE: "Due" is no longer taken from the request body — it's always
        // derived server-side (Total - Advance - payments already allocated
        // via SupplierPayments), same as the /items endpoints, so a stray
        // client-supplied Due can never drift from what's actually been paid.
        //
        // If dto.Items is provided, it's treated as the desired final item
        // set for this invoice (full replace, matched by item Id):
        //   - incoming item with no Id / Id not found on this invoice -> added
        //   - incoming item whose Id matches an existing item          -> updated
        //   - existing item whose Id is missing from the incoming list -> removed
        // Same safety rule as RemoveItem: a removal is blocked if any of that
        // batch has already been sold/transferred out. If dto.Items is null
        // (omitted), items are left untouched — old behavior.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PurchaseInvoiceUpdateDto dto)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.Purchase && l.SourceId == id))
                return Conflict(new ApiError("Posted purchase invoices are immutable. Use a purchase return or a correcting invoice."));
            var existing = await _db.PurchaseInvoices.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (existing is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            existing.Advance = dto.Advance;
            existing.Discount = dto.Discount;
            existing.TaxOrOthers = dto.TaxOrOthers;
            existing.PaymentMethod = dto.PaymentMethod;

            var affectedProductIds = new HashSet<int>();

            if (dto.Items is not null)
            {
                // Validate every referenced product/unit up front, before mutating anything.
                foreach (var itemDto in dto.Items)
                {
                    if (!await _db.Products.AnyAsync(p => p.Id == itemDto.ProductId))
                        return BadRequest(new ApiError($"Product {itemDto.ProductId} does not exist."));
                    if (!await _db.Units.AnyAsync(u => u.Id == itemDto.UnitId))
                        return BadRequest(new ApiError($"Unit {itemDto.UnitId} does not exist."));
                }

                var incomingIds = dto.Items.Where(i => i.Id is > 0).Select(i => i.Id!.Value).ToHashSet();

                // Guard against two lines in the SAME payload resolving to the
                // same (ProductId, BatchNumber) — that would violate the
                // ProductStock unique index no matter what save-ordering we do,
                // since both would be live rows at the same time. Catch it here
                // with a clear error instead of a raw DbUpdateException.
                var resolvedKeys = new HashSet<(int ProductId, string BatchNumber)>();
                foreach (var itemDto in dto.Items)
                {
                    var existingMatch = itemDto.Id is > 0
                        ? existing.Items.FirstOrDefault(i => i.Id == itemDto.Id)
                        : null;
                    var effectiveBatch = string.IsNullOrWhiteSpace(itemDto.BatchNumber)
                        ? $"{existing.InvoiceNo}-{(existingMatch?.Id.ToString() ?? "new")}"
                        : itemDto.BatchNumber;
                    var key = (itemDto.ProductId, effectiveBatch);

                    if (!string.IsNullOrWhiteSpace(itemDto.BatchNumber) && !resolvedKeys.Add(key))
                    {
                        return BadRequest(new ApiError(
                            $"Duplicate line items for product {itemDto.ProductId} with batch number '{itemDto.BatchNumber}' — a batch number must be unique per product."));
                    }
                }

                // Items about to be removed in this same request (Id not present
                // in the incoming list) — computed early so the conflict check
                // below doesn't reject a batch number that's being freed up by
                // one of these removals.
                var plannedRemovalIds = existing.Items
                    .Where(i => !incomingIds.Contains(i.Id))
                    .Select(i => i.Id)
                    .ToHashSet();

                // Guard against a line's (ProductId, BatchNumber) colliding with
                // a ProductStock row that belongs to a DIFFERENT item that is
                // staying on this invoice untouched, or to another invoice
                // entirely — that's a genuine duplicate, not a remove/re-add
                // ordering issue, so it must be rejected up front.
                foreach (var itemDto in dto.Items.Where(i => !string.IsNullOrWhiteSpace(i.BatchNumber)))
                {
                    var conflicting = await _db.ProductStocks.FirstOrDefaultAsync(s =>
                        s.ProductId == itemDto.ProductId && s.BatchNumber == itemDto.BatchNumber);

                    if (conflicting is null) continue;

                    // Find which existing invoice item (if any) currently owns this stock row.
                    var owningItem = conflicting.PurchaseInvoiceId == id
                        ? existing.Items.FirstOrDefault(i =>
                            i.ProductId == conflicting.ProductId &&
                            ResolveBatchNumber(i, existing.InvoiceNo) == conflicting.BatchNumber)
                        : null;

                    // OK if it belongs to the very item we're updating (synced in
                    // place), or to an item that's being removed in this same request.
                    var isSelf = owningItem is not null && itemDto.Id is > 0 && owningItem.Id == itemDto.Id;
                    var isBeingRemoved = owningItem is not null && plannedRemovalIds.Contains(owningItem.Id);

                    if (!isSelf && !isBeingRemoved)
                    {
                        return Conflict(new ApiError(
                            $"Batch number '{itemDto.BatchNumber}' is already in use for product {itemDto.ProductId} (invoice {conflicting.PurchaseInvoiceId}). Batch numbers must be unique per product."));
                    }
                }

                // --- Removals: existing items whose Id is not in the incoming list ---
                var toRemove = existing.Items.Where(i => !incomingIds.Contains(i.Id)).ToList();
                foreach (var item in toRemove)
                {
                    var batchNumber = ResolveBatchNumber(item, existing.InvoiceNo);
                    var stock = await _db.ProductStocks.FirstOrDefaultAsync(s =>
                        s.ProductId == item.ProductId && s.BatchNumber == batchNumber && s.PurchaseInvoiceId == id);

                    if (stock is not null)
                    {
                        if (stock.AvailableQuantity < stock.Quantity)
                        {
                            return Conflict(new ApiError(
                                $"Cannot remove line item {item.Id} — {stock.Quantity - stock.AvailableQuantity} unit(s) from batch '{stock.BatchNumber}' have already been sold or transferred out."));
                        }
                        _db.ProductStocks.Remove(stock);
                    }

                    affectedProductIds.Add(item.ProductId);
                    _db.PurchaseInvoiceItems.Remove(item);
                    existing.Items.Remove(item);
                }

                // Commit removals now, BEFORE any additions/updates run. Without
                // this, a removed item and a new/updated item that happen to
                // share the same (ProductId, BatchNumber) — e.g. client re-sent
                // the same batch without its item Id — would have their DELETE
                // and INSERT queued in the same SaveChangesAsync call. EF Core
                // does not guarantee DELETE runs before INSERT there, so it can
                // violate the ProductStock unique index (ProductId, BatchNumber)
                // even though the net result is a single row per key.
                if (toRemove.Count > 0)
                    await _db.SaveChangesAsync();

                // --- Updates: incoming items that match an existing Id ---
                foreach (var itemDto in dto.Items.Where(i => i.Id is > 0))
                {
                    var item = existing.Items.FirstOrDefault(i => i.Id == itemDto.Id);
                    if (item is null) continue; // Id didn't belong to this invoice — treat as not found, skip.

                    var oldProductId = item.ProductId;
                    var oldBatchNumber = ResolveBatchNumber(item, existing.InvoiceNo);

                    item.ProductId = itemDto.ProductId;
                    item.UnitId = itemDto.UnitId;
                    item.Quantity = itemDto.Quantity;
                    item.UnitCost = itemDto.UnitCost;
                    item.SubTotal = itemDto.Quantity * itemDto.UnitCost;
                    // Omitting BatchNumber means "leave it as-is" — every
                    // item already has a GUID-generated batch number from
                    // creation time (see Create()/AddItem()), so there is
                    // nothing to auto-generate on update.
                    if (!string.IsNullOrWhiteSpace(itemDto.BatchNumber))
                        item.BatchNumber = itemDto.BatchNumber;
                    item.ExpiryDate = itemDto.ExpiryDate;

                    affectedProductIds.Add(oldProductId);
                    affectedProductIds.Add(item.ProductId);

                    await SyncStockBatchForItemAsync(item, existing, oldProductId, oldBatchNumber);
                }

                // --- Additions: incoming items with no Id (or Id not found above) ---
                var newItems = new List<PurchaseInvoiceItem>();
                foreach (var itemDto in dto.Items.Where(i => i.Id is null or 0))
                {
                    var newItem = new PurchaseInvoiceItem
                    {
                        ProductId = itemDto.ProductId,
                        UnitId = itemDto.UnitId,
                        Quantity = itemDto.Quantity,
                        UnitCost = itemDto.UnitCost,
                        SubTotal = itemDto.Quantity * itemDto.UnitCost,
                        BatchNumber = string.IsNullOrWhiteSpace(itemDto.BatchNumber)
                            ? await _codeGenerator.GenerateBatchNumberAsync()
                            : itemDto.BatchNumber,
                        ExpiryDate = itemDto.ExpiryDate
                    };
                    existing.Items.Add(newItem);
                    newItems.Add(newItem);
                    affectedProductIds.Add(newItem.ProductId);
                }

                // New items need an Id (for the batch-number fallback) before
                // their stock batch can be synced — same reasoning as Create/AddItem.
                if (newItems.Count > 0)
                {
                    await _db.SaveChangesAsync();
                    foreach (var newItem in newItems)
                        await SyncStockBatchForItemAsync(newItem, existing);
                }

                // Total is recomputed from the (now-final) item set, same formula as RecalculateTotalsAsync.
                existing.Total = existing.Items.Sum(i => i.SubTotal) - existing.Discount + existing.TaxOrOthers;
            }
            else
            {
                existing.Total = dto.Total;
            }

            var paidViaAllocations = await _db.SupplierPaymentDetails
                .Where(d => d.PurchaseInvoiceId == id && !d.SupplierPayment.IsCancelled)
                .SumAsync(d => (decimal?)d.PaidAmount) ?? 0m;
            existing.Due = Math.Max(0, existing.Total - existing.Advance - paidViaAllocations);
            existing.PaymentStatus = existing.Due <= 0 ? "Complete" : "InComplete";

            await _db.SaveChangesAsync();

            foreach (var item in existing.Items)
                await UpsertSupplierProductFromInvoiceItemAsync(existing.SupplierId, item, existing.PurchaseDate);
            await _db.SaveChangesAsync();

            if (affectedProductIds.Count > 0)
            {
                await RecalculatePurchaseQtyAsync(affectedProductIds);
                await RecalculateStockQuantityAsync(affectedProductIds);
                await _db.SaveChangesAsync();
            }

            return NoContent();
        }

        // DELETE api/purchaseinvoices/5 — blocked once any payment has been allocated to it.
        // Line items cascade-delete with the invoice.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.Purchase && l.SourceId == id))
                return Conflict(new ApiError("Posted purchase invoices cannot be deleted. Use a purchase return or an accounting reversal."));
            var invoice = await _db.PurchaseInvoices.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (invoice is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            if (await _db.SupplierPaymentDetails.AnyAsync(d => d.PurchaseInvoiceId == id))
                return Conflict(new ApiError("Cannot delete an invoice that already has payments allocated to it."));

            // ProductStock.PurchaseInvoiceId is DeleteBehavior.SetNull, so removing
            // the invoice alone does NOT remove the stock batches it created — they'd
            // just become orphaned (PurchaseInvoiceId = null) while still holding their
            // full AvailableQuantity, leaving Product.StockQuantity permanently stale.
            // Same safety rule as RemoveItem: if any of a batch has already been
            // sold/transferred out, block the whole delete rather than silently
            // erasing units that are already accounted for elsewhere.
            var batches = await _db.ProductStocks
                .Where(s => s.PurchaseInvoiceId == id)
                .ToListAsync();

            var partiallyConsumed = batches.Where(s => s.AvailableQuantity < s.Quantity).ToList();
            if (partiallyConsumed.Count > 0)
            {
                var batchList = string.Join(", ", partiallyConsumed.Select(s => s.BatchNumber));
                return Conflict(new ApiError(
                    $"Cannot delete this invoice — batch(es) {batchList} already have units sold or transferred out."));
            }

            var affectedProductIds = invoice.Items.Select(i => i.ProductId).Distinct().ToList();

            DeleteReceiptImageFile(invoice.ReceiptImagePath);

            _db.ProductStocks.RemoveRange(batches);
            _db.PurchaseInvoices.Remove(invoice);
            await _db.SaveChangesAsync();

            // Whole invoice (and its items, cascade-deleted) plus its stock batches
            // (removed above) are gone now, so both recalcs need to run — needs its
            // own save after, same convention as everywhere else in this controller.
            await RecalculatePurchaseQtyAsync(affectedProductIds);
            await RecalculateStockQuantityAsync(affectedProductIds);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/purchaseinvoices/5/receipt-image — upload/replace the
        // receipt image as an actual file (multipart/form-data), instead of
        // base64 into the JSON body. In Postman: Body -> form-data -> key
        // "file", type "File" -> pick an image from disk.
        //
        // Saves the file physically under wwwroot/images/purchase-receipts
        // and stores the short public path in ReceiptImagePath, e.g.
        // "/images/purchase-receipts/4_a1b2c3d4.jpg" — no more base64 blobs
        // bloating every invoice response.
        [HttpPost("{id:int}/receipt-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(p => p.Id == id);
            if (invoice is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            if (file is null || file.Length == 0)
                return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            bool isAllowed = false;
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            if (!isAllowed)
                return BadRequest(new ApiError("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp."));

            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            string folderPath = GetPurchaseReceiptImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteReceiptImageFile(invoice.ReceiptImagePath);

            invoice.ReceiptImage = null; // stop keeping raw bytes in the DB now that we have a file on disk
            invoice.ReceiptImageContentType = file.ContentType;
            invoice.ReceiptImagePath = "/" + PurchaseReceiptImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { invoice.Id, invoice.ReceiptImagePath });
        }

        // DELETE api/purchaseinvoices/5/receipt-image — remove the receipt
        // image, both the physical file under wwwroot/images/purchase-receipts
        // and the DB fields.
        [HttpDelete("{id:int}/receipt-image")]
        public async Task<IActionResult> DeleteReceiptImage(int id)
        {
            var invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(p => p.Id == id);
            if (invoice is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            DeleteReceiptImageFile(invoice.ReceiptImagePath);

            invoice.ReceiptImagePath = null;
            invoice.ReceiptImage = null;
            invoice.ReceiptImageContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/purchaseinvoices/5/items — append one line item to an existing invoice.
        // Recomputes the invoice Total/Due from the item set.
        [HttpPost("{id:int}/items")]
        public async Task<ActionResult<PurchaseInvoiceItemReadDto>> AddItem(int id, [FromBody] PurchaseInvoiceItemWriteDto dto)
        {
            var invoice = await _db.PurchaseInvoices.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (invoice is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest(new ApiError($"Product {dto.ProductId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            // Same duplicate-batch guard as Create()/Update() — a batch number
            // must be unique per product across the whole DB, not just this invoice.
            if (!string.IsNullOrWhiteSpace(dto.BatchNumber) &&
                await _db.ProductStocks.AnyAsync(s => s.ProductId == dto.ProductId && s.BatchNumber == dto.BatchNumber))
            {
                return Conflict(new ApiError(
                    $"Batch number '{dto.BatchNumber}' is already in use for product {dto.ProductId}. " +
                    "Each batch number must be unique per product — use a different batch number, or omit it to have one generated automatically."));
            }

            var item = new PurchaseInvoiceItem
            {
                PurchaseInvoiceId = id,
                ProductId = dto.ProductId,
                UnitId = dto.UnitId,
                Quantity = dto.Quantity,
                UnitCost = dto.UnitCost,
                SubTotal = dto.Quantity * dto.UnitCost,
                BatchNumber = string.IsNullOrWhiteSpace(dto.BatchNumber)
                    ? await _codeGenerator.GenerateBatchNumberAsync()
                    : dto.BatchNumber,
                ExpiryDate = dto.ExpiryDate
            };

            invoice.Items.Add(item);
            await RecalculateTotalsAsync(invoice);
            await _db.SaveChangesAsync();

            // Item is now persisted, so PurchaseQty recalc (which queries the
            // DB) can see it — needs its own save afterwards.
            await RecalculatePurchaseQtyAsync(new[] { item.ProductId });

            // Create the ProductStock batch this line item receives into stock.
            await SyncStockBatchForItemAsync(item, invoice);
            await _db.SaveChangesAsync();

            await RecalculateStockQuantityAsync(new[] { item.ProductId });
            await _db.SaveChangesAsync();

            await _db.Entry(item).Reference(i => i.Product).LoadAsync();
            await _db.Entry(item).Reference(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id }, ItemToDto(item));
        }

        // PUT api/purchaseinvoices/5/items/12 — update a single line item.
        [HttpPut("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] PurchaseInvoiceItemWriteDto dto)
        {
            var invoice = await _db.PurchaseInvoices.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (invoice is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            var item = invoice.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for invoice {id}."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest(new ApiError($"Product {dto.ProductId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            // Track the old ProductId too, in case the item is being
            // reassigned to a different product — both need PurchaseQty
            // recalculated, not just the new one. Also capture the batch
            // number the ProductStock row was filed under BEFORE mutating
            // the item, so SyncStockBatchForItemAsync can find it.
            var oldProductId = item.ProductId;
            var oldBatchNumber = ResolveBatchNumber(item, invoice.InvoiceNo);

            // Same duplicate-batch guard as Create()/AddItem() — but skip the
            // conflict if the match IS this item's own current batch (i.e.
            // BatchNumber wasn't actually changed).
            if (!string.IsNullOrWhiteSpace(dto.BatchNumber) && dto.BatchNumber != oldBatchNumber)
            {
                var conflicting = await _db.ProductStocks.AnyAsync(s =>
                    s.ProductId == dto.ProductId && s.BatchNumber == dto.BatchNumber);
                if (conflicting)
                    return Conflict(new ApiError(
                        $"Batch number '{dto.BatchNumber}' is already in use for product {dto.ProductId}. Batch numbers must be unique per product."));
            }

            item.ProductId = dto.ProductId;
            item.UnitId = dto.UnitId;
            item.Quantity = dto.Quantity;
            item.UnitCost = dto.UnitCost;
            item.SubTotal = dto.Quantity * dto.UnitCost;
            // Omitting BatchNumber means "leave it as-is" now, not "blank it
            // out" — every item already has a GUID-generated batch number
            // from creation time, so there's nothing to auto-generate here.
            if (!string.IsNullOrWhiteSpace(dto.BatchNumber))
                item.BatchNumber = dto.BatchNumber;
            item.ExpiryDate = dto.ExpiryDate;

            await RecalculateTotalsAsync(invoice);
            await _db.SaveChangesAsync();

            // Item change is now persisted, so PurchaseQty recalc (which
            // queries the DB) can see it — needs its own save afterwards.
            await RecalculatePurchaseQtyAsync(new[] { oldProductId, item.ProductId });

            // Keep the ProductStock batch behind this line item in sync
            // (quantity/expiry/batch number/product may all have changed).
            await SyncStockBatchForItemAsync(item, invoice, oldProductId, oldBatchNumber);
            await _db.SaveChangesAsync();

            await RecalculateStockQuantityAsync(new[] { oldProductId, item.ProductId });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/purchaseinvoices/5/items/12 — remove a single line item.
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var invoice = await _db.PurchaseInvoices.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (invoice is null) return NotFound(new ApiError($"Purchase invoice {id} not found."));

            var item = invoice.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for invoice {id}."));

            var removedProductId = item.ProductId;
            var batchNumber = ResolveBatchNumber(item, invoice.InvoiceNo);

            // Find the ProductStock batch this line item receipted in. If any
            // of it has already been sold/transferred out, block the removal —
            // deleting the batch would either orphan those sale records or
            // silently make sold units vanish from the audit trail.
            var stock = await _db.ProductStocks.FirstOrDefaultAsync(s =>
                s.ProductId == item.ProductId && s.BatchNumber == batchNumber && s.PurchaseInvoiceId == id);

            if (stock is not null)
            {
                if (stock.AvailableQuantity < stock.Quantity)
                {
                    return Conflict(new ApiError(
                        $"Cannot remove this line — {stock.Quantity - stock.AvailableQuantity} unit(s) from batch '{stock.BatchNumber}' have already been sold or transferred out."));
                }
                _db.ProductStocks.Remove(stock);
            }

            _db.PurchaseInvoiceItems.Remove(item);
            invoice.Items.Remove(item);
            await RecalculateTotalsAsync(invoice);
            await _db.SaveChangesAsync();

            // Removal is now persisted, so PurchaseQty recalc (which queries
            // the DB) won't count the deleted row — needs its own save after.
            await RecalculatePurchaseQtyAsync(new[] { removedProductId });
            await RecalculateStockQuantityAsync(new[] { removedProductId });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // Keeps Total/Due/PaymentStatus honest whenever the item set changes
        // via the dedicated endpoints: Total = sum of item SubTotals -
        // Discount + Tax, Due = Total - Advance - (sum of PaidAmount across
        // all non-cancelled SupplierPayment allocations against this invoice).
        // The allocation subtraction is what keeps this in sync with
        // SupplierPaymentsController — without it, editing an item after a
        // payment was recorded would silently wipe out that payment's effect
        // on Due. Always recomputed from scratch (not incremented), so it
        // stays correct no matter what order operations happen in.
        private async Task RecalculateTotalsAsync(PurchaseInvoice invoice)
        {
            invoice.Total = invoice.Items.Sum(i => i.SubTotal) - invoice.Discount + invoice.TaxOrOthers;

            var paidViaAllocations = await _db.SupplierPaymentDetails
                .Where(d => d.PurchaseInvoiceId == invoice.Id && !d.SupplierPayment.IsCancelled)
                .SumAsync(d => (decimal?)d.PaidAmount) ?? 0m;

            invoice.Due = Math.Max(0, invoice.Total - invoice.Advance - paidViaAllocations);
            invoice.PaymentStatus = invoice.Due <= 0 ? "Complete" : "InComplete";
        }

        // Keeps Product.PurchaseQty honest — total quantity ever purchased
        // for that product across ALL purchase invoices (not just this one).
        // Always recomputed from scratch via SUM (not incremented), same
        // reasoning as RecalculateTotalsAsync: stays correct no matter what
        // order Create/AddItem/UpdateItem/RemoveItem happen in, and self-heals
        // if something ever gets out of sync. Callers must SaveChangesAsync
        // separately after this — see call sites for why.
        private async Task RecalculatePurchaseQtyAsync(IEnumerable<int> productIds)
        {
            foreach (var productId in productIds.Distinct())
            {
                var total = await _db.PurchaseInvoiceItems
                    .Where(i => i.ProductId == productId)
                    .SumAsync(i => (decimal?)i.Quantity) ?? 0m;

                var product = await _db.Products.FindAsync(productId);
                if (product is not null)
                    product.PurchaseQty = (int)total;
            }
        }

        // A batch number is required for ProductStock (and unique per
        // product), but BatchNumber is optional on the invoice item —
        // suppliers/receiving clerks don't always record one. Falls back to
        // a stable, unique identifier derived from the invoice + item id so
        // two items never collide.
        // Safety net only: every PurchaseInvoiceItem created going forward
        // always has a non-empty BatchNumber (GUID-generated at creation
        // time when the caller doesn't supply one — see Create()/AddItem()).
        // This fallback only matters for pre-existing legacy rows saved
        // before that change, where BatchNumber could still be blank.
        private static string ResolveBatchNumber(PurchaseInvoiceItem item, string invoiceNo) =>
            string.IsNullOrWhiteSpace(item.BatchNumber) ? $"{invoiceNo}-{item.Id}" : item.BatchNumber;

        // ProductStock.ExpiryDate is required, but ExpiryDate is optional on
        // the invoice item. Falls back to a 2-year placeholder so the batch
        // isn't blocked from being created — this should really be filled in
        // properly at receiving time for real pharmacy stock.
        private static DateOnly ResolveExpiryDate(PurchaseInvoiceItem item) =>
            item.ExpiryDate.HasValue
                ? DateOnly.FromDateTime(item.ExpiryDate.Value)
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2));

        // Creates or updates the ProductStock batch behind a purchase invoice
        // line item — this is the actual "goods receipt" event that turns a
        // Keeps the Supplier<->Product many-to-many (and its per-unit price)
        // in sync with reality every time an invoice line is posted. If this
        // supplier + product has never been linked before, creates the link
        // and seeds a SupplierProductPrice row for the unit purchased. If it
        // already exists, refreshes the "last purchase" rollup fields and —
        // only when the price actually changed — updates the current
        // PurchasePrice for that unit and writes a supplier-scoped
        // ProductPriceHistory row so the price trail stays intact.
        private async Task UpsertSupplierProductFromInvoiceItemAsync(int supplierId, PurchaseInvoiceItem item, DateTime purchaseDate)
        {
            var link = await _db.SupplierProducts
                .Include(sp => sp.Prices)
                .FirstOrDefaultAsync(sp => sp.SupplierId == supplierId && sp.ProductId == item.ProductId);

            if (link is null)
            {
                link = new PharmacyV2.Models.People.SupplierProduct
                {
                    SupplierId = supplierId,
                    ProductId = item.ProductId,
                    IsActive = true
                };
                _db.SupplierProducts.Add(link);
            }

            link.LastPurchaseDate = purchaseDate;
            link.LastPurchaseUnitCost = item.UnitCost;
            link.LastPurchaseUnitId = item.UnitId;

            var unitPrice = link.Prices.FirstOrDefault(p => p.UnitId == item.UnitId);
            if (unitPrice is null)
            {
                link.Prices.Add(new PharmacyV2.Models.People.SupplierProductPrice
                {
                    UnitId = item.UnitId,
                    BaseQuantity = 1,
                    PurchasePrice = item.UnitCost
                });

                _db.Add(new ProductPriceHistory
                {
                    ProductId = item.ProductId,
                    SupplierId = supplierId,
                    UnitId = item.UnitId,
                    PriceType = "SupplierPurchase",
                    PreviousPrice = null,
                    NewPrice = item.UnitCost,
                    ChangedAt = DateTime.UtcNow
                });
            }
            else if (unitPrice.PurchasePrice != item.UnitCost)
            {
                _db.Add(new ProductPriceHistory
                {
                    ProductId = item.ProductId,
                    SupplierId = supplierId,
                    UnitId = item.UnitId,
                    PriceType = "SupplierPurchase",
                    PreviousPrice = unitPrice.PurchasePrice,
                    NewPrice = item.UnitCost,
                    ChangedAt = DateTime.UtcNow
                });

                unitPrice.PurchasePrice = item.UnitCost;
                unitPrice.UpdatedAt = DateTime.UtcNow;
            }
        }

        // recorded purchase into real, sellable stock. One PurchaseInvoiceItem
        // maps to one ProductStock batch (matched by ProductId + BatchNumber
        // + PurchaseInvoiceId). Must run AFTER the item has been saved once
        // (so item.Id is populated for the batch-number fallback), and needs
        // its own SaveChangesAsync afterward — same convention as
        // RecalculatePurchaseQtyAsync.
        //
        // previousProductId/previousBatchNumber let UpdateItem find the batch
        // under its OLD identity when the product or batch number changed.
        private async Task SyncStockBatchForItemAsync(
            PurchaseInvoiceItem item,
            PurchaseInvoice invoice,
            int? previousProductId = null,
            string? previousBatchNumber = null)
        {
            string batchNumber = ResolveBatchNumber(item, invoice.InvoiceNo);
            DateOnly expiryDate = ResolveExpiryDate(item);
            DateOnly receivedDate = DateOnly.FromDateTime(invoice.PurchaseDate);

            ProductStock? stock = null;
            if (previousProductId is not null && previousBatchNumber is not null)
            {
                stock = await _db.ProductStocks.FirstOrDefaultAsync(s =>
                    s.ProductId == previousProductId && s.BatchNumber == previousBatchNumber && s.PurchaseInvoiceId == invoice.Id);
            }
            stock ??= await _db.ProductStocks.FirstOrDefaultAsync(s =>
                s.ProductId == item.ProductId && s.BatchNumber == batchNumber && s.PurchaseInvoiceId == invoice.Id);

            if (stock is null)
            {
                _db.ProductStocks.Add(new ProductStock
                {
                    ProductId = item.ProductId,
                    WarehouseId = invoice.WarehouseId,
                    BatchNumber = batchNumber,
                    Quantity = item.Quantity,
                    AvailableQuantity = item.Quantity,
                    ExpiryDate = expiryDate,
                    SupplierId = invoice.SupplierId,
                    PurchaseInvoiceId = invoice.Id,
                    ReceivedDate = receivedDate,
                    UnitCost = item.UnitCost
                });
            }
            else
            {
                // Preserve however much of the batch has already been sold —
                // only the CHANGE in received quantity should move
                // AvailableQuantity, never a blind overwrite (that would
                // silently "restock" units that were already sold out of
                // this batch, or hide a shortfall).
                decimal alreadySold = stock.Quantity - stock.AvailableQuantity;
                stock.ProductId = item.ProductId;
                stock.WarehouseId = invoice.WarehouseId;
                stock.BatchNumber = batchNumber;
                stock.Quantity = item.Quantity;
                stock.AvailableQuantity = Math.Max(0, item.Quantity - alreadySold);
                stock.UnitCost = item.UnitCost;
                stock.ExpiryDate = expiryDate;
                stock.SupplierId = invoice.SupplierId;
                stock.ReceivedDate = receivedDate;
            }
        }

        // Keeps Product.StockQuantity honest — total AvailableQuantity across
        // ALL ProductStock batches (every warehouse) for that product.
        // Always recomputed from scratch via SUM, same reasoning as
        // RecalculatePurchaseQtyAsync: stays correct no matter what order
        // things happen in, and self-heals if something ever drifts.
        // Callers must SaveChangesAsync separately after this.
        private async Task RecalculateStockQuantityAsync(IEnumerable<int> productIds)
        {
            foreach (var productId in productIds.Distinct())
            {
                var total = await _db.ProductStocks
                    .Where(s => s.ProductId == productId)
                    .SumAsync(s => (decimal?)s.AvailableQuantity) ?? 0m;

                var product = await _db.Products.FindAsync(productId);
                if (product is not null)
                    product.StockQuantity = (int)total;
            }
        }

        // Deletes the physical file behind a ReceiptImagePath like
        // "/images/purchase-receipts/4_a1b2...c1.jpg", if it exists. Safe to
        // call with null/empty/unrelated paths — it just does nothing then.
        private void DeleteReceiptImageFile(string? receiptImagePath)
        {
            if (string.IsNullOrWhiteSpace(receiptImagePath))
                return;

            if (receiptImagePath.IndexOf(PurchaseReceiptImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(receiptImagePath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetPurchaseReceiptImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private static PurchaseInvoiceItemReadDto ItemToDto(PurchaseInvoiceItem i) => new()
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.Product?.ProductName,
            UnitId = i.UnitId,
            UnitName = i.Unit?.Name,
            Quantity = i.Quantity,
            UnitCost = i.UnitCost,
            SubTotal = i.SubTotal,
            BatchNumber = i.BatchNumber,
            ExpiryDate = i.ExpiryDate
        };

        private static PurchaseInvoiceReadDto ToDto(PurchaseInvoice p) => new()
        {
            Id = p.Id,
            InvoiceNo = p.InvoiceNo,
            PurchaseDate = p.PurchaseDate,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.SupplierName,
            PurchaseOrderId = p.PurchaseOrderId,
            WarehouseId = p.WarehouseId,
            WarehouseName = p.Warehouse?.Name,
            PaymentMethod = p.PaymentMethod,
            Advance = p.Advance,
            Due = p.Due,
            Total = p.Total,
            Discount = p.Discount,
            TaxOrOthers = p.TaxOrOthers,
            PaymentStatus = p.PaymentStatus,
            IsSupplierWise = p.IsSupplierWise,
            CreatedAt = p.CreatedAt,
            ReceiptImagePath = p.ReceiptImagePath,
            ReceiptImageContentType = p.ReceiptImageContentType,
            Items = p.Items.Select(ItemToDto).ToList()
        };
    }
}
```

### `PurchaseOrdersController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Purchase;

namespace PharmacyV2.Controllers
{
    // Purchase orders raised against a supplier (manually, or auto-generated
    // from DailyPurchaseRequirementTable). Create accepts the header + its
    // line items together so Postman only needs one call per order.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PurchaseOrdersController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        public PurchaseOrdersController(PharmacyDbContext db) => _db = db;

        // GET api/purchaseorders — all orders with their line items.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PurchaseOrderReadDto>>> GetAll()
        {
            var orders = await _db.PurchaseOrders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Items).ThenInclude(i => i.Unit)
                .Include(o => o.Supplier)
                .AsNoTracking().ToListAsync();
            return Ok(orders.Select(ToDto));
        }

        // GET api/purchaseorders/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PurchaseOrderReadDto>> GetById(int id)
        {
            var order = await _db.PurchaseOrders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Items).ThenInclude(i => i.Unit)
                .Include(o => o.Supplier)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id);
            return order is null ? NotFound() : Ok(ToDto(order));
        }

        // POST api/purchaseorders — create an order header plus its items in one call.
        // OrderNo must be unique; each item's ProductId/UnitId must already exist.
        [HttpPost]
        public async Task<ActionResult<PurchaseOrderReadDto>> Create([FromBody] PurchaseOrderCreateDto dto)
        {
            if (await _db.PurchaseOrders.AnyAsync(o => o.OrderNo == dto.OrderNo))
                return Conflict(new ApiError($"Order number '{dto.OrderNo}' already exists."));

            if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == dto.SupplierId))
                return BadRequest(new ApiError($"Supplier {dto.SupplierId} does not exist."));

            var order = new PurchaseOrder
            {
                OrderNo = dto.OrderNo,
                OrderDate = dto.OrderDate,
                RequirementDate = dto.RequirementDate,
                SupplierId = dto.SupplierId,
                SupplierCategory = dto.SupplierCategory,
                Source = dto.Source,
                Status = dto.Status,
                IsUrgent = dto.IsUrgent,
                CreatedAt = DateTime.UtcNow,
                ReceiptImagePath = dto.ReceiptImagePath,
                ReceiptImage = dto.ReceiptImage,
                ReceiptImageContentType = dto.ReceiptImageContentType
            };

            foreach (var itemDto in dto.Items)
            {
                if (!await _db.Products.AnyAsync(p => p.Id == itemDto.ProductId))
                    return BadRequest(new ApiError($"Product {itemDto.ProductId} does not exist."));
                if (!await _db.Units.AnyAsync(u => u.Id == itemDto.UnitId))
                    return BadRequest(new ApiError($"Unit {itemDto.UnitId} does not exist."));

                order.Items.Add(new PurchaseOrderItem
                {
                    ProductId = itemDto.ProductId,
                    UnitId = itemDto.UnitId,
                    LastPurchasePrice = itemDto.LastPurchasePrice,
                    StockQty = itemDto.StockQty,
                    RequiredQty = itemDto.RequiredQty,
                    OrderQty = itemDto.OrderQty,
                    ReceivingQty = itemDto.ReceivingQty,
                    IsCancelled = itemDto.IsCancelled
                });
            }

            _db.PurchaseOrders.Add(order);
            await _db.SaveChangesAsync();

            await _db.Entry(order).Reference(o => o.Supplier).LoadAsync();
            await _db.Entry(order).Collection(o => o.Items).Query()
                .Include(i => i.Product).Include(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(order));
        }

        // PUT api/purchaseorders/5 — update header fields (status, supplier category, etc).
        // Use the items endpoints below to add/remove line items.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PurchaseOrderUpdateDto dto)
        {
            var existing = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id);
            if (existing is null) return NotFound();

            existing.SupplierId = dto.SupplierId;
            existing.SupplierCategory = dto.SupplierCategory;
            existing.Source = dto.Source;
            existing.Status = dto.Status;
            existing.IsUrgent = dto.IsUrgent;
            existing.RequirementDate = dto.RequirementDate;

            // Receipt fields are optional on update — only overwrite when the
            // caller actually sends a value, so a PUT that omits them doesn't
            // wipe out a receipt attached earlier.
            if (dto.ReceiptImagePath is not null) existing.ReceiptImagePath = dto.ReceiptImagePath;
            if (dto.ReceiptImage is not null) existing.ReceiptImage = dto.ReceiptImage;
            if (dto.ReceiptImageContentType is not null) existing.ReceiptImageContentType = dto.ReceiptImageContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/purchaseorders/5/receipt-image — upload/replace the
        // receipt image as an actual file (multipart/form-data). In Postman:
        // Body -> form-data -> key "file", type "File" -> pick an image.
        [HttpPost("{id:int}/receipt-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id);
            if (order is null) return NotFound(new ApiError($"Purchase order {id} not found."));

            if (file is null || file.Length == 0)
                return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            order.ReceiptImage = ms.ToArray();
            order.ReceiptImageContentType = file.ContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/purchaseorders/5/items — append one line item to an existing order.
        [HttpPost("{id:int}/items")]
        public async Task<ActionResult<PurchaseOrderItemReadDto>> AddItem(int id, [FromBody] PurchaseOrderItemWriteDto dto)
        {
            if (!await _db.PurchaseOrders.AnyAsync(o => o.Id == id))
                return NotFound(new ApiError($"Purchase order {id} not found."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest(new ApiError($"Product {dto.ProductId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            var item = new PurchaseOrderItem
            {
                PurchaseOrderId = id,
                ProductId = dto.ProductId,
                UnitId = dto.UnitId,
                LastPurchasePrice = dto.LastPurchasePrice,
                StockQty = dto.StockQty,
                RequiredQty = dto.RequiredQty,
                OrderQty = dto.OrderQty,
                ReceivingQty = dto.ReceivingQty,
                IsCancelled = dto.IsCancelled
            };

            _db.PurchaseOrderItems.Add(item);
            await _db.SaveChangesAsync();

            await _db.Entry(item).Reference(i => i.Product).LoadAsync();
            await _db.Entry(item).Reference(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id }, ItemToDto(item));
        }

        // DELETE api/purchaseorders/5/items/12 — remove a single line item.
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var item = await _db.PurchaseOrderItems.FirstOrDefaultAsync(i => i.Id == itemId && i.PurchaseOrderId == id);
            if (item is null) return NotFound();

            _db.PurchaseOrderItems.Remove(item);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/purchaseorders/5 — blocked once it's been converted into a purchase invoice.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _db.PurchaseOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
            if (order is null) return NotFound();

            if (await _db.PurchaseInvoices.AnyAsync(p => p.PurchaseOrderId == id))
                return Conflict(new ApiError("Cannot delete an order that has already been converted to a purchase invoice."));

            _db.PurchaseOrderItems.RemoveRange(order.Items);
            _db.PurchaseOrders.Remove(order);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static PurchaseOrderItemReadDto ItemToDto(PurchaseOrderItem i) => new()
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.Product?.ProductName,
            UnitId = i.UnitId,
            UnitName = i.Unit?.Name,
            LastPurchasePrice = i.LastPurchasePrice,
            StockQty = i.StockQty,
            RequiredQty = i.RequiredQty,
            OrderQty = i.OrderQty,
            ReceivingQty = i.ReceivingQty,
            IsCancelled = i.IsCancelled
        };

        private static PurchaseOrderReadDto ToDto(PurchaseOrder o) => new()
        {
            Id = o.Id,
            OrderNo = o.OrderNo,
            OrderDate = o.OrderDate,
            RequirementDate = o.RequirementDate,
            SupplierId = o.SupplierId,
            SupplierName = o.Supplier?.SupplierName,
            SupplierCategory = o.SupplierCategory,
            Source = o.Source,
            Status = o.Status,
            IsUrgent = o.IsUrgent,
            CreatedAt = o.CreatedAt,
            ReceiptImagePath = o.ReceiptImagePath,
            ReceiptImage = o.ReceiptImage,
            ReceiptImageContentType = o.ReceiptImageContentType,
            Items = o.Items.Select(ItemToDto).ToList()
        };
    }
}
```

### `PurchaseReturnsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Services;
using PharmacyV2.Enums;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // Master-detail-detail trio: PurchaseReturn -> PurchaseReturnItem (what's
    // being returned) and PurchaseReturn -> PurchaseReturnReceive (refund/credit
    // received back from the supplier for the return). The models for this
    // already existed but had no controller — added here.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class PurchaseReturnsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;
        private readonly IAuditService _audit;
        public PurchaseReturnsController(PharmacyDbContext db, IAccountingPostingService accounting, IAuditService audit) { _db = db; _accounting = accounting; _audit = audit; }

        // GET api/purchasereturns?purchaseInvoiceId=3
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PurchaseReturnReadDto>>> GetAll([FromQuery] int? purchaseInvoiceId)
        {
            var query = _db.PurchaseReturns
                .Include(r => r.Items).ThenInclude(i => i.Product)
                .Include(r => r.Items).ThenInclude(i => i.Unit)
                .Include(r => r.Receives)
                .AsNoTracking().AsQueryable();
            if (purchaseInvoiceId is not null) query = query.Where(r => r.PurchaseInvoiceId == purchaseInvoiceId);

            var returns = await query.OrderByDescending(r => r.ReturnDate).ToListAsync();
            return Ok(returns.Select(ToDto));
        }

        // GET api/purchasereturns/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PurchaseReturnReadDto>> GetById(int id)
        {
            var ret = await _db.PurchaseReturns
                .Include(r => r.Items).ThenInclude(i => i.Product)
                .Include(r => r.Items).ThenInclude(i => i.Unit)
                .Include(r => r.Receives)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);
            return ret is null ? NotFound(new ApiError($"Purchase return {id} not found.")) : Ok(ToDto(ret));
        }

        // POST api/purchasereturns — header + line items (+ optional receives) in one call.
        // ReturnNo must be unique; PurchaseInvoiceId must already exist.
        // ReturnTotal is always computed server-side from the items.
        [HttpPost]
        public async Task<ActionResult<PurchaseReturnReadDto>> Create([FromBody] PurchaseReturnCreateDto dto)
        {
            if (await _db.PurchaseReturns.AnyAsync(r => r.ReturnNo == dto.ReturnNo))
                return Conflict(new ApiError($"Return number '{dto.ReturnNo}' already exists."));

            if (!await _db.PurchaseInvoices.AnyAsync(p => p.Id == dto.PurchaseInvoiceId))
                return BadRequest(new ApiError($"Purchase invoice {dto.PurchaseInvoiceId} does not exist."));

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var ret = new PurchaseReturn
            {
                ReturnNo = dto.ReturnNo,
                ReturnDate = dto.ReturnDate,
                PurchaseInvoiceId = dto.PurchaseInvoiceId,
                Reason = dto.Reason,
                ReceiptImagePath = dto.ReceiptImagePath,
                ReceiptImage = dto.ReceiptImage,
                ReceiptImageContentType = dto.ReceiptImageContentType
            };

            if (dto.Items is { Count: > 0 })
            {
                foreach (var itemDto in dto.Items)
                {
                    var source = await _db.PurchaseInvoiceItems.FirstOrDefaultAsync(x => x.Id == itemDto.PurchaseInvoiceItemId && x.PurchaseInvoiceId == dto.PurchaseInvoiceId);
                    if (source is null) return BadRequest(new ApiError("Return item must reference an original purchase invoice item."));
                    var returned = await _db.PurchaseReturnItems.Where(x => x.PurchaseInvoiceItemId == source.Id).SumAsync(x => (decimal?)x.Quantity) ?? 0;
                    if (itemDto.Quantity <= 0 || returned + itemDto.Quantity > source.Quantity) return Conflict(new ApiError("Return quantity exceeds the original purchased quantity."));
                    var baseQuantity = itemDto.Quantity * source.Quantity / source.Quantity;
                    var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.ProductId == source.ProductId && s.PurchaseInvoiceId == dto.PurchaseInvoiceId);
                    if (stock is null || stock.AvailableQuantity < baseQuantity) return Conflict(new ApiError("The original purchase batch does not have enough available stock to return."));
                    stock.AvailableQuantity -= baseQuantity;

                    ret.Items.Add(new PurchaseReturnItem
                    {
                        PurchaseInvoiceItemId = source.Id, ProductId = source.ProductId, UnitId = source.UnitId,
                        Quantity = itemDto.Quantity,
                        BaseQuantity = baseQuantity, UnitCost = source.UnitCost, SubTotal = itemDto.Quantity * source.UnitCost
                    });
                }
            }

            if (dto.Receives is { Count: > 0 })
            {
                if (dto.Receives.Sum(r => r.ReceivedAmount) > ret.Items.Sum(i => i.SubTotal))
                    return BadRequest(new ApiError("Initial refund amount cannot exceed the purchase-return total."));
                foreach (var receiveDto in dto.Receives)
                {
                    ret.Receives.Add(new PurchaseReturnReceive
                    {
                        ReceivedAmount = receiveDto.ReceivedAmount,
                        ReceivedDate = receiveDto.ReceivedDate,
                        PaymentMethod = receiveDto.PaymentMethod,
                        ReceivedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                        Note = receiveDto.Note
                    });
                }
            }

            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);

            _db.PurchaseReturns.Add(ret);
            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.PurchaseReturn, ret.Id, ret.ReturnDate, $"Purchase return {ret.ReturnNo}", ("2000", ret.ReturnTotal, 0), ("1200", 0, ret.ReturnTotal));
            foreach (var receive in ret.Receives)
            {
                var account = receive.PaymentMethod == "Card" ? "1010" : receive.PaymentMethod == "Mobile Banking" ? "1020" : "1000";
                await _accounting.PostAsync(LedgerSourceType.PurchaseReturnRefund, receive.Id, receive.ReceivedDate,
                    $"Supplier refund received for purchase return {ret.ReturnNo}", (account, receive.ReceivedAmount, 0), ("2000", 0, receive.ReceivedAmount));
                await _audit.LogAsync("PurchaseReturnRefundReceived", "PurchaseReturn", ret.Id, $"Amount: {receive.ReceivedAmount}; Method: {receive.PaymentMethod}", receive.ReceivedByUserId);
            }
            ret.IsCompleted = ret.Receives.Sum(r => r.ReceivedAmount) >= ret.ReturnTotal;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("PurchaseReturnCreated", "PurchaseReturn", ret.Id, $"Return total: {ret.ReturnTotal}; Initial refund received: {ret.Receives.Sum(r => r.ReceivedAmount)}", User.FindFirstValue(ClaimTypes.NameIdentifier));
            await transaction.CommitAsync();

            await _db.Entry(ret).Collection(r => r.Items).Query()
                .Include(i => i.Product).Include(i => i.Unit).LoadAsync();
            await _db.Entry(ret).Collection(r => r.Receives).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = ret.Id }, ToDto(ret));
        }

        // PUT api/purchasereturns/5 — header fields, plus an optional full
        // sync of Receives (send "receives": [...] to replace the set, or
        // omit it to leave receives untouched). Items still only go through
        // the dedicated /items endpoints below.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PurchaseReturnUpdateDto dto)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.PurchaseReturn && l.SourceId == id)) return Conflict(new ApiError("Posted purchase returns are immutable."));
            var existing = await _db.PurchaseReturns
                .Include(r => r.Receives)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (existing is null) return NotFound(new ApiError($"Purchase return {id} not found."));

            existing.Reason = dto.Reason;
            existing.IsCompleted = dto.IsCompleted;

            // Receipt fields are optional on update — only overwrite when the
            // caller actually sends a value, so a PUT that omits them doesn't
            // wipe out a receipt attached earlier.
            if (dto.ReceiptImagePath is not null) existing.ReceiptImagePath = dto.ReceiptImagePath;
            if (dto.ReceiptImage is not null) existing.ReceiptImage = dto.ReceiptImage;
            if (dto.ReceiptImageContentType is not null) existing.ReceiptImageContentType = dto.ReceiptImageContentType;

            // Receives: optional full sync. Omit the field entirely (null) to
            // leave existing receives untouched; send a list (even []) to
            // replace the whole set — no Id/unmatched Id => insert, matching
            // Id => update, existing row missing from the payload => delete.
            if (dto.Receives is not null)
            {
                var incomingIds = dto.Receives.Where(r => r.Id is > 0).Select(r => r.Id!.Value).ToHashSet();
                var toRemove = existing.Receives.Where(r => !incomingIds.Contains(r.Id)).ToList();
                foreach (var receiveToRemove in toRemove)
                {
                    _db.PurchaseReturnReceives.Remove(receiveToRemove);
                    existing.Receives.Remove(receiveToRemove);
                }

                foreach (var receiveDto in dto.Receives)
                {
                    var existingReceive = receiveDto.Id is > 0
                        ? existing.Receives.FirstOrDefault(r => r.Id == receiveDto.Id)
                        : null;

                    if (existingReceive is not null)
                    {
                        existingReceive.ReceivedAmount = receiveDto.ReceivedAmount;
                        existingReceive.ReceivedDate = receiveDto.ReceivedDate;
                        existingReceive.Note = receiveDto.Note;
                    }
                    else
                    {
                        existing.Receives.Add(new PurchaseReturnReceive
                        {
                            ReceivedAmount = receiveDto.ReceivedAmount,
                            ReceivedDate = receiveDto.ReceivedDate,
                            Note = receiveDto.Note
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/purchasereturns/5 — items and receives cascade-delete
        // with the return (configured OnDelete(Cascade) in PharmacyDbContext).
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var ret = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Purchase return {id} not found."));

            _db.PurchaseReturns.Remove(ret); // items + receives cascade-delete with the return
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/purchasereturns/5/receipt-image — upload/replace the
        // receipt image as an actual file (multipart/form-data). In Postman:
        // Body -> form-data -> key "file", type "File" -> pick an image.
        [HttpPost("{id:int}/receipt-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var ret = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Purchase return {id} not found."));

            if (file is null || file.Length == 0)
                return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ret.ReceiptImage = ms.ToArray();
            ret.ReceiptImageContentType = file.ContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints: items ----------

        // POST api/purchasereturns/5/items — append one line item.
        [HttpPost("{id:int}/items")]
        public async Task<ActionResult<PurchaseReturnItemReadDto>> AddItem(int id, [FromBody] PurchaseReturnItemWriteDto dto)
        {
            var ret = await _db.PurchaseReturns.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Purchase return {id} not found."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest(new ApiError($"Product {dto.ProductId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            var item = new PurchaseReturnItem
            {
                PurchaseReturnId = id,
                ProductId = dto.ProductId,
                UnitId = dto.UnitId,
                Quantity = dto.Quantity,
                BaseQuantity = dto.BaseQuantity,
                UnitCost = dto.UnitCost,
                SubTotal = dto.Quantity * dto.UnitCost
            };

            ret.Items.Add(item);
            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);
            await _db.SaveChangesAsync();

            await _db.Entry(item).Reference(i => i.Product).LoadAsync();
            await _db.Entry(item).Reference(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id }, ItemToDto(item));
        }

        // PUT api/purchasereturns/5/items/12 — update a single line item.
        [HttpPut("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] PurchaseReturnItemWriteDto dto)
        {
            var ret = await _db.PurchaseReturns.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Purchase return {id} not found."));

            var item = ret.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for purchase return {id}."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequest(new ApiError($"Product {dto.ProductId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            item.ProductId = dto.ProductId;
            item.UnitId = dto.UnitId;
            item.Quantity = dto.Quantity;
            item.BaseQuantity = dto.BaseQuantity;
            item.UnitCost = dto.UnitCost;
            item.SubTotal = dto.Quantity * dto.UnitCost;

            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/purchasereturns/5/items/12 — remove a single line item.
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var ret = await _db.PurchaseReturns.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Purchase return {id} not found."));

            var item = ret.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for purchase return {id}."));

            _db.PurchaseReturnItems.Remove(item);
            ret.Items.Remove(item);
            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints: receives ----------

        // POST api/purchasereturns/5/receives — record a refund/credit received from the supplier.
        [HttpPost("{id:int}/receives")]
        public async Task<ActionResult<PurchaseReturnReceiveReadDto>> AddReceive(int id, [FromBody] PurchaseReturnReceiveWriteDto dto)
        {
            var ret = await _db.PurchaseReturns.Include(r => r.Receives).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Purchase return {id} not found."));
            var alreadyReceived = ret.Receives.Sum(r => r.ReceivedAmount);
            if (dto.ReceivedAmount > ret.ReturnTotal - alreadyReceived)
                return BadRequest(new ApiError("Refund amount cannot exceed the remaining supplier credit."));

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var receive = new PurchaseReturnReceive
            {
                PurchaseReturnId = id,
                ReceivedAmount = dto.ReceivedAmount,
                ReceivedDate = dto.ReceivedDate,
                PaymentMethod = dto.PaymentMethod,
                ReceivedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                Note = dto.Note
            };

            _db.PurchaseReturnReceives.Add(receive);
            await _db.SaveChangesAsync();
            var account = dto.PaymentMethod == "Card" ? "1010" : dto.PaymentMethod == "Mobile Banking" ? "1020" : "1000";
            await _accounting.PostAsync(LedgerSourceType.PurchaseReturnRefund, receive.Id, receive.ReceivedDate,
                $"Supplier refund received for purchase return {ret.ReturnNo}", (account, receive.ReceivedAmount, 0), ("2000", 0, receive.ReceivedAmount));
            await _audit.LogAsync("PurchaseReturnRefundReceived", "PurchaseReturn", ret.Id, $"Amount: {receive.ReceivedAmount}; Method: {dto.PaymentMethod}", receive.ReceivedByUserId);
            ret.IsCompleted = alreadyReceived + receive.ReceivedAmount >= ret.ReturnTotal;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return CreatedAtAction(nameof(GetById), new { id }, ReceiveToDto(receive));
        }

        // DELETE api/purchasereturns/5/receives/7 — remove a receive record.
        [HttpDelete("{id:int}/receives/{receiveId:int}")]
        public async Task<IActionResult> RemoveReceive(int id, int receiveId)
        {
            var receive = await _db.PurchaseReturnReceives
                .FirstOrDefaultAsync(r => r.Id == receiveId && r.PurchaseReturnId == id);
            if (receive is null) return NotFound(new ApiError($"Receive {receiveId} not found for purchase return {id}."));

            _db.PurchaseReturnReceives.Remove(receive);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static PurchaseReturnItemReadDto ItemToDto(PurchaseReturnItem i) => new()
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.Product?.ProductName,
            UnitId = i.UnitId,
            UnitName = i.Unit?.Name,
            Quantity = i.Quantity,
            BaseQuantity = i.BaseQuantity,
            UnitCost = i.UnitCost,
            SubTotal = i.SubTotal
        };

        private static PurchaseReturnReceiveReadDto ReceiveToDto(PurchaseReturnReceive r) => new()
        {
            Id = r.Id,
            ReceivedAmount = r.ReceivedAmount,
            ReceivedDate = r.ReceivedDate,
            PaymentMethod = r.PaymentMethod,
            Note = r.Note
        };

        private static PurchaseReturnReadDto ToDto(PurchaseReturn r) => new()
        {
            Id = r.Id,
            ReturnNo = r.ReturnNo,
            ReturnDate = r.ReturnDate,
            PurchaseInvoiceId = r.PurchaseInvoiceId,
            ReturnTotal = r.ReturnTotal,
            Reason = r.Reason,
            IsCompleted = r.IsCompleted,
            ReceiptImagePath = r.ReceiptImagePath,
            ReceiptImage = r.ReceiptImage,
            ReceiptImageContentType = r.ReceiptImageContentType,
            Items = r.Items.Select(ItemToDto).ToList(),
            Receives = r.Receives.Select(ReceiveToDto).ToList()
        };
    }
}
```

### `Rolepermissionscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Other;

namespace PharmacyV2.Controllers
{
    // API for RolePermission — the per-module CanView/CanCreate/CanEdit/
    // CanDelete matrix for a Role. Had a model + DbSet + fluent config
    // (unique on RoleId+Module, cascade-deletes with its Role) but no
    // controller. Nested under /api/roles/{roleId}/permissions rather than
    // a top-level resource, since a permission row is meaningless without
    // its role.
    [Authorize]
    [Route("api/roles/{roleId:int}/permissions")]
    [ApiController]
    public class RolePermissionsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public RolePermissionsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/roles/5/permissions — the full matrix for this role
        // (only the modules that have an explicit row; anything not listed
        // has no access).
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RolePermissionReadDto>>> GetAll(int roleId)
        {
            if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
                return NotFoundResponse($"Role {roleId} not found.");

            var permissions = await _db.RolePermissions
                .AsNoTracking()
                .Where(p => p.RoleId == roleId)
                .Select(p => MapToReadDto(p))
                .ToListAsync();

            return Ok(permissions);
        }

        // PUT api/roles/5/permissions — replaces the entire matrix in one
        // call: for each {module, canView, canCreate, canEdit, canDelete} in
        // the body, upsert that module's row; any existing module row NOT
        // present in the body is removed (so unchecking a module in the UI
        // and saving actually revokes it). This matches how a permissions
        // screen is normally built — one grid, one Save.
        [HttpPut]
        public async Task<ActionResult<IEnumerable<RolePermissionReadDto>>> SetAll(int roleId, [FromBody] List<RolePermissionSetDto> permissions)
        {
            if (!await _db.Roles.AnyAsync(r => r.Id == roleId))
                return NotFoundResponse($"Role {roleId} not found.");

            var moduleNames = permissions.Select(p => p.Module).ToList();
            if (moduleNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != moduleNames.Count)
                return BadRequestResponse("Each module can only appear once in the list.");

            var existing = await _db.RolePermissions.Where(p => p.RoleId == roleId).ToListAsync();

            // Remove rows for modules no longer in the submitted matrix.
            var toRemove = existing.Where(e => !moduleNames.Contains(e.Module, StringComparer.OrdinalIgnoreCase)).ToList();
            _db.RolePermissions.RemoveRange(toRemove);

            foreach (var dto in permissions)
            {
                var row = existing.FirstOrDefault(e => string.Equals(e.Module, dto.Module, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new RolePermission { RoleId = roleId, Module = dto.Module };
                    _db.RolePermissions.Add(row);
                }

                row.CanView = dto.CanView;
                row.CanCreate = dto.CanCreate;
                row.CanEdit = dto.CanEdit;
                row.CanDelete = dto.CanDelete;
            }

            await _db.SaveChangesAsync();

            var result = await _db.RolePermissions
                .AsNoTracking()
                .Where(p => p.RoleId == roleId)
                .Select(p => MapToReadDto(p))
                .ToListAsync();

            return Ok(result);
        }

        // DELETE api/roles/5/permissions/Sales — revoke access to a single module.
        [HttpDelete("{module}")]
        public async Task<IActionResult> DeleteOne(int roleId, string module)
        {
            var row = await _db.RolePermissions
                .FirstOrDefaultAsync(p => p.RoleId == roleId && p.Module.ToLower() == module.ToLower());

            if (row is null)
                return NotFoundResponse($"No permission row for module '{module}' on role {roleId}.");

            _db.RolePermissions.Remove(row);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static RolePermissionReadDto MapToReadDto(RolePermission p) => new()
        {
            Id = p.Id,
            Module = p.Module,
            CanView = p.CanView,
            CanCreate = p.CanCreate,
            CanEdit = p.CanEdit,
            CanDelete = p.CanDelete
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
    }
}
```

### `Rolescontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Other;

namespace PharmacyV2.Controllers
{
    // CRUD for Role, which AppUser.RoleId and AuthController's Register
    // endpoint depend on. Permission assignment (RolePermission) is a
    // separate module, not covered here — this is just the role itself.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public RolesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/roles
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RoleReadDto>>> GetAll()
        {
            var roles = await _db.Roles
                .AsNoTracking()
                .Select(r => new RoleReadDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    IsActive = r.IsActive,
                    UserCount = r.Users.Count
                })
                .ToListAsync();

            return Ok(roles);
        }

        // GET api/roles/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<RoleReadDto>> GetById(int id)
        {
            var role = await _db.Roles
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new RoleReadDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    IsActive = r.IsActive,
                    UserCount = r.Users.Count
                })
                .FirstOrDefaultAsync();

            if (role is null)
                return NotFoundResponse($"Role {id} not found.");

            return Ok(role);
        }

        // POST api/roles — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<RoleReadDto>> Create([FromBody] RoleWriteDto dto)
        {
            if (await _db.Roles.AnyAsync(r => r.Name == dto.Name))
                return ConflictResponse($"Role '{dto.Name}' already exists.");

            var role = new Role
            {
                Name = dto.Name,
                Description = dto.Description,
                IsActive = dto.IsActive
            };

            _db.Roles.Add(role);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = role.Id }, new RoleReadDto
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                IsActive = role.IsActive,
                UserCount = 0
            });
        }

        // PUT api/roles/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] RoleWriteDto dto)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
            if (role is null)
                return NotFoundResponse($"Role {id} not found.");

            if (await _db.Roles.AnyAsync(r => r.Name == dto.Name && r.Id != id))
                return ConflictResponse($"Role '{dto.Name}' already exists.");

            role.Name = dto.Name;
            role.Description = dto.Description;
            role.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/roles/5 — blocked while any user still holds this role.
        // (DB-level FK is SetNull, which would silently strip users of their
        // role instead of failing — blocking here is the safer default for
        // an access-control table.)
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
            if (role is null)
                return NotFoundResponse($"Role {id} not found.");

            if (await _db.AppUsers.AnyAsync(u => u.RoleId == id))
                return ConflictResponse("Cannot delete a role that users are assigned to.");

            _db.Roles.Remove(role);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `SaleReturnsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.SaleInvoice;
using PharmacyV2.Services;
using PharmacyV2.Enums;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // Master-detail pair: SaleReturn -> SaleReturnItem. The models for this
    // already existed but had no controller — added here, following the
    // same header+items convention as PurchaseOrdersController.
    //
    // Create() credits stock back through the original SaleItem link (it
    // knows exactly which ProductStock batch to restore, and recomputes
    // Product.StockQuantity same as SalesController). The dedicated
    // AddItem/UpdateItem/RemoveItem detail-row endpoints below are a
    // different, SaleItem-less path (MedicineId only, no ProductStockId) —
    // there's no way to know which batch to credit/debit from those, so
    // they intentionally leave stock untouched. Prefer Create() for normal
    // returns; only reach for the detail-row endpoints when editing an
    // existing return's line items directly.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SaleReturnsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;
        private readonly IAuditService _audit;
        public SaleReturnsController(PharmacyDbContext db, IAccountingPostingService accounting, IAuditService audit) { _db = db; _accounting = accounting; _audit = audit; }

        // GET api/salereturns?saleId=3
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SaleReturnReadDto>>> GetAll([FromQuery] int? saleId)
        {
            var query = _db.SaleReturns
                .Include(r => r.Items).ThenInclude(i => i.Medicine)
                .Include(r => r.Items).ThenInclude(i => i.Unit).Include(r => r.Refunds)
                .AsNoTracking().AsQueryable();
            if (saleId is not null) query = query.Where(r => r.SaleId == saleId);

            var returns = await query.OrderByDescending(r => r.ReturnDate).ToListAsync();
            return Ok(returns.Select(ToDto));
        }

        // GET api/salereturns/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SaleReturnReadDto>> GetById(int id)
        {
            var ret = await _db.SaleReturns
                .Include(r => r.Items).ThenInclude(i => i.Medicine)
                .Include(r => r.Items).ThenInclude(i => i.Unit).Include(r => r.Refunds)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);
            return ret is null ? NotFound(new ApiError($"Sale return {id} not found.")) : Ok(ToDto(ret));
        }

        // POST api/salereturns — header + line items in one call.
        // ReturnNo must be unique; SaleId must already exist. ReturnTotal is
        // always computed server-side from the items.
        [HttpPost]
        public async Task<ActionResult<SaleReturnReadDto>> Create([FromBody] SaleReturnCreateDto dto)
        {
            if (await _db.SaleReturns.AnyAsync(r => r.ReturnNo == dto.ReturnNo))
                return Conflict(new ApiError($"Return number '{dto.ReturnNo}' already exists."));

            var originalSale = await _db.Sales.FirstOrDefaultAsync(s => s.SaleId == dto.SaleId);
            if (originalSale is null) return BadRequest(new ApiError($"Sale {dto.SaleId} does not exist."));

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var affectedProductIds = new List<int>();
            var ret = new SaleReturn
            {
                ReturnNo = dto.ReturnNo,
                ReturnDate = dto.ReturnDate,
                SaleId = dto.SaleId,
                Reason = dto.Reason,
                ReceiptImagePath = dto.ReceiptImagePath,
                ReceiptImage = dto.ReceiptImage,
                ReceiptImageContentType = dto.ReceiptImageContentType
            };

            if (dto.Items is { Count: > 0 })
            {
                foreach (var itemDto in dto.Items)
                {
                    var source = await _db.SaleItems.Include(x => x.ProductStocks).FirstOrDefaultAsync(x => x.SaleItemId == itemDto.SaleItemId && x.SaleId == dto.SaleId);
                    if (source is null) return BadRequest(new ApiError("Return item must reference an original sale item."));
                    var returned = await _db.SaleReturnItems.Where(x => x.SaleItemId == source.SaleItemId).SumAsync(x => (decimal?)x.Quantity) ?? 0;
                    if (itemDto.Quantity <= 0 || returned + itemDto.Quantity > source.Quantity) return Conflict(new ApiError("Return quantity exceeds the original sold quantity."));
                    var returnedBase = itemDto.Quantity * source.BaseQuantity / source.Quantity;
                    source.ProductStocks.AvailableQuantity += returnedBase;
                    affectedProductIds.Add(source.ProductStocks.ProductId);

                    ret.Items.Add(new SaleReturnItem
                    {
                        SaleItemId = source.SaleItemId, MedicineId = source.ProductStocks.ProductId, UnitId = source.UnitId,
                        Quantity = itemDto.Quantity,
                        BaseQuantity = returnedBase, SalesPrice = source.UnitPrice, SubTotal = itemDto.Quantity * source.UnitPrice
                    });
                }
            }

            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);

            _db.SaleReturns.Add(ret);
            // Persist the restored batch quantities before calculating the
            // denormalized Product.StockQuantity. EF's SUM query below runs
            // against SQL and cannot see a tracked-but-unsaved increase to
            // ProductStock.AvailableQuantity.
            await _db.SaveChangesAsync();
            // Keeps Product.StockQuantity (the "Stock" column on the Products
            // page) honest — it's a denormalized SUM of every batch's
            // AvailableQuantity, and only self-heals when something
            // recomputes it. SalesController does this on every sale;
            // returns need the same treatment or the batch-level quantity
            // above is correct but the Products page shows a stale number.
            await RecalculateStockQuantityAsync(affectedProductIds);
            await _db.SaveChangesAsync();
            var cost = ret.Items.Sum(x => x.BaseQuantity * _db.SaleItems.Where(s => s.SaleItemId == x.SaleItemId).Select(s => s.ProductStocks.UnitCost).First());
            // Credit Accounts Receivable first. It either reduces the
            // customer's outstanding due or becomes store credit. A refund
            // is a later, separately auditable cash/bank payment.
            await _accounting.PostAsync(LedgerSourceType.SaleReturn, ret.Id, ret.ReturnDate, $"Sale return {ret.ReturnNo}", ("4100", ret.ReturnTotal, 0), ("1100", 0, ret.ReturnTotal), ("1200", cost, 0), ("5000", 0, cost));
            await _db.SaveChangesAsync();
            await _audit.LogAsync("SaleReturnCreated", "SaleReturn", ret.Id, $"Return total/customer credit: {ret.ReturnTotal}", User.FindFirstValue(ClaimTypes.NameIdentifier));
            await transaction.CommitAsync();

            await _db.Entry(ret).Collection(r => r.Items).Query()
                .Include(i => i.Medicine).Include(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = ret.Id }, ToDto(ret));
        }

        [HttpPost("{id:int}/refunds")]
        public async Task<IActionResult> Refund(int id, [FromBody] SaleReturnRefundDto dto)
        {
            var ret = await _db.SaleReturns.Include(r => r.Refunds).FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
            if (ret is null) return NotFound(new ApiError($"Sale return {id} not found."));
            var refunded = ret.Refunds.Sum(r => r.Amount);
            if (dto.Amount > ret.ReturnTotal - refunded) return BadRequest(new ApiError("Refund amount cannot exceed available customer credit."));
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var refund = new SaleReturnRefund { SaleReturnId = id, Amount = dto.Amount, PaymentMethod = dto.PaymentMethod, Note = dto.Note, RefundedAt = DateTime.UtcNow, RefundedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) };
            _db.SaleReturnRefunds.Add(refund);
            await _db.SaveChangesAsync();
            var account = dto.PaymentMethod == "Card" ? "1010" : dto.PaymentMethod == "Mobile Banking" ? "1020" : "1000";
            await _accounting.PostAsync(LedgerSourceType.SaleReturnRefund, refund.Id, refund.RefundedAt, $"Customer refund for sale return {ret.ReturnNo}", ("1100", refund.Amount, 0), (account, 0, refund.Amount));
            await _audit.LogAsync("SaleReturnRefundPaid", "SaleReturn", ret.Id, $"Amount: {refund.Amount}; Method: {dto.PaymentMethod}", refund.RefundedByUserId);
            await transaction.CommitAsync();
            return NoContent();
        }

        // PUT api/salereturns/5 — header fields only (Reason). Use the items
        // endpoints below to add/update/remove line items.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaleReturnUpdateDto dto)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.SaleReturn && l.SourceId == id)) return Conflict(new ApiError("Posted sale returns are immutable."));
            var existing = await _db.SaleReturns.FirstOrDefaultAsync(r => r.Id == id);
            if (existing is null) return NotFound(new ApiError($"Sale return {id} not found."));

            existing.Reason = dto.Reason;
            if (dto.ReceiptImagePath is not null) existing.ReceiptImagePath = dto.ReceiptImagePath;
            if (dto.ReceiptImage is not null) existing.ReceiptImage = dto.ReceiptImage;
            if (dto.ReceiptImageContentType is not null) existing.ReceiptImageContentType = dto.ReceiptImageContentType;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/salereturns/5 — soft delete (IsDeleted flag), matching
        // the SaleReturn.IsDeleted column already on the model.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var ret = await _db.SaleReturns.FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Sale return {id} not found."));

            ret.IsDeleted = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/salereturns/5/items — append one line item to an existing return.
        [HttpPost("{id:int}/items")]
        public async Task<ActionResult<SaleReturnItemReadDto>> AddItem(int id, [FromBody] SaleReturnItemWriteDto dto)
        {
            var ret = await _db.SaleReturns.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Sale return {id} not found."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.MedicineId))
                return BadRequest(new ApiError($"Product {dto.MedicineId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            var item = new SaleReturnItem
            {
                SaleReturnId = id,
                MedicineId = dto.MedicineId,
                UnitId = dto.UnitId,
                Quantity = dto.Quantity,
                BaseQuantity = dto.BaseQuantity,
                SalesPrice = dto.SalesPrice,
                SubTotal = dto.Quantity * dto.SalesPrice
            };

            ret.Items.Add(item);
            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);
            await _db.SaveChangesAsync();

            await _db.Entry(item).Reference(i => i.Medicine).LoadAsync();
            await _db.Entry(item).Reference(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id }, ItemToDto(item));
        }

        // PUT api/salereturns/5/items/12 — update a single line item.
        [HttpPut("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] SaleReturnItemWriteDto dto)
        {
            var ret = await _db.SaleReturns.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Sale return {id} not found."));

            var item = ret.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for sale return {id}."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.MedicineId))
                return BadRequest(new ApiError($"Product {dto.MedicineId} does not exist."));
            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            item.MedicineId = dto.MedicineId;
            item.UnitId = dto.UnitId;
            item.Quantity = dto.Quantity;
            item.BaseQuantity = dto.BaseQuantity;
            item.SalesPrice = dto.SalesPrice;
            item.SubTotal = dto.Quantity * dto.SalesPrice;

            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/salereturns/5/items/12 — remove a single line item.
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var ret = await _db.SaleReturns.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Sale return {id} not found."));

            var item = ret.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for sale return {id}."));

            _db.SaleReturnItems.Remove(item);
            ret.Items.Remove(item);
            ret.ReturnTotal = ret.Items.Sum(i => i.SubTotal);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/salereturns/5/receipt-image — upload/replace the receipt
        // image as an actual file (multipart/form-data). In Postman: Body ->
        // form-data -> key "file", type "File" -> pick an image from disk.
        [HttpPost("{id:int}/receipt-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var ret = await _db.SaleReturns.FirstOrDefaultAsync(r => r.Id == id);
            if (ret is null) return NotFound(new ApiError($"Sale return {id} not found."));

            if (file is null || file.Length == 0)
                return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ret.ReceiptImage = ms.ToArray();
            ret.ReceiptImageContentType = file.ContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // Keeps Product.StockQuantity honest — total AvailableQuantity across
        // ALL ProductStock batches for that product. Same convention as
        // SalesController.RecalculateStockQuantityAsync /
        // PurchaseInvoicesController's version: always recomputed from
        // scratch via SUM, so it self-heals no matter what order things
        // happen in. Callers must SaveChangesAsync separately after this.
        private async Task RecalculateStockQuantityAsync(IEnumerable<int> productIds)
        {
            foreach (var productId in productIds.Distinct())
            {
                var total = await _db.ProductStocks
                    .Where(s => s.ProductId == productId)
                    .SumAsync(s => (decimal?)s.AvailableQuantity) ?? 0m;

                var product = await _db.Products.FindAsync(productId);
                if (product is not null)
                    product.StockQuantity = (int)total;
            }
        }

        private static SaleReturnItemReadDto ItemToDto(SaleReturnItem i) => new()
        {
            Id = i.Id,
            MedicineId = i.MedicineId,
            MedicineName = i.Medicine?.ProductName,
            UnitId = i.UnitId,
            UnitName = i.Unit?.Name,
            Quantity = i.Quantity,
            BaseQuantity = i.BaseQuantity,
            SalesPrice = i.SalesPrice,
            SubTotal = i.SubTotal
        };

        private static SaleReturnReadDto ToDto(SaleReturn r) => new()
        {
            Id = r.Id,
            ReturnNo = r.ReturnNo,
            ReturnDate = r.ReturnDate,
            SaleId = r.SaleId,
            ReturnTotal = r.ReturnTotal,
            Reason = r.Reason,
            IsDeleted = r.IsDeleted,
            ReceiptImagePath = r.ReceiptImagePath,
            ReceiptImage = r.ReceiptImage,
            ReceiptImageContentType = r.ReceiptImageContentType,
            Items = r.Items.Select(ItemToDto).ToList(),
            RefundedAmount = r.Refunds.Sum(x => x.Amount),
            CustomerCredit = r.ReturnTotal - r.Refunds.Sum(x => x.Amount)
        };
    }
}
```

### `SalesController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.SaleInvoice;
using System.Security.Claims;
using PharmacyV2.Services;
using PharmacyV2.Enums;

namespace PharmacyV2.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SalesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IAccountingPostingService _accounting;
        private readonly IAuditService _audit;
        private readonly ICodeGeneratorService _codeGenerator;

        // Folder (under wwwroot) where receipt images physically live —
        // same pattern as EmployeesController.EmployeeImagesRelativeFolder.
        private const string SaleReceiptImagesRelativeFolder = "images/sale-receipts";

        public SalesController(PharmacyDbContext db, IWebHostEnvironment env, IAccountingPostingService accounting, IAuditService audit, ICodeGeneratorService codeGenerator)
        {
            _db = db;
            _env = env;
            _accounting = accounting;
            _audit = audit;
            _codeGenerator = codeGenerator;
        }

        // wwwroot may not exist yet on a fresh clone. Creates
        // "wwwroot/images/sale-receipts" on demand the first time it's needed.
        private string GetSaleReceiptImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "sale-receipts");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/sales?customerId=3 — list sales, optionally filtered by customer.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SaleReadDto>>> GetAll([FromQuery] int? customerId)
        {
            var query = _db.Sales.Where(s => !s.IsVoided).Include(s => s.Items).ThenInclude(i => i.Unit).Include(s => s.Items).ThenInclude(i => i.ProductStocks).ThenInclude(stock => stock.Product).Include(s => s.Customer).Include(s => s.Payments).AsNoTracking().AsQueryable();
            if (customerId is not null) query = query.Where(s => s.CustomerId == customerId);

            var sales = await query.OrderByDescending(s => s.SaleDate).ToListAsync();

            var saleIds = sales.Select(s => s.SaleId).ToList();
            var returnTotals = await _db.SaleReturns.AsNoTracking()
                .Where(r => !r.IsDeleted && saleIds.Contains(r.SaleId))
                .GroupBy(r => r.SaleId)
                .Select(g => new { SaleId = g.Key, Total = g.Sum(x => x.ReturnTotal) })
                .ToListAsync();
            var returnMap = returnTotals.ToDictionary(x => x.SaleId, x => x.Total);

            return Ok(sales.Select(s => ToDto(s, returnMap.GetValueOrDefault(s.SaleId))));
        }

        // GET api/sales/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SaleReadDto>> GetById(int id)
        {
            var sale = await _db.Sales.Include(s => s.Items).ThenInclude(i => i.Unit).Include(s => s.Items).ThenInclude(i => i.ProductStocks).ThenInclude(stock => stock.Product).Include(s => s.Customer).Include(s => s.Payments).AsNoTracking()
                .FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound();

            var returnedAmount = await _db.SaleReturns.AsNoTracking()
                .Where(r => !r.IsDeleted && r.SaleId == id)
                .SumAsync(r => (decimal?)r.ReturnTotal) ?? 0m;

            return Ok(ToDto(sale, returnedAmount));
        }

        // POST api/sales — record a sale with its line items in one call.
        // For each item, checks that the ProductStock batch has enough
        // AvailableQuantity, then decrements it — this is the point stock
        // actually leaves the warehouse.
        // NOTE: ReceiptImage is no longer accepted here as raw bytes — upload
        // it afterwards via POST /api/sales/{id}/receipt-image (multipart file).
        [HttpPost]
        public async Task<ActionResult<SaleReadDto>> Create([FromBody] SaleCreateDto dto)
        {
            if (dto.CustomerId is not null && !await _db.Customers.AnyAsync(c => c.CustomerId == dto.CustomerId))
                return BadRequest(new ApiError($"Customer {dto.CustomerId} does not exist."));

            // InvoiceNo is always system-generated (GUID-backed, see
            // ICodeGeneratorService) — there is no client-supplied value to
            // read or validate here.
            var generatedInvoiceNo = await _codeGenerator.GenerateSaleInvoiceNoAsync();

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var sale = new Sale
            {
                InvoiceNo = generatedInvoiceNo,
                CustomerId = dto.CustomerId,
                SaleDate = dto.SaleDate == default ? DateTime.UtcNow : dto.SaleDate,
                PaymentMethod = dto.PaymentMethod,
                CashierId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                IsPaid = dto.IsPaid ?? false
            };

            var affectedProductIds = new List<int>();
            decimal costOfGoodsSold = 0;

            foreach (var itemDto in dto.Items)
            {
                // Each line item can now be sold in its own unit (Pcs, Strip,
                // Bottle...) — validated per item instead of once on the header.
                if (!await _db.Units.AnyAsync(u => u.Id == itemDto.UnitId))
                    return BadRequest(new ApiError($"Unit {itemDto.UnitId} does not exist."));

                var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == itemDto.ProductStockId);
                if (stock is null)
                    return BadRequest(new ApiError($"Product stock batch {itemDto.ProductStockId} does not exist."));
                var terms = await GetSaleTermsAsync(stock.ProductId, itemDto.UnitId);
                if (terms is null) return BadRequest(new ApiError($"Unit {itemDto.UnitId} is not configured for this product."));
                decimal baseQuantity = itemDto.Quantity * terms.Value.BaseQuantity;
                if (stock.AvailableQuantity < baseQuantity)
                    return Conflict(new ApiError($"Insufficient stock for batch {stock.BatchNumber}: available {stock.AvailableQuantity}, requested {itemDto.Quantity}."));

                // UnitPrice is no longer trusted from the client — it's pulled
                // live from Product.SalePrice so it always reflects whatever
                // that column currently holds (see GetCurrentSalePriceAsync).
                decimal unitPrice = terms.Value.UnitPrice;

                stock.AvailableQuantity -= baseQuantity;
                costOfGoodsSold += stock.UnitCost * baseQuantity;
                affectedProductIds.Add(stock.ProductId);

                sale.Items.Add(new SaleItem
                {
                    ProductStockId = itemDto.ProductStockId,
                    UnitId = itemDto.UnitId,
                    Quantity = itemDto.Quantity,
                    BaseQuantity = baseQuantity,
                    UnitPrice = unitPrice
                    // TotalPrice is DB-computed; never set it here
                });
            }

            // TotalAmount used to be trusted verbatim from the client, which
            // let it drift from what the line items actually add up to.
            // It's always derived from Quantity * UnitPrice per item now,
            // using the server-resolved UnitPrice (Product.SalePrice), not
            // whatever the client sent.
            sale.TotalAmount = sale.Items.Sum(i => i.Quantity * i.UnitPrice);

            _db.Sales.Add(sale);
            await _db.SaveChangesAsync();
            var cashCode = sale.IsPaid == true ? PaymentAccountCode(sale.PaymentMethod) : "1100";
            await _accounting.PostAsync(LedgerSourceType.Sale, sale.SaleId, sale.SaleDate, $"Sale {sale.SaleId}",
                (cashCode, sale.TotalAmount, 0), ("4000", 0, sale.TotalAmount), ("5000", costOfGoodsSold, 0), ("1200", 0, costOfGoodsSold));
            await _db.SaveChangesAsync();

            // Stock actually left the warehouse above — roll that into
            // Product.StockQuantity now (same pattern as PurchaseInvoicesController).
            await RecalculateStockQuantityAsync(affectedProductIds);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("SaleCreated", "Sale", sale.SaleId, $"Total: {sale.TotalAmount}; Paid: {sale.IsPaid}; Payment method: {sale.PaymentMethod}", sale.CashierId);
            await transaction.CommitAsync();

            await _db.Entry(sale).Reference(s => s.Customer).LoadAsync();
            await _db.Entry(sale).Collection(s => s.Items).LoadAsync();
            foreach (var item in sale.Items)
                await _db.Entry(item).Reference(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = sale.SaleId }, ToDto(sale));
        }

        // POST api/sales/5/payment — settles a previously unpaid sale. The
        // original sale stays immutable; this creates a separate Dr cash/bank
        // Cr accounts-receivable entry and then marks the invoice paid.
        [HttpPost("{id:int}/payment")]
        public async Task<ActionResult<SaleReadDto>> RecordPayment(int id, [FromBody] SalePaymentDto dto)
        {
            var sale = await _db.Sales.Include(s => s.Items).ThenInclude(i => i.Unit).Include(s => s.Customer).Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound(new ApiError($"Sale {id} not found."));
            if (sale.IsVoided) return Conflict(new ApiError("A voided sale cannot receive payment."));
            if (sale.IsPaid == true) return Conflict(new ApiError("This sale has already been paid."));

            // Older pending records may have been posted to Cash before the
            // receivable workflow existed. Do not create a duplicate receipt
            // for those; only normalize their status and payment method.
            var wasPostedToReceivable = await _db.LedgerAccounts
                .Include(l => l.ChartOfAccount)
                .AnyAsync(l => l.SourceType == LedgerSourceType.Sale && l.SourceId == id && l.ChartOfAccount.Code == "1100" && l.DebitAmount > 0);

            var alreadyPaid = sale.Payments.Sum(p => p.Amount);
            var remaining = sale.TotalAmount - alreadyPaid;
            var amount = dto.Amount ?? remaining;
            if (amount <= 0 || amount > remaining) return BadRequest(new ApiError($"Payment must be between 0.01 and the remaining due of {remaining:0.00}."));
            if (!wasPostedToReceivable) return Conflict(new ApiError("This legacy sale was not posted to accounts receivable; it cannot be collected again automatically."));

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var payment = new SalePayment
            {
                SaleId = sale.SaleId,
                Amount = amount,
                PaymentMethod = dto.PaymentMethod,
                Note = dto.Note,
                ReceivedAt = DateTime.UtcNow,
                ReceivedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            };
            _db.SalePayments.Add(payment);
            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.CustomerPayment, payment.Id, payment.ReceivedAt, $"Payment received for Sale {sale.SaleId}",
                (PaymentAccountCode(dto.PaymentMethod), amount, 0), ("1100", 0, amount));
            sale.PaymentMethod = dto.PaymentMethod;
            sale.IsPaid = alreadyPaid + amount >= sale.TotalAmount;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("CustomerPaymentReceived", "Sale", sale.SaleId, $"Amount: {amount}; Method: {dto.PaymentMethod}; Remaining due: {Math.Max(0, remaining - amount)}; Note: {dto.Note}", payment.ReceivedByUserId);
            await transaction.CommitAsync();
            return Ok(ToDto(sale));
        }

        // POST api/sales/5/void — reverses the original stock and accounting
        // entries and preserves the sale as an audit record. This is the safe
        // replacement for deleting a completed invoice.
        [HttpPost("{id:int}/void")]
        public async Task<IActionResult> Void(int id, [FromBody] SaleVoidDto dto)
        {
            var sale = await _db.Sales.Include(s => s.Items).ThenInclude(i => i.ProductStocks)
                .FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound(new ApiError($"Sale {id} not found."));
            if (sale.IsVoided) return Conflict(new ApiError("This sale has already been voided."));
            if (await _db.SaleReturns.AnyAsync(r => r.SaleId == id && !r.IsDeleted))
                return Conflict(new ApiError("A sale with returns cannot be voided. Complete a correcting sale return instead."));

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var affectedProductIds = new List<int>();
            foreach (var item in sale.Items)
            {
                item.ProductStocks.AvailableQuantity += item.BaseQuantity;
                affectedProductIds.Add(item.ProductStocks.ProductId);
            }

            var originalPosting = await _db.LedgerAccounts.Include(l => l.ChartOfAccount)
                .Where(l => l.SourceType == LedgerSourceType.Sale && l.SourceId == id)
                .ToListAsync();
            if (originalPosting.Count > 0)
            {
                await _accounting.PostAsync(LedgerSourceType.SaleVoid, sale.SaleId, DateTime.UtcNow,
                    $"Void Sale {sale.SaleId}: {dto.Reason}", originalPosting
                    .Select(line => (line.ChartOfAccount.Code!, line.CreditAmount, line.DebitAmount)).ToArray());
            }
            sale.IsVoided = true;
            sale.VoidReason = dto.Reason;
            sale.VoidedAt = DateTime.UtcNow;
            sale.VoidedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await RecalculateStockQuantityAsync(affectedProductIds);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("SaleVoided", "Sale", sale.SaleId, dto.Reason, sale.VoidedByUserId);
            await transaction.CommitAsync();
            return NoContent();
        }

        // PUT api/sales/5 — updates header fields, and optionally does a full
        // add/update/delete sync of the line items (same pattern as
        // EmployeesController's Documents / SuppliersController's Contacts),
        // reconciling ProductStock.AvailableQuantity and TotalAmount either way.
        // Omit "items" from the body to leave line items untouched.
        // Receipt image is managed only via the dedicated endpoints below.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SaleUpdateDto dto)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.Sale && l.SourceId == id))
                return Conflict(new ApiError("Posted sales are immutable. Create a sale return or a correcting sale instead."));
            var existing = await _db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.SaleId == id);
            if (existing is null) return NotFound();

            if (dto.CustomerId is not null && !await _db.Customers.AnyAsync(c => c.CustomerId == dto.CustomerId))
                return BadRequest(new ApiError($"Customer {dto.CustomerId} does not exist."));

            existing.PaymentMethod = dto.PaymentMethod;
            // The original cashier is an audit field and must never be supplied by a client update.
            existing.CustomerId = dto.CustomerId;
            existing.IsPaid = dto.IsPaid;
            // dto.SaleDate was previously missing from SaleUpdateDto entirely,
            // so this line never existed and SaleDate could never change here.
            if (dto.SaleDate is not null)
                existing.SaleDate = dto.SaleDate.Value;

            var affectedProductIds = new List<int>();

            if (dto.Items is not null)
            {
                var incomingIds = dto.Items.Where(i => i.SaleItemId is > 0).Select(i => i.SaleItemId!.Value).ToHashSet();

                // items left out of the payload => remove, restoring the stock they held
                var toRemove = existing.Items.Where(i => !incomingIds.Contains(i.SaleItemId)).ToList();
                foreach (var item in toRemove)
                {
                    var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == item.ProductStockId);
                    if (stock is not null)
                    {
                        stock.AvailableQuantity += item.BaseQuantity;
                        affectedProductIds.Add(stock.ProductId);
                    }

                    _db.SaleItems.Remove(item);
                    existing.Items.Remove(item);
                }

                foreach (var itemDto in dto.Items)
                {
                    if (itemDto.SaleItemId is > 0)
                    {
                        var existingItem = existing.Items.FirstOrDefault(i => i.SaleItemId == itemDto.SaleItemId);
                        if (existingItem is null)
                            return BadRequest(new ApiError($"Item {itemDto.SaleItemId} does not belong to sale {id}."));

                        if (!await _db.Units.AnyAsync(u => u.Id == itemDto.UnitId))
                            return BadRequest(new ApiError($"Unit {itemDto.UnitId} does not exist."));

                        // release the stock this line previously held...
                        var oldStock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == existingItem.ProductStockId);
                        if (oldStock is not null)
                        {
                            oldStock.AvailableQuantity += existingItem.BaseQuantity;
                            affectedProductIds.Add(oldStock.ProductId);
                        }

                        // ...then re-reserve against the (possibly new) batch/quantity
                        var newStock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == itemDto.ProductStockId);
                        if (newStock is null)
                            return BadRequest(new ApiError($"Product stock batch {itemDto.ProductStockId} does not exist."));
                        var updateTerms = await GetSaleTermsAsync(newStock.ProductId, itemDto.UnitId);
                        if (updateTerms is null) return BadRequest(new ApiError($"Unit {itemDto.UnitId} is not configured for this product."));
                        decimal updatedBaseQuantity = itemDto.Quantity * updateTerms.Value.BaseQuantity;
                        if (newStock.AvailableQuantity < updatedBaseQuantity)
                            return Conflict(new ApiError($"Insufficient stock for batch {newStock.BatchNumber}: available {newStock.AvailableQuantity}, requested {itemDto.Quantity}."));

                        newStock.AvailableQuantity -= updatedBaseQuantity;
                        affectedProductIds.Add(newStock.ProductId);

                        // Existing sales retain their recorded price. A master
                        // product-price change is never allowed to rewrite
                        // previous accounting when this sale is edited.
                        bool priceBasisChanged = existingItem.UnitId != itemDto.UnitId || existingItem.ProductStockId != itemDto.ProductStockId;
                        existingItem.ProductStockId = itemDto.ProductStockId;
                        existingItem.UnitId = itemDto.UnitId;
                        existingItem.Quantity = itemDto.Quantity;
                        existingItem.BaseQuantity = updatedBaseQuantity;
                        if (priceBasisChanged)
                            existingItem.UnitPrice = updateTerms.Value.UnitPrice;
                    }
                    else
                    {
                        if (!await _db.Units.AnyAsync(u => u.Id == itemDto.UnitId))
                            return BadRequest(new ApiError($"Unit {itemDto.UnitId} does not exist."));

                        var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == itemDto.ProductStockId);
                        if (stock is null)
                            return BadRequest(new ApiError($"Product stock batch {itemDto.ProductStockId} does not exist."));
                        var addTerms = await GetSaleTermsAsync(stock.ProductId, itemDto.UnitId);
                        if (addTerms is null) return BadRequest(new ApiError($"Unit {itemDto.UnitId} is not configured for this product."));
                        decimal addedBaseQuantity = itemDto.Quantity * addTerms.Value.BaseQuantity;
                        if (stock.AvailableQuantity < addedBaseQuantity)
                            return Conflict(new ApiError($"Insufficient stock for batch {stock.BatchNumber}: available {stock.AvailableQuantity}, requested {itemDto.Quantity}."));

                        stock.AvailableQuantity -= addedBaseQuantity;
                        affectedProductIds.Add(stock.ProductId);

                        existing.Items.Add(new SaleItem
                        {
                            ProductStockId = itemDto.ProductStockId,
                            UnitId = itemDto.UnitId,
                            Quantity = itemDto.Quantity,
                            BaseQuantity = addedBaseQuantity,
                            UnitPrice = addTerms.Value.UnitPrice
                            // TotalPrice is DB-computed; never set it here
                        });
                    }
                }

                existing.TotalAmount = existing.Items.Sum(i => i.Quantity * i.UnitPrice);
            }

            await _db.SaveChangesAsync();

            if (affectedProductIds.Count > 0)
            {
                await RecalculateStockQuantityAsync(affectedProductIds);
                await _db.SaveChangesAsync();
            }

            return NoContent();
        }

        // DELETE api/sales/5 — restores the stock it consumed, deletes the
        // physical receipt image (if any), then removes the sale.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.Sale && l.SourceId == id))
                return Conflict(new ApiError("Posted sales cannot be deleted. Use a sale return or an accounting reversal."));
            var sale = await _db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound();

            var affectedProductIds = new List<int>();
            foreach (var item in sale.Items)
            {
                var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == item.ProductStockId);
                if (stock is not null)
                {
                    stock.AvailableQuantity += item.BaseQuantity;
                    affectedProductIds.Add(stock.ProductId);
                }
            }

            DeleteReceiptImageFile(sale.ReceiptImagePath);

            _db.Sales.Remove(sale); // items cascade-delete with the sale
            await _db.SaveChangesAsync();

            if (affectedProductIds.Count > 0)
            {
                await RecalculateStockQuantityAsync(affectedProductIds);
                await _db.SaveChangesAsync();
            }

            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/sales/5/items — add one line item to an existing sale.
        [HttpPost("{id:int}/items")]
        public async Task<ActionResult<SaleItemReadDto>> AddItem(int id, [FromBody] SaleItemWriteDto dto)
        {
            var sale = await _db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound(new ApiError($"Sale {id} not found."));

            if (!await _db.Units.AnyAsync(u => u.Id == dto.UnitId))
                return BadRequest(new ApiError($"Unit {dto.UnitId} does not exist."));

            var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == dto.ProductStockId);
            if (stock is null)
                return BadRequest(new ApiError($"Product stock batch {dto.ProductStockId} does not exist."));
            var terms = await GetSaleTermsAsync(stock.ProductId, dto.UnitId);
            if (terms is null) return BadRequest(new ApiError($"Unit {dto.UnitId} is not configured for this product."));
            decimal baseQuantity = dto.Quantity * terms.Value.BaseQuantity;
            if (stock.AvailableQuantity < baseQuantity)
                return Conflict(new ApiError($"Insufficient stock for batch {stock.BatchNumber}: available {stock.AvailableQuantity}, requested {dto.Quantity}."));

            stock.AvailableQuantity -= baseQuantity;

            var item = new SaleItem
            {
                SaleId = id,
                ProductStockId = dto.ProductStockId,
                UnitId = dto.UnitId,
                Quantity = dto.Quantity,
                BaseQuantity = baseQuantity,
                // UnitPrice always re-pulled from Product.SalePrice — never
                // trusted from the client (see Create()).
                UnitPrice = terms.Value.UnitPrice
                // TotalPrice is DB-computed; never set it here
            };

            sale.Items.Add(item);
            sale.TotalAmount = sale.Items.Sum(i => i.Quantity * i.UnitPrice);
            await _db.SaveChangesAsync();

            await RecalculateStockQuantityAsync(new[] { stock.ProductId });
            await _db.SaveChangesAsync();

            await _db.Entry(item).ReloadAsync();
            await _db.Entry(item).Reference(i => i.Unit).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id }, new SaleItemReadDto
            {
                SaleItemId = item.SaleItemId,
                ProductStockId = item.ProductStockId,
                UnitId = item.UnitId,
                UnitName = item.Unit?.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalPrice
            });
        }

        // DELETE api/sales/5/items/12 — remove a single line item, restore
        // the stock it consumed, and recompute TotalAmount.
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var sale = await _db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound(new ApiError($"Sale {id} not found."));

            var item = sale.Items.FirstOrDefault(i => i.SaleItemId == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for sale {id}."));

            var stock = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == item.ProductStockId);
            if (stock is not null) stock.AvailableQuantity += item.BaseQuantity;

            _db.SaleItems.Remove(item);
            sale.Items.Remove(item);
            sale.TotalAmount = sale.Items.Sum(i => i.Quantity * i.UnitPrice);
            await _db.SaveChangesAsync();

            if (stock is not null)
            {
                await RecalculateStockQuantityAsync(new[] { stock.ProductId });
                await _db.SaveChangesAsync();
            }

            return NoContent();
        }

        // POST api/sales/5/receipt-image — upload/replace the receipt image
        // as an actual file (multipart/form-data), same pattern as
        // EmployeesController.UploadPhoto. In Postman: Body -> form-data ->
        // key "file", type "File" -> pick an image from disk.
        //
        // Saves the file physically under wwwroot/images/sale-receipts and
        // stores the short public path in Sale.ReceiptImagePath, e.g.
        // "/images/sale-receipts/2_a1b2c3d4.jpg" — no more base64 blobs
        // bloating the JSON responses.
        [HttpPost("{id:int}/image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var sale = await _db.Sales.FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound(new ApiError($"Sale {id} not found."));

            if (file is null || file.Length == 0)
                return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            bool isAllowed = false;
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            if (!isAllowed)
                return BadRequest(new ApiError("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp."));

            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            string folderPath = GetSaleReceiptImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteReceiptImageFile(sale.ReceiptImagePath);

            sale.ReceiptImage = null; // stop keeping raw bytes in the DB now that we have a file on disk
            sale.ReceiptImageContentType = file.ContentType;
            sale.ReceiptImagePath = "/" + SaleReceiptImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { sale.SaleId, sale.ReceiptImagePath });
        }

        // DELETE api/sales/5/receipt-image — remove the receipt image, both
        // the physical file under wwwroot/images/sale-receipts and the DB fields.
        [HttpDelete("{id:int}/image")]
        public async Task<IActionResult> DeleteReceiptImage(int id)
        {
            var sale = await _db.Sales.FirstOrDefaultAsync(s => s.SaleId == id);
            if (sale is null) return NotFound(new ApiError($"Sale {id} not found."));

            DeleteReceiptImageFile(sale.ReceiptImagePath);

            sale.ReceiptImagePath = null;
            sale.ReceiptImage = null;
            sale.ReceiptImageContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- helpers ----------

        // Deletes the physical file behind a ReceiptImagePath like
        // "/images/sale-receipts/2_a1b2...c1.jpg", if it exists. Safe to
        // call with null/empty/unrelated paths — it just does nothing then.
        private void DeleteReceiptImageFile(string? receiptImagePath)
        {
            if (string.IsNullOrWhiteSpace(receiptImagePath))
                return;

            if (receiptImagePath.IndexOf(SaleReceiptImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(receiptImagePath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetSaleReceiptImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private async Task<(decimal BaseQuantity, decimal UnitPrice)?> GetSaleTermsAsync(int productId, int unitId)
        {
            var product = await _db.Products.AsNoTracking().Include(p => p.ProductPrices)
                .FirstOrDefaultAsync(p => p.Id == productId);
            if (product is null) return null;
            if (product.UnitId == unitId) return (1m, product.SalePrice ?? product.UnitPrice);
            var package = product.ProductPrices.FirstOrDefault(p => p.UnitId == unitId);
            return package is null ? null : (package.BaseQuantity, package.PerUnitPrice);
        }

        private static string PaymentAccountCode(string paymentMethod) => paymentMethod switch
        {
            "Card" => "1010",
            "Mobile Banking" => "1020",
            _ => "1000"
        };

        // Resolves the price to charge for a base-unit line item straight from
        // Products.SalePrice — the same column an admin edits on the Product
        // record. Because Create/Update/AddItem always call this instead of
        // trusting the client's UnitPrice, a line item's price is whatever
        // SalePrice currently is at the moment the item is added — if
        // SalePrice is later changed on the product, that only affects new
        // sales made afterwards; it does not retroactively change past sales.
        // Falls back to Product.UnitPrice if SalePrice hasn't been set yet.
        private async Task<decimal> GetCurrentSalePriceAsync(int productId)
        {
            var product = await _db.Products.FindAsync(productId);
            return product is null ? 0m : (product.SalePrice ?? product.UnitPrice);
        }

        // Keeps Product.StockQuantity honest — total AvailableQuantity across
        // ALL ProductStock batches (every warehouse) for that product.
        // Always recomputed from scratch via SUM, same convention as
        // PurchaseInvoicesController.RecalculateStockQuantityAsync /
        // RecalculatePurchaseQtyAsync: self-heals no matter what order
        // things happen in. Callers must SaveChangesAsync separately after this.
        private async Task RecalculateStockQuantityAsync(IEnumerable<int> productIds)
        {
            foreach (var productId in productIds.Distinct())
            {
                var total = await _db.ProductStocks
                    .Where(s => s.ProductId == productId)
                    .SumAsync(s => (decimal?)s.AvailableQuantity) ?? 0m;

                var product = await _db.Products.FindAsync(productId);
                if (product is not null)
                    product.StockQuantity = (int)total;
            }
        }

        private static SaleReadDto ToDto(Sale s, decimal returnedAmount = 0) => new()
        {
            SaleId = s.SaleId,
            InvoiceNo = s.InvoiceNo,
            CustomerId = s.CustomerId,
            CustomerName = s.Customer is null ? null : $"{s.Customer.FirstName} {s.Customer.LastName}".Trim(),
            SaleDate = s.SaleDate,
            TotalAmount = s.TotalAmount,
            PaymentMethod = s.PaymentMethod,
            CashierId = s.CashierId,
            IsPaid = s.IsPaid,
            IsVoided = s.IsVoided,
            VoidReason = s.VoidReason,
            VoidedAt = s.VoidedAt,
            TotalPaid = s.IsPaid == true && s.Payments.Count == 0 ? s.TotalAmount : s.Payments.Sum(p => p.Amount),
            DueAmount = s.IsPaid == true && s.Payments.Count == 0 ? 0 : Math.Max(0, s.TotalAmount - s.Payments.Sum(p => p.Amount)),
            ReturnedAmount = returnedAmount,
            HasReturns = returnedAmount > 0,
            ReceiptImagePath = s.ReceiptImagePath,
            ReceiptImageContentType = s.ReceiptImageContentType,
            Items = s.Items.Select(i => new SaleItemReadDto
            {
                SaleItemId = i.SaleItemId,
                ProductStockId = i.ProductStockId,
                ProductName = i.ProductStocks?.Product?.ProductName,
                ProductImagePath = i.ProductStocks?.Product?.ImagePath,
                UnitId = i.UnitId,
                UnitName = i.Unit?.Name,
                Quantity = i.Quantity,
                BaseQuantity = i.BaseQuantity,
                UnitPrice = i.UnitPrice,
                TotalPrice = i.TotalPrice
            }).ToList()
        };
    }
}
```

### `Smslogscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Other;

namespace PharmacyV2.Controllers
{
    // API for SmsLog — an audit trail of SMS attempts (e.g. order/payment
    // notifications). Had a model + DbSet but no controller.
    //
    // This does NOT send SMS itself — there's no SMS-gateway configuration
    // anywhere in this project (no API keys/settings for one). POST here
    // just records the outcome of a send that happened elsewhere (a 3rd-party
    // gateway call from the frontend, or a future background service), so
    // there's a queryable history of what was sent and whether it succeeded.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SmsLogsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public SmsLogsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/smslogs — filterable audit list.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SmsLogReadDto>>> GetAll(
            [FromQuery] string? phoneNumber,
            [FromQuery] bool? isSuccess,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var query = _db.SmsLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(phoneNumber))
                query = query.Where(s => s.PhoneNumber == phoneNumber);
            if (isSuccess is not null)
                query = query.Where(s => s.IsSuccess == isSuccess);
            if (fromDate is not null)
                query = query.Where(s => s.SentAt >= fromDate);
            if (toDate is not null)
                query = query.Where(s => s.SentAt <= toDate);

            var logs = await query
                .OrderByDescending(s => s.SentAt)
                .Select(s => MapToReadDto(s))
                .ToListAsync();

            return Ok(logs);
        }

        // GET api/smslogs/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SmsLogReadDto>> GetById(int id)
        {
            var log = await _db.SmsLogs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (log is null)
                return NotFoundResponse($"SMS log {id} not found.");

            return Ok(MapToReadDto(log));
        }

        // POST api/smslogs — records the result of an SMS send attempt.
        [HttpPost]
        public async Task<ActionResult<SmsLogReadDto>> Create([FromBody] SmsLogCreateDto dto)
        {
            var log = new SmsLog
            {
                PhoneNumber = dto.PhoneNumber,
                Message = dto.Message,
                SentAt = DateTime.Now,
                IsSuccess = dto.IsSuccess,
                Response = dto.Response
            };

            _db.SmsLogs.Add(log);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = log.Id }, MapToReadDto(log));
        }

        // DELETE api/smslogs/5 — remove a single log entry.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var log = await _db.SmsLogs.FirstOrDefaultAsync(s => s.Id == id);
            if (log is null)
                return NotFoundResponse($"SMS log {id} not found.");

            _db.SmsLogs.Remove(log);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/smslogs/purge?olderThanDays=90 — bulk-clear old log
        // rows so this table doesn't grow forever. Returns how many were removed.
        [HttpDelete("purge")]
        public async Task<ActionResult<object>> Purge([FromQuery] int olderThanDays = 90)
        {
            if (olderThanDays < 1)
                return BadRequestResponse("olderThanDays must be at least 1.");

            var cutoff = DateTime.Now.AddDays(-olderThanDays);
            var toRemove = await _db.SmsLogs.Where(s => s.SentAt < cutoff).ToListAsync();

            _db.SmsLogs.RemoveRange(toRemove);
            await _db.SaveChangesAsync();

            return Ok(new { Deleted = toRemove.Count, CutoffDate = cutoff });
        }

        private static SmsLogReadDto MapToReadDto(SmsLog s) => new()
        {
            Id = s.Id,
            PhoneNumber = s.PhoneNumber,
            Message = s.Message,
            SentAt = s.SentAt,
            IsSuccess = s.IsSuccess,
            Response = s.Response
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
    }
}
```

### `StockTransfersController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Branch;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // Moves stock between two warehouses/branches.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class StockTransfersController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        public StockTransfersController(PharmacyDbContext db) => _db = db;

        // GET api/stocktransfers — every transfer with its line items.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<StockTransferReadDto>>> GetAll()
        {
            var transfers = await _db.StockTransfers
                .Include(t => t.Items).ThenInclude(i => i.Medicine)
                .Include(t => t.FromWarehouse)
                .Include(t => t.ToWarehouse)
                .AsNoTracking().ToListAsync();
            return Ok(transfers.Select(ToDto));
        }

        // GET api/stocktransfers/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<StockTransferReadDto>> GetById(int id)
        {
            var transfer = await _db.StockTransfers
                .Include(t => t.Items).ThenInclude(i => i.Medicine)
                .Include(t => t.FromWarehouse)
                .Include(t => t.ToWarehouse)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);
            return transfer is null ? NotFound() : Ok(ToDto(transfer));
        }

        // POST api/stocktransfers — create a transfer header + items in one call.
        // FromWarehouseId and ToWarehouseId must differ and must both exist.
        [HttpPost]
        public async Task<ActionResult<StockTransferReadDto>> Create([FromBody] StockTransferCreateDto dto)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var createdByUserId)) return Unauthorized();
            if (dto.FromWarehouseId == dto.ToWarehouseId)
                return BadRequest(new ApiError("FromWarehouseId and ToWarehouseId must be different."));

            var warehousesExist = await _db.Warehouses
                .Where(w => w.Id == dto.FromWarehouseId || w.Id == dto.ToWarehouseId)
                .CountAsync();
            if (warehousesExist < 2)
                return BadRequest(new ApiError("One or both warehouses do not exist."));

            var transfer = new StockTransfer
            {
                InvoiceId = dto.InvoiceId,
                TransferDate = dto.TransferDate == default ? DateTime.UtcNow : dto.TransferDate,
                FromWarehouseId = dto.FromWarehouseId,
                ToWarehouseId = dto.ToWarehouseId,
                CreatedByUserId = createdByUserId,
                ReceiptImagePath = dto.ReceiptImagePath,
                ReceiptImage = dto.ReceiptImage,
                ReceiptImageContentType = dto.ReceiptImageContentType
            };

            foreach (var itemDto in dto.Items)
            {
                var source = await _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == itemDto.SourceProductStockId && s.WarehouseId == dto.FromWarehouseId);
                if (source is null || source.ProductId != itemDto.MedicineId) return BadRequest(new ApiError("Selected source batch does not belong to the source warehouse/product."));
                if (source.AvailableQuantity < itemDto.Quantity) return Conflict(new ApiError("Insufficient quantity in selected source batch."));
                source.AvailableQuantity -= itemDto.Quantity;
                transfer.Items.Add(new StockTransferItem
                {
                    MedicineId = itemDto.MedicineId,
                    SourceProductStockId = source.Id,
                    Quantity = itemDto.Quantity,
                    ExpireDate = itemDto.ExpireDate
                });
                _db.ProductStocks.Add(new PharmacyV2.Models.Product.ProductStock { ProductId = source.ProductId, WarehouseId = dto.ToWarehouseId,
                    BatchNumber = $"{source.BatchNumber}-TR-{dto.InvoiceId}", Quantity = itemDto.Quantity, AvailableQuantity = itemDto.Quantity,
                    ExpiryDate = source.ExpiryDate, SupplierId = source.SupplierId, ReceivedDate = DateOnly.FromDateTime(dto.TransferDate), UnitCost = source.UnitCost });
            }

            transfer.TotalQty = transfer.Items.Sum(i => i.Quantity);

            _db.StockTransfers.Add(transfer);
            await _db.SaveChangesAsync();

            await _db.Entry(transfer).Reference(t => t.FromWarehouse).LoadAsync();
            await _db.Entry(transfer).Reference(t => t.ToWarehouse).LoadAsync();
            await _db.Entry(transfer).Collection(t => t.Items).Query().Include(i => i.Medicine).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = transfer.Id }, ToDto(transfer));
        }

        // PUT api/stocktransfers/5 — update header notes/invoice id (items are append/remove only).
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] StockTransferUpdateDto dto)
        {
            var existing = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (existing is null) return NotFound();

            existing.InvoiceId = dto.InvoiceId;
            existing.IsReceived = dto.IsReceived;

            // Receipt fields are optional on update — only overwrite when the
            // caller actually sends a value, so a PUT that omits them doesn't
            // wipe out a receipt attached earlier.
            if (dto.ReceiptImagePath is not null) existing.ReceiptImagePath = dto.ReceiptImagePath;
            if (dto.ReceiptImage is not null) existing.ReceiptImage = dto.ReceiptImage;
            if (dto.ReceiptImageContentType is not null) existing.ReceiptImageContentType = dto.ReceiptImageContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // GET api/stocktransfers/5/receipt-image — serves the actual image
        // bytes with the right content-type, instead of shipping them as
        // base64 inside every JSON response. <img src="/api/stocktransfers/5/receipt-image">
        // works directly against this.
        [HttpGet("{id:int}/receipt-image")]
        public async Task<IActionResult> GetReceiptImage(int id)
        {
            var transfer = await _db.StockTransfers
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transfer is null) return NotFound();
            if (transfer.ReceiptImage is null || transfer.ReceiptImage.Length == 0)
                return NotFound(new ApiError("This stock transfer has no receipt image."));

            return File(transfer.ReceiptImage, transfer.ReceiptImageContentType ?? "application/octet-stream");
        }

        // POST api/stocktransfers/5/receipt-image — upload/replace the
        // receipt image as an actual file (multipart/form-data). In Postman:
        // Body -> form-data -> key "file", type "File" -> pick an image.
        [HttpPost("{id:int}/receipt-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (transfer is null) return NotFound(new ApiError($"Stock transfer {id} not found."));

            if (file is null || file.Length == 0)
                return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            transfer.ReceiptImage = ms.ToArray();
            transfer.ReceiptImageContentType = file.ContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/stocktransfers/5/items — append one line item to an existing transfer.
        // Recomputes TotalQty.
        [HttpPost("{id:int}/items")]
        public async Task<ActionResult<StockTransferItemReadDto>> AddItem(int id, [FromBody] StockTransferItemWriteDto dto)
        {
            var transfer = await _db.StockTransfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
            if (transfer is null) return NotFound(new ApiError($"Stock transfer {id} not found."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.MedicineId))
                return BadRequest(new ApiError($"Product {dto.MedicineId} does not exist."));

            var item = new StockTransferItem
            {
                StockTransferId = id,
                MedicineId = dto.MedicineId,
                Quantity = dto.Quantity,
                ExpireDate = dto.ExpireDate
            };

            transfer.Items.Add(item);
            transfer.TotalQty = transfer.Items.Sum(i => i.Quantity);
            await _db.SaveChangesAsync();

            await _db.Entry(item).Reference(i => i.Medicine).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id }, new StockTransferItemReadDto
            {
                Id = item.Id,
                MedicineId = item.MedicineId,
                SourceProductStockId = item.SourceProductStockId,
                MedicineName = item.Medicine?.ProductName,
                Quantity = item.Quantity,
                ExpireDate = item.ExpireDate
            });
        }

        // PUT api/stocktransfers/5/items/12 — update a single line item.
        [HttpPut("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] StockTransferItemWriteDto dto)
        {
            var transfer = await _db.StockTransfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
            if (transfer is null) return NotFound(new ApiError($"Stock transfer {id} not found."));

            var item = transfer.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for stock transfer {id}."));

            if (!await _db.Products.AnyAsync(p => p.Id == dto.MedicineId))
                return BadRequest(new ApiError($"Product {dto.MedicineId} does not exist."));

            item.MedicineId = dto.MedicineId;
            item.Quantity = dto.Quantity;
            item.ExpireDate = dto.ExpireDate;

            transfer.TotalQty = transfer.Items.Sum(i => i.Quantity);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/stocktransfers/5/items/12 — remove a single line item. Recomputes TotalQty.
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var transfer = await _db.StockTransfers.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
            if (transfer is null) return NotFound(new ApiError($"Stock transfer {id} not found."));

            var item = transfer.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) return NotFound(new ApiError($"Item {itemId} not found for stock transfer {id}."));

            _db.StockTransferItems.Remove(item);
            transfer.Items.Remove(item);
            transfer.TotalQty = transfer.Items.Sum(i => i.Quantity);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/stocktransfers/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (transfer is null) return NotFound();

            _db.StockTransfers.Remove(transfer); // items cascade-delete with the transfer
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static StockTransferReadDto ToDto(StockTransfer t) => new()
        {
            Id = t.Id,
            InvoiceId = t.InvoiceId,
            TransferDate = t.TransferDate,
            FromWarehouseId = t.FromWarehouseId,
            FromWarehouseName = t.FromWarehouse?.Name,
            ToWarehouseId = t.ToWarehouseId,
            ToWarehouseName = t.ToWarehouse?.Name,
            TotalQty = t.TotalQty,
            CreatedByUserId = t.CreatedByUserId,
            IsReceived = t.IsReceived,
            ReceiptImagePath = t.ReceiptImagePath,
            HasReceiptImage = t.ReceiptImage is { Length: > 0 },
            ReceiptImageUrl = (t.ReceiptImage is { Length: > 0 })
                ? $"/api/stocktransfers/{t.Id}/receipt-image"
                : null,
            ReceiptImageContentType = t.ReceiptImageContentType,
            Items = t.Items.Select(i => new StockTransferItemReadDto
            {
                Id = i.Id,
                MedicineId = i.MedicineId,
                SourceProductStockId = i.SourceProductStockId,
                MedicineName = i.Medicine?.ProductName,
                Quantity = i.Quantity,
                ExpireDate = i.ExpireDate
            }).ToList()
        };
    }
}
```

### `SupplierPaymentsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Payment;
using PharmacyV2.Models.Purchase;
using PharmacyV2.Services;
using PharmacyV2.Enums;

namespace PharmacyV2.Controllers
{
    // Master (SupplierPayment) + details (SupplierPaymentDetail) CRUD.
    // Each detail row allocates part of this payment voucher toward a
    // specific PurchaseInvoice. Every write here also keeps that invoice's
    // Due/PaymentStatus fields in sync — see RecalculateInvoiceDueAsync.
    // PurchaseInvoicesController.RecalculateTotalsAsync uses the exact same
    // formula, so the two controllers never disagree about an invoice's Due
    // no matter which one touched it last.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SupplierPaymentsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;
        public SupplierPaymentsController(PharmacyDbContext db, IAccountingPostingService accounting) { _db = db; _accounting = accounting; }

        // GET api/supplierpayments?supplierId=3 — list vouchers, optionally by supplier.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SupplierPaymentReadDto>>> GetAll([FromQuery] int? supplierId)
        {
            var query = _db.SupplierPayments
                .Include(p => p.Supplier)
                .Include(p => p.Details).ThenInclude(d => d.PurchaseInvoice)
                .AsNoTracking().AsQueryable();
            if (supplierId is not null) query = query.Where(p => p.SupplierId == supplierId);

            var payments = await query.OrderByDescending(p => p.PaidDate).ToListAsync();
            return Ok(payments.Select(ToDto));
        }

        // GET api/supplierpayments/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SupplierPaymentReadDto>> GetById(int id)
        {
            var payment = await _db.SupplierPayments
                .Include(p => p.Supplier)
                .Include(p => p.Details).ThenInclude(d => d.PurchaseInvoice)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            return payment is null ? NotFoundResponse($"Supplier payment {id} not found.") : Ok(ToDto(payment));
        }

        // POST api/supplierpayments — record a new payment voucher, header +
        // allocation rows in one call. Every PurchaseInvoiceId in Details
        // must belong to this same SupplierId, and each row's PaidAmount
        // can't exceed that invoice's current Due.
        [HttpPost]
        public async Task<ActionResult<SupplierPaymentReadDto>> Create([FromBody] SupplierPaymentCreateDto dto)
        {
            if (await _db.SupplierPayments.AnyAsync(p => p.PaidNo == dto.PaidNo))
                return ConflictResponse($"Paid No '{dto.PaidNo}' already exists.");

            if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == dto.SupplierId))
                return BadRequestResponse($"Supplier {dto.SupplierId} does not exist.");

            var payment = new SupplierPayment
            {
                PaidNo = dto.PaidNo,
                PaidDate = dto.PaidDate,
                PaymentMethod = dto.PaymentMethod,
                SupplierId = dto.SupplierId,
                CreatedAt = DateTime.UtcNow,
                ReceiptImagePath = dto.ReceiptImagePath,
                ReceiptImage = dto.ReceiptImage,
                ReceiptImageContentType = dto.ReceiptImageContentType
            };

            var lineNo = 1;
            var touchedInvoiceIds = new List<int>();

            // Cache invoices we've already loaded in this request AND track a
            // running "remaining due" per invoice. This matters as soon as a
            // single voucher carries two or more detail rows against the SAME
            // invoice (installments split across lines): checking each row
            // against invoice.Due straight from the DB would let both rows
            // pass individually even though, added together, they overpay the
            // invoice — the DB value never moves until SaveChangesAsync, so a
            // naive per-row check can't see what earlier rows in this same
            // request already consumed. Tracking it in-memory closes that gap.
            var invoiceCache = new Dictionary<int, PurchaseInvoice>();
            var remainingDue = new Dictionary<int, decimal>();

            foreach (var detailDto in dto.Details)
            {
                if (!invoiceCache.TryGetValue(detailDto.PurchaseInvoiceId, out var invoice))
                {
                    invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == detailDto.PurchaseInvoiceId);
                    if (invoice is null)
                        return BadRequestResponse($"Purchase invoice {detailDto.PurchaseInvoiceId} does not exist.");
                    if (invoice.SupplierId != dto.SupplierId)
                        return BadRequestResponse($"Purchase invoice {invoice.Id} belongs to a different supplier, not supplier {dto.SupplierId}.");

                    invoiceCache[invoice.Id] = invoice;
                    remainingDue[invoice.Id] = invoice.Due;
                }

                if (detailDto.PaidAmount > remainingDue[invoice.Id])
                    return ConflictResponse($"Paid amount {detailDto.PaidAmount} exceeds invoice {invoice.Id}'s available due of {remainingDue[invoice.Id]} (current due {invoice.Due}).");

                payment.Details.Add(new SupplierPaymentDetail
                {
                    PurchaseInvoiceId = detailDto.PurchaseInvoiceId,
                    LineNo = lineNo++,
                    TotalAmount = invoice.Total,
                    DueBeforePayment = remainingDue[invoice.Id],
                    PrePaid = detailDto.PrePaid,
                    PaidAmount = detailDto.PaidAmount
                });

                remainingDue[invoice.Id] -= detailDto.PaidAmount;
                touchedInvoiceIds.Add(invoice.Id);
            }

            payment.TotalAmount = payment.Details.Sum(d => d.PaidAmount);

            _db.SupplierPayments.Add(payment);
            await _db.SaveChangesAsync(); // needed first so Detail rows get real Ids / the invoice FK is satisfied

            foreach (var invoiceId in touchedInvoiceIds.Distinct())
                await RecalculateInvoiceDueAsync(invoiceId);
            await _db.SaveChangesAsync();
            await _accounting.PostAsync(LedgerSourceType.SupplierPayment, payment.Id, payment.PaidDate, $"Supplier payment {payment.PaidNo}",
                ("2000", payment.TotalAmount, 0), ("1000", 0, payment.TotalAmount));
            await _db.SaveChangesAsync();

            await _db.Entry(payment).Reference(p => p.Supplier).LoadAsync();
            await _db.Entry(payment).Collection(p => p.Details).Query().Include(d => d.PurchaseInvoice).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = payment.Id }, ToDto(payment));
        }

        // PUT api/supplierpayments/5 — header fields only (PaidNo/PaidDate,
        // receipt, and cancelling). Use the details endpoints below to
        // add/update/remove allocation rows.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SupplierPaymentUpdateDto dto)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.SupplierPayment && l.SourceId == id))
                return ConflictResponse("Posted supplier payments are immutable. Cancel through an accounting reversal instead.");
            var payment = await _db.SupplierPayments.Include(p => p.Details).FirstOrDefaultAsync(p => p.Id == id);
            if (payment is null) return NotFoundResponse($"Supplier payment {id} not found.");

            if (payment.IsCancelled && !dto.IsCancelled)
                return BadRequestResponse("A cancelled payment cannot be un-cancelled. Create a new voucher instead.");

            if (await _db.SupplierPayments.AnyAsync(p => p.PaidNo == dto.PaidNo && p.Id != id))
                return ConflictResponse($"Paid No '{dto.PaidNo}' already exists.");

            payment.PaidNo = dto.PaidNo;
            payment.PaidDate = dto.PaidDate;
            payment.PaymentMethod = dto.PaymentMethod;
            if (dto.ReceiptImagePath is not null) payment.ReceiptImagePath = dto.ReceiptImagePath;
            if (dto.ReceiptImage is not null) payment.ReceiptImage = dto.ReceiptImage;
            if (dto.ReceiptImageContentType is not null) payment.ReceiptImageContentType = dto.ReceiptImageContentType;

            var wasCancelled = payment.IsCancelled;
            payment.IsCancelled = dto.IsCancelled;
            await _db.SaveChangesAsync();

            // Just got cancelled -> every invoice this voucher touched needs
            // its Due restored (RecalculateInvoiceDueAsync automatically
            // excludes cancelled vouchers from the sum).
            if (!wasCancelled && dto.IsCancelled)
            {
                foreach (var invoiceId in payment.Details.Select(d => d.PurchaseInvoiceId).Distinct())
                    await RecalculateInvoiceDueAsync(invoiceId);
                await _db.SaveChangesAsync();
            }

            return NoContent();
        }

        // DELETE api/supplierpayments/5 — hard-deletes the voucher (details
        // cascade-delete with it) and restores Due on every invoice it had
        // touched. Prefer PUT with IsCancelled=true if you want to keep an
        // audit trail instead of erasing the voucher outright.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == LedgerSourceType.SupplierPayment && l.SourceId == id))
                return ConflictResponse("Posted supplier payments cannot be deleted. Cancel through an accounting reversal instead.");
            var payment = await _db.SupplierPayments.Include(p => p.Details).FirstOrDefaultAsync(p => p.Id == id);
            if (payment is null) return NotFoundResponse($"Supplier payment {id} not found.");

            var touchedInvoiceIds = payment.Details.Select(d => d.PurchaseInvoiceId).Distinct().ToList();

            _db.SupplierPayments.Remove(payment); // details cascade-delete with it
            await _db.SaveChangesAsync();

            foreach (var invoiceId in touchedInvoiceIds)
                await RecalculateInvoiceDueAsync(invoiceId);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // POST api/supplierpayments/5/receipt-image — upload/replace the
        // receipt image as an actual file (multipart/form-data). Kept at
        // original resolution (not resized like profile/logo photos) since
        // this is a scanned bank slip / receipt that needs to stay legible.
        // In Postman: Body -> form-data -> key "file", type "File".
        [HttpPost("{id:int}/receipt-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadReceiptImage(int id, IFormFile file)
        {
            var payment = await _db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id);
            if (payment is null) return NotFoundResponse($"Supplier payment {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            payment.ReceiptImage = ms.ToArray();
            payment.ReceiptImageContentType = file.ContentType;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/supplierpayments/5/details — allocate more of this
        // voucher toward another invoice.
        [HttpPost("{id:int}/details")]
        public async Task<ActionResult<SupplierPaymentDetailReadDto>> AddDetail(int id, [FromBody] SupplierPaymentDetailWriteDto dto)
        {
            var payment = await _db.SupplierPayments.Include(p => p.Details).FirstOrDefaultAsync(p => p.Id == id);
            if (payment is null) return NotFoundResponse($"Supplier payment {id} not found.");
            if (payment.IsCancelled) return BadRequestResponse("This payment voucher is cancelled.");

            var invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId);
            if (invoice is null) return BadRequestResponse($"Purchase invoice {dto.PurchaseInvoiceId} does not exist.");
            if (invoice.SupplierId != payment.SupplierId)
                return BadRequestResponse($"Purchase invoice {invoice.Id} belongs to a different supplier, not supplier {payment.SupplierId}.");
            if (dto.PaidAmount > invoice.Due)
                return ConflictResponse($"Paid amount {dto.PaidAmount} exceeds invoice {invoice.Id}'s current due of {invoice.Due}.");

            var detail = new SupplierPaymentDetail
            {
                SupplierPaymentId = id,
                PurchaseInvoiceId = dto.PurchaseInvoiceId,
                LineNo = payment.Details.Count == 0 ? 1 : payment.Details.Max(d => d.LineNo) + 1,
                TotalAmount = invoice.Total,
                DueBeforePayment = invoice.Due,
                PrePaid = dto.PrePaid,
                PaidAmount = dto.PaidAmount
            };

            _db.SupplierPaymentDetails.Add(detail);
            payment.TotalAmount += detail.PaidAmount;
            await _db.SaveChangesAsync();

            await RecalculateInvoiceDueAsync(invoice.Id);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id }, new SupplierPaymentDetailReadDto
            {
                Id = detail.Id,
                PurchaseInvoiceId = detail.PurchaseInvoiceId,
                InvoiceNo = invoice.InvoiceNo,
                LineNo = detail.LineNo,
                TotalAmount = detail.TotalAmount,
                DueBeforePayment = detail.DueBeforePayment,
                PrePaid = detail.PrePaid,
                PaidAmount = detail.PaidAmount
            });
        }

        // PUT api/supplierpayments/5/details/9 — change the amount (or, less
        // commonly, which invoice) a single allocation row covers.
        [HttpPut("{id:int}/details/{detailId:int}")]
        public async Task<IActionResult> UpdateDetail(int id, int detailId, [FromBody] SupplierPaymentDetailWriteDto dto)
        {
            var payment = await _db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id);
            if (payment is null) return NotFoundResponse($"Supplier payment {id} not found.");
            if (payment.IsCancelled) return BadRequestResponse("This payment voucher is cancelled.");

            var detail = await _db.SupplierPaymentDetails.FirstOrDefaultAsync(d => d.Id == detailId && d.SupplierPaymentId == id);
            if (detail is null) return NotFoundResponse($"Detail {detailId} not found for payment {id}.");

            var newInvoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == dto.PurchaseInvoiceId);
            if (newInvoice is null) return BadRequestResponse($"Purchase invoice {dto.PurchaseInvoiceId} does not exist.");
            if (newInvoice.SupplierId != payment.SupplierId)
                return BadRequestResponse($"Purchase invoice {newInvoice.Id} belongs to a different supplier, not supplier {payment.SupplierId}.");

            var oldInvoiceId = detail.PurchaseInvoiceId;

            // Validate against the invoice's due AS IF this row's current
            // amount weren't already counted, so editing a row's own amount
            // (without changing invoice) doesn't falsely trip "exceeds due".
            var dueAvailable = newInvoice.Id == oldInvoiceId
                ? newInvoice.Due + detail.PaidAmount
                : newInvoice.Due;
            if (dto.PaidAmount > dueAvailable)
                return ConflictResponse($"Paid amount {dto.PaidAmount} exceeds invoice {newInvoice.Id}'s available due of {dueAvailable}.");

            payment.TotalAmount += dto.PaidAmount - detail.PaidAmount;

            detail.PurchaseInvoiceId = dto.PurchaseInvoiceId;
            detail.TotalAmount = newInvoice.Total;
            detail.DueBeforePayment = newInvoice.Due;
            detail.PrePaid = dto.PrePaid;
            detail.PaidAmount = dto.PaidAmount;

            await _db.SaveChangesAsync();

            await RecalculateInvoiceDueAsync(oldInvoiceId);
            if (newInvoice.Id != oldInvoiceId)
                await RecalculateInvoiceDueAsync(newInvoice.Id);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // DELETE api/supplierpayments/5/details/9 — remove one allocation
        // row and restore the corresponding amount to the invoice's Due.
        [HttpDelete("{id:int}/details/{detailId:int}")]
        public async Task<IActionResult> RemoveDetail(int id, int detailId)
        {
            var payment = await _db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id);
            if (payment is null) return NotFoundResponse($"Supplier payment {id} not found.");

            var detail = await _db.SupplierPaymentDetails.FirstOrDefaultAsync(d => d.Id == detailId && d.SupplierPaymentId == id);
            if (detail is null) return NotFoundResponse($"Detail {detailId} not found for payment {id}.");

            var invoiceId = detail.PurchaseInvoiceId;

            payment.TotalAmount -= detail.PaidAmount;
            _db.SupplierPaymentDetails.Remove(detail);
            await _db.SaveChangesAsync();

            await RecalculateInvoiceDueAsync(invoiceId);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // ---------- helpers ----------

        // The single source of truth for an invoice's Due/PaymentStatus once
        // SupplierPayments exist: Due = Total - Advance - (sum of PaidAmount
        // across all non-cancelled allocations against it). Always recomputed
        // from scratch rather than incremented, so it can never drift no
        // matter what order operations happen in. PurchaseInvoicesController
        // uses this exact same formula in its own RecalculateTotalsAsync, so
        // editing an invoice's items later never erases a payment's effect.
        private async Task RecalculateInvoiceDueAsync(int invoiceId)
        {
            var invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
            if (invoice is null) return;

            var paidViaAllocations = await _db.SupplierPaymentDetails
                .Where(d => d.PurchaseInvoiceId == invoiceId && !d.SupplierPayment.IsCancelled)
                .SumAsync(d => (decimal?)d.PaidAmount) ?? 0m;

            invoice.Due = Math.Max(0, invoice.Total - invoice.Advance - paidViaAllocations);
            invoice.PaymentStatus = invoice.Due <= 0 ? "Complete" : "InComplete";
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));

        private static SupplierPaymentReadDto ToDto(SupplierPayment p) => new()
        {
            Id = p.Id,
            PaidNo = p.PaidNo,
            PaidDate = p.PaidDate,
            PaymentMethod = p.PaymentMethod,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.SupplierName,
            TotalAmount = p.TotalAmount,
            CreatedAt = p.CreatedAt,
            IsCancelled = p.IsCancelled,
            ReceiptImagePath = p.ReceiptImagePath,
            ReceiptImage = p.ReceiptImage,
            ReceiptImageContentType = p.ReceiptImageContentType,
            Details = p.Details.Select(d => new SupplierPaymentDetailReadDto
            {
                Id = d.Id,
                PurchaseInvoiceId = d.PurchaseInvoiceId,
                InvoiceNo = d.PurchaseInvoice?.InvoiceNo,
                LineNo = d.LineNo,
                TotalAmount = d.TotalAmount,
                DueBeforePayment = d.DueBeforePayment,
                PrePaid = d.PrePaid,
                PaidAmount = d.PaidAmount
            }).ToList()
        };
    }
}
```

### `SupplierProductsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // The Supplier <-> Product many-to-many. Two ways to call GetAll:
    //   ?supplierId=7  -> every product this supplier carries (their catalogue)
    //   ?productId=12  -> every supplier this product can be bought from
    // Each link carries its own set of per-unit prices (Pcs/Box/Strip/...),
    // never falls back to the product's flat master price.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SupplierProductsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public SupplierProductsController(PharmacyDbContext db)
        {
            _db = db;
        }

        private static SupplierProductReadDto MapToReadDto(SupplierProduct sp) => new()
        {
            Id = sp.Id,
            SupplierId = sp.SupplierId,
            SupplierName = sp.Supplier?.SupplierName ?? string.Empty,
            ProductId = sp.ProductId,
            ProductName = sp.Product?.ProductName ?? string.Empty,
            ProductStrength = sp.Product?.Strength,
            SupplierProductCode = sp.SupplierProductCode,
            IsPreferred = sp.IsPreferred,
            IsActive = sp.IsActive,
            LastPurchaseDate = sp.LastPurchaseDate,
            LastPurchaseUnitCost = sp.LastPurchaseUnitCost,
            LastPurchaseUnitName = null, // filled by caller when unit is loaded; see GetAll/GetById
            Note = sp.Note,
            Prices = sp.Prices.Select(p => new SupplierProductPriceReadDto
            {
                Id = p.Id,
                UnitId = p.UnitId,
                UnitName = p.Unit?.Name ?? string.Empty,
                BaseQuantity = p.BaseQuantity,
                PurchasePrice = p.PurchasePrice,
                SalePrice = p.SalePrice,
                UnitPrice = p.UnitPrice,
                DistributorPrice = p.DistributorPrice,
                UpdatedAt = p.UpdatedAt
            }).OrderBy(p => p.UnitName).ToList()
        };

        private IQueryable<SupplierProduct> BaseQuery() => _db.SupplierProducts
            .Include(sp => sp.Supplier)
            .Include(sp => sp.Product)
            .Include(sp => sp.Prices).ThenInclude(p => p.Unit)
            .AsQueryable();

        // GET api/supplierproducts?supplierId=7
        // GET api/supplierproducts?productId=12
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SupplierProductReadDto>>> GetAll(
            [FromQuery] int? supplierId,
            [FromQuery] int? productId,
            [FromQuery] bool includeInactive = false)
        {
            if (supplierId is null && productId is null)
                return BadRequestResponse("Provide supplierId or productId.");

            var query = BaseQuery();

            if (supplierId is not null)
                query = query.Where(sp => sp.SupplierId == supplierId);

            if (productId is not null)
                query = query.Where(sp => sp.ProductId == productId);

            if (!includeInactive)
                query = query.Where(sp => sp.IsActive);

            var links = await query
                .OrderByDescending(sp => sp.IsPreferred)
                .ThenBy(sp => sp.Supplier.SupplierName)
                .ThenBy(sp => sp.Product.ProductName)
                .ToListAsync();

            var unitNames = await _db.Units.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);

            var result = links.Select(sp =>
            {
                var dto = MapToReadDto(sp);
                if (sp.LastPurchaseUnitId is not null && unitNames.TryGetValue(sp.LastPurchaseUnitId.Value, out var uName))
                    dto.LastPurchaseUnitName = uName;
                return dto;
            });

            return Ok(result);
        }

        // GET api/supplierproducts/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SupplierProductReadDto>> GetById(int id)
        {
            var sp = await BaseQuery().FirstOrDefaultAsync(x => x.Id == id);
            if (sp is null) return NotFoundResponse($"Supplier-product link {id} not found.");

            var dto = MapToReadDto(sp);
            if (sp.LastPurchaseUnitId is not null)
            {
                var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == sp.LastPurchaseUnitId);
                dto.LastPurchaseUnitName = unit?.Name;
            }
            return Ok(dto);
        }

        // POST api/supplierproducts — link a supplier to a product, optionally
        // seeding its per-unit prices in the same call.
        [HttpPost]
        public async Task<ActionResult<SupplierProductReadDto>> Create([FromBody] SupplierProductWriteDto dto)
        {
            if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == dto.SupplierId))
                return BadRequestResponse($"Supplier {dto.SupplierId} does not exist.");

            if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
                return BadRequestResponse($"Product {dto.ProductId} does not exist.");

            if (await _db.SupplierProducts.AnyAsync(sp => sp.SupplierId == dto.SupplierId && sp.ProductId == dto.ProductId))
                return ConflictResponse("This supplier is already linked to this product.");

            if (dto.Prices is { Count: > 0 })
            {
                var unitIds = dto.Prices.Select(p => p.UnitId).ToList();
                var validUnitCount = await _db.Units.CountAsync(u => unitIds.Contains(u.Id));
                if (validUnitCount != unitIds.Distinct().Count())
                    return BadRequestResponse("One or more units in Prices do not exist, or are duplicated.");
            }

            var link = new SupplierProduct
            {
                SupplierId = dto.SupplierId,
                ProductId = dto.ProductId,
                SupplierProductCode = dto.SupplierProductCode,
                IsPreferred = dto.IsPreferred,
                IsActive = dto.IsActive,
                Note = dto.Note
            };

            if (dto.Prices is { Count: > 0 })
            {
                foreach (var p in dto.Prices)
                {
                    link.Prices.Add(new SupplierProductPrice
                    {
                        UnitId = p.UnitId,
                        BaseQuantity = p.BaseQuantity,
                        PurchasePrice = p.PurchasePrice,
                        SalePrice = p.SalePrice,
                        UnitPrice = p.UnitPrice,
                        DistributorPrice = p.DistributorPrice
                    });

                    _db.Add(new ProductPriceHistory
                    {
                        ProductId = dto.ProductId,
                        SupplierId = dto.SupplierId,
                        UnitId = p.UnitId,
                        PriceType = "SupplierPurchase",
                        PreviousPrice = null,
                        NewPrice = p.PurchasePrice,
                        ChangedAt = DateTime.UtcNow
                    });
                }
            }

            _db.SupplierProducts.Add(link);
            await _db.SaveChangesAsync();

            var reloaded = await BaseQuery().FirstAsync(x => x.Id == link.Id);
            return CreatedAtAction(nameof(GetById), new { id = link.Id }, MapToReadDto(reloaded));
        }

        // PUT api/supplierproducts/5 — update link metadata and, when Prices
        // is provided, fully sync the per-unit price rows (same "sync"
        // convention ProductsController uses: matching Id = update, no Id =
        // insert, missing existing Id = delete), logging each purchase-price
        // change to ProductPriceHistory with SupplierId set.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SupplierProductWriteDto dto)
        {
            var link = await _db.SupplierProducts.Include(sp => sp.Prices)
                .FirstOrDefaultAsync(sp => sp.Id == id);
            if (link is null) return NotFoundResponse($"Supplier-product link {id} not found.");

            if (await _db.SupplierProducts.AnyAsync(sp => sp.SupplierId == dto.SupplierId && sp.ProductId == dto.ProductId && sp.Id != id))
                return ConflictResponse("This supplier is already linked to this product.");

            link.SupplierId = dto.SupplierId;
            link.ProductId = dto.ProductId;
            link.SupplierProductCode = dto.SupplierProductCode;
            link.IsPreferred = dto.IsPreferred;
            link.IsActive = dto.IsActive;
            link.Note = dto.Note;

            if (dto.Prices is not null)
            {
                var incomingIds = dto.Prices.Where(p => p.Id is not null).Select(p => p.Id!.Value).ToHashSet();
                var toRemove = link.Prices.Where(p => !incomingIds.Contains(p.Id)).ToList();
                foreach (var removed in toRemove)
                    _db.SupplierProductPrices.Remove(removed);

                foreach (var p in dto.Prices)
                {
                    var existing = p.Id is not null ? link.Prices.FirstOrDefault(x => x.Id == p.Id) : null;

                    if (existing is not null)
                    {
                        if (existing.PurchasePrice != p.PurchasePrice)
                        {
                            _db.Add(new ProductPriceHistory
                            {
                                ProductId = link.ProductId,
                                SupplierId = link.SupplierId,
                                UnitId = p.UnitId,
                                PriceType = "SupplierPurchase",
                                PreviousPrice = existing.PurchasePrice,
                                NewPrice = p.PurchasePrice,
                                ChangedAt = DateTime.UtcNow
                            });
                        }

                        existing.UnitId = p.UnitId;
                        existing.BaseQuantity = p.BaseQuantity;
                        existing.PurchasePrice = p.PurchasePrice;
                        existing.SalePrice = p.SalePrice;
                        existing.UnitPrice = p.UnitPrice;
                        existing.DistributorPrice = p.DistributorPrice;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        link.Prices.Add(new SupplierProductPrice
                        {
                            UnitId = p.UnitId,
                            BaseQuantity = p.BaseQuantity,
                            PurchasePrice = p.PurchasePrice,
                            SalePrice = p.SalePrice,
                            UnitPrice = p.UnitPrice,
                            DistributorPrice = p.DistributorPrice
                        });

                        _db.Add(new ProductPriceHistory
                        {
                            ProductId = link.ProductId,
                            SupplierId = link.SupplierId,
                            UnitId = p.UnitId,
                            PriceType = "SupplierPurchase",
                            PreviousPrice = null,
                            NewPrice = p.PurchasePrice,
                            ChangedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/supplierproducts/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var link = await _db.SupplierProducts.FirstOrDefaultAsync(sp => sp.Id == id);
            if (link is null) return NotFoundResponse($"Supplier-product link {id} not found.");

            _db.SupplierProducts.Remove(link);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // GET api/supplierproducts/5/price-history — full price-change log
        // for this supplier+product, per unit, newest first.
        [HttpGet("{id:int}/price-history")]
        public async Task<ActionResult> GetPriceHistory(int id)
        {
            var link = await _db.SupplierProducts.AsNoTracking().FirstOrDefaultAsync(sp => sp.Id == id);
            if (link is null) return NotFoundResponse($"Supplier-product link {id} not found.");

            var history = await _db.ProductPriceHistories.AsNoTracking()
                .Include(h => h.Unit)
                .Where(h => h.ProductId == link.ProductId && h.SupplierId == link.SupplierId)
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new
                {
                    h.Id,
                    h.PriceType,
                    UnitName = h.Unit != null ? h.Unit.Name : null,
                    h.PreviousPrice,
                    h.NewPrice,
                    h.ChangedAt
                })
                .ToListAsync();

            return Ok(history);
        }

        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `SuppliersController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    // Master (Supplier) + details (SupplierContact) CRUD.
    // SupplierTypeId is the relational/dropdown field — SupplierType itself
    // has no controller, it's seeded lookup data (see PharmacyDbContext).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SuppliersController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;

        // Same pattern as EmployeesController.EmployeeImagesRelativeFolder.
        private const string SupplierImagesRelativeFolder = "images/suppliers";

        public SuppliersController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private string GetSupplierImagesFolderPath()
        {
            string webRoot = string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;

            string folderPath = Path.Combine(webRoot, "images", "suppliers");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            return folderPath;
        }

        // GET api/suppliers?search=beximco — list suppliers, optionally filtered by name.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SupplierListItemDto>>> GetAll([FromQuery] string? search, [FromQuery] int? companyId)
        {
            var query = _db.Suppliers.AsNoTracking()
                .Include(s => s.SupplierType)
                .Include(s => s.Company)
                .Include(s => s.Contacts)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.SupplierName.Contains(search));

            if (companyId is not null)
                query = query.Where(s => s.CompanyId == companyId);

            var suppliers = await query
                .OrderBy(s => s.SupplierName)
                .Select(s => new SupplierListItemDto
                {
                    SupplierId = s.SupplierId,
                    SupplierName = s.SupplierName,
                    CompanyId = s.CompanyId,
                    CompanyName = s.Company != null ? s.Company.Name : null,
                    Phone = s.Phone,
                    Distributor = s.Distributor,
                    SupplierTypeId = s.SupplierTypeId,
                    SupplierTypeName = s.SupplierType.Name,
                    LogoPath = s.LogoPath,
                    Contacts = s.Contacts.Select(c => new SupplierContactReadDto
                    {
                        Id = c.Id,
                        ContactName = c.ContactName,
                        Designation = c.Designation,
                        Phone = c.Phone,
                        IsPrimary = c.IsPrimary
                    }).ToList()
                })
                .ToListAsync();

            return Ok(suppliers);
        }

        // GET api/suppliers/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SupplierReadDto>> GetById(int id)
        {
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .Include(s => s.SupplierType)
                .Include(s => s.Company)
                .Include(s => s.Contacts)
                .FirstOrDefaultAsync(s => s.SupplierId == id);

            if (supplier is null)
                return NotFoundResponse($"Supplier {id} not found.");

            return Ok(MapToReadDto(supplier));
        }

        // POST api/suppliers — register a new supplier.
        [HttpPost]
        public async Task<ActionResult<SupplierReadDto>> Create([FromBody] SupplierCreateDto dto)
        {
            var typeExists = await _db.SupplierTypes.AnyAsync(t => t.Id == dto.SupplierTypeId);
            if (!typeExists)
                return BadRequestResponse($"Supplier type {dto.SupplierTypeId} does not exist.");

            if (dto.CompanyId is not null && !await _db.Companies.AnyAsync(c => c.Id == dto.CompanyId))
                return BadRequestResponse($"Company {dto.CompanyId} does not exist.");

            var supplier = new Supplier
            {
                SupplierName = dto.SupplierName,
                CompanyId = dto.CompanyId,
                ContactPerson = dto.ContactPerson,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address,
                Distributor = dto.Distributor,
                OpeningBalance = dto.OpeningBalance,
                Logo = dto.Logo,
                LogoContentType = dto.LogoContentType,
                SupplierTypeId = dto.SupplierTypeId,
                // Client-supplied date/time wins if sent; otherwise default to now (UTC).
                CreatedAt = dto.CreatedAt ?? DateTime.UtcNow
            };

            if (dto.Contacts is { Count: > 0 })
            {
                foreach (var contactDto in dto.Contacts)
                {
                    supplier.Contacts.Add(new SupplierContact
                    {
                        ContactName = contactDto.ContactName,
                        Designation = contactDto.Designation,
                        Phone = contactDto.Phone,
                        IsPrimary = contactDto.IsPrimary
                    });
                }

                NormalizePrimaryContact(supplier.Contacts);
            }

            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync();

            await _db.Entry(supplier).Reference(s => s.SupplierType).LoadAsync();
            if (supplier.CompanyId is not null)
                await _db.Entry(supplier).Reference(s => s.Company).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = supplier.SupplierId }, MapToReadDto(supplier));
        }

        // PUT api/suppliers/5 — full update of master fields, with a full
        // add/update/delete sync of the Contacts collection.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SupplierUpdateDto dto)
        {
            var supplier = await _db.Suppliers
                .Include(s => s.Contacts)
                .FirstOrDefaultAsync(s => s.SupplierId == id);

            if (supplier is null)
                return NotFoundResponse($"Supplier {id} not found.");

            var typeExists = await _db.SupplierTypes.AnyAsync(t => t.Id == dto.SupplierTypeId);
            if (!typeExists)
                return BadRequestResponse($"Supplier type {dto.SupplierTypeId} does not exist.");

            if (dto.CompanyId is not null && !await _db.Companies.AnyAsync(c => c.Id == dto.CompanyId))
                return BadRequestResponse($"Company {dto.CompanyId} does not exist.");

            supplier.SupplierName = dto.SupplierName;
            supplier.CompanyId = dto.CompanyId;
            supplier.ContactPerson = dto.ContactPerson;
            supplier.Phone = dto.Phone;
            supplier.Email = dto.Email;
            supplier.Address = dto.Address;
            supplier.Distributor = dto.Distributor;
            supplier.OpeningBalance = dto.OpeningBalance;
            if (dto.Logo is not null) supplier.Logo = dto.Logo;
            if (dto.LogoContentType is not null) supplier.LogoContentType = dto.LogoContentType;
            supplier.SupplierTypeId = dto.SupplierTypeId;

            // Only touch CreatedAt if the caller actually sent a value.
            if (dto.CreatedAt.HasValue)
                supplier.CreatedAt = dto.CreatedAt.Value;

            if (dto.Contacts is not null)
            {
                var incomingIds = dto.Contacts.Where(c => c.Id is > 0).Select(c => c.Id!.Value).ToHashSet();

                var toRemove = supplier.Contacts.Where(c => !incomingIds.Contains(c.Id)).ToList();
                foreach (var contact in toRemove)
                    supplier.Contacts.Remove(contact);

                foreach (var contactDto in dto.Contacts)
                {
                    if (contactDto.Id is > 0)
                    {
                        var existingContact = supplier.Contacts.FirstOrDefault(c => c.Id == contactDto.Id);
                        if (existingContact is null)
                            return BadRequestResponse($"Contact {contactDto.Id} does not belong to supplier {id}.");

                        existingContact.ContactName = contactDto.ContactName;
                        existingContact.Designation = contactDto.Designation;
                        existingContact.Phone = contactDto.Phone;
                        existingContact.IsPrimary = contactDto.IsPrimary;
                    }
                    else
                    {
                        supplier.Contacts.Add(new SupplierContact
                        {
                            ContactName = contactDto.ContactName,
                            Designation = contactDto.Designation,
                            Phone = contactDto.Phone,
                            IsPrimary = contactDto.IsPrimary
                        });
                    }
                }

                NormalizePrimaryContact(supplier.Contacts);
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/suppliers/5 — blocked once the supplier has purchase history;
        // contacts cascade-delete with the supplier otherwise.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.SupplierId == id);
            if (supplier is null)
                return NotFoundResponse($"Supplier {id} not found.");

            if (await _db.PurchaseInvoices.AnyAsync(p => p.SupplierId == id))
                return ConflictResponse("Cannot delete a supplier with existing purchase invoices.");

            _db.Suppliers.Remove(supplier);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/suppliers/5/logo — upload/replace the supplier's logo as a
        // real file (multipart/form-data). In Postman: Body -> form-data ->
        // key "file", type "File" -> pick an image from disk.
        [HttpPost("{id:int}/logo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadLogo(int id, IFormFile file)
        {
            var supplier = await _db.Suppliers.FindAsync(id);
            if (supplier is null) return NotFoundResponse($"Supplier {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";

            string[] allowed = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            if (!allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return BadRequestResponse("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp.");

            string folderPath = GetSupplierImagesFolderPath();
            string fileName = id + "_" + Guid.NewGuid().ToString("N")[..8] + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            using (var stream = System.IO.File.Create(fullFilePath))
                await file.CopyToAsync(stream);

            DeleteSupplierLogoFile(supplier.LogoPath);

            supplier.Logo = null; // stop keeping raw bytes in the DB now that we have a file on disk
            supplier.LogoContentType = file.ContentType;
            supplier.LogoPath = "/" + SupplierImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { supplier.SupplierId, supplier.LogoPath });
        }

        // DELETE api/suppliers/5/logo
        [HttpDelete("{id:int}/logo")]
        public async Task<IActionResult> DeleteLogo(int id)
        {
            var supplier = await _db.Suppliers.FindAsync(id);
            if (supplier is null) return NotFoundResponse($"Supplier {id} not found.");

            DeleteSupplierLogoFile(supplier.LogoPath);

            supplier.Logo = null;
            supplier.LogoContentType = null;
            supplier.LogoPath = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/suppliers/5/contacts
        [HttpPost("{supplierId:int}/contacts")]
        public async Task<ActionResult<SupplierContactReadDto>> AddContact(int supplierId, [FromBody] SupplierContactWriteDto dto)
        {
            var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.SupplierId == supplierId);
            if (supplier is null)
                return NotFoundResponse($"Supplier {supplierId} not found.");

            var contact = new SupplierContact
            {
                SupplierId = supplierId,
                ContactName = dto.ContactName,
                Designation = dto.Designation,
                Phone = dto.Phone,
                IsPrimary = dto.IsPrimary
            };

            _db.SupplierContacts.Add(contact);
            await _db.SaveChangesAsync();

            if (contact.IsPrimary)
            {
                await ClearOtherPrimaryContactsAsync(supplierId, contact.Id);
                await _db.SaveChangesAsync();
            }

            return CreatedAtAction(nameof(GetById), new { id = supplierId }, MapContactToReadDto(contact));
        }

        // PUT api/suppliers/5/contacts/9
        [HttpPut("{supplierId:int}/contacts/{contactId:int}")]
        public async Task<IActionResult> UpdateContact(int supplierId, int contactId, [FromBody] SupplierContactWriteDto dto)
        {
            var contact = await _db.SupplierContacts
                .FirstOrDefaultAsync(c => c.Id == contactId && c.SupplierId == supplierId);

            if (contact is null)
                return NotFoundResponse($"Contact {contactId} not found for supplier {supplierId}.");

            contact.ContactName = dto.ContactName;
            contact.Designation = dto.Designation;
            contact.Phone = dto.Phone;
            contact.IsPrimary = dto.IsPrimary;

            await _db.SaveChangesAsync();
            if (contact.IsPrimary)
            {
                await ClearOtherPrimaryContactsAsync(supplierId, contact.Id);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }

        // DELETE api/suppliers/5/contacts/9
        [HttpDelete("{supplierId:int}/contacts/{contactId:int}")]
        public async Task<IActionResult> DeleteContact(int supplierId, int contactId)
        {
            var contact = await _db.SupplierContacts
                .FirstOrDefaultAsync(c => c.Id == contactId && c.SupplierId == supplierId);

            if (contact is null)
                return NotFoundResponse($"Contact {contactId} not found for supplier {supplierId}.");

            _db.SupplierContacts.Remove(contact);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // GET api/suppliers/by-company/5 — company-র অধীনে যত supplier আছে (Company
        // drill-down page + Purchase page-এর company-wise supplier column দুটোতেই ব্যবহার হবে)
        [HttpGet("by-company/{companyId:int}")]
        public async Task<ActionResult<IEnumerable<SupplierListItemDto>>> GetByCompany(int companyId)
        {
            if (!await _db.Companies.AnyAsync(c => c.Id == companyId))
                return NotFoundResponse($"Company {companyId} not found.");

            var suppliers = await _db.Suppliers.AsNoTracking()
                .Include(s => s.SupplierType).Include(s => s.Company).Include(s => s.Contacts)
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.SupplierName)
                .Select(s => new SupplierListItemDto
                {
                    SupplierId = s.SupplierId,
                    SupplierName = s.SupplierName,
                    CompanyId = s.CompanyId,
                    CompanyName = s.Company != null ? s.Company.Name : null,
                    Phone = s.Phone,
                    Distributor = s.Distributor,
                    SupplierTypeId = s.SupplierTypeId,
                    SupplierTypeName = s.SupplierType.Name,
                    LogoPath = s.LogoPath,
                    Contacts = s.Contacts.Select(c => new SupplierContactReadDto
                    {
                        Id = c.Id,
                        ContactName = c.ContactName,
                        Designation = c.Designation,
                        Phone = c.Phone,
                        IsPrimary = c.IsPrimary
                    }).ToList()
                })
                .ToListAsync();

            return Ok(suppliers);
        }

        // ---------- helpers ----------

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));

        private static void NormalizePrimaryContact(ICollection<SupplierContact> contacts)
        {
            var primaryContact = contacts.LastOrDefault(c => c.IsPrimary) ?? contacts.FirstOrDefault();
            foreach (var contact in contacts)
                contact.IsPrimary = ReferenceEquals(contact, primaryContact);
        }

        private async Task ClearOtherPrimaryContactsAsync(int supplierId, int keepContactId)
        {
            var others = await _db.SupplierContacts
                .Where(c => c.SupplierId == supplierId && c.Id != keepContactId && c.IsPrimary)
                .ToListAsync();
            foreach (var other in others)
                other.IsPrimary = false;
        }

        // Deletes the physical file behind a LogoPath, if any. Safe to call
        // with null/empty/unrelated paths — does nothing in that case.
        private void DeleteSupplierLogoFile(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath)) return;
            if (logoPath.IndexOf(SupplierImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0) return;

            string fileName = Path.GetFileName(logoPath);
            if (string.IsNullOrWhiteSpace(fileName)) return;

            string fullFilePath = Path.Combine(GetSupplierImagesFolderPath(), fileName);
            if (System.IO.File.Exists(fullFilePath))
                System.IO.File.Delete(fullFilePath);
        }

        private static SupplierReadDto MapToReadDto(Supplier supplier) => new()
        {
            SupplierId = supplier.SupplierId,
            SupplierName = supplier.SupplierName,
            CompanyId = supplier.CompanyId,
            CompanyName = supplier.Company?.Name,
            ContactPerson = supplier.ContactPerson,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            Distributor = supplier.Distributor,
            CreatedAt = supplier.CreatedAt,
            OpeningBalance = supplier.OpeningBalance,
            Logo = supplier.Logo,
            LogoContentType = supplier.LogoContentType,
            LogoPath = supplier.LogoPath,
            SupplierTypeId = supplier.SupplierTypeId,
            SupplierTypeName = supplier.SupplierType?.Name ?? string.Empty,
            Contacts = supplier.Contacts.Select(MapContactToReadDto).ToList()
        };

        private static SupplierContactReadDto MapContactToReadDto(SupplierContact contact) => new()
        {
            Id = contact.Id,
            ContactName = contact.ContactName,
            Designation = contact.Designation,
            Phone = contact.Phone,
            IsPrimary = contact.IsPrimary
        };
    }
}
```

### `Suppliertypescontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    // CRUD for the SupplierType lookup that Supplier.SupplierTypeId feeds
    // from. Previously seed-only (Manufacturer/Distributor/Local Vendor).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class SupplierTypesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public SupplierTypesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/suppliertypes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SupplierTypeReadDto>>> GetAll()
        {
            var types = await _db.SupplierTypes
                .AsNoTracking()
                .Select(t => new SupplierTypeReadDto { Id = t.Id, Name = t.Name, SupplierCount = t.Suppliers.Count })
                .ToListAsync();

            return Ok(types);
        }

        // GET api/suppliertypes/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<SupplierTypeReadDto>> GetById(int id)
        {
            var type = await _db.SupplierTypes
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new SupplierTypeReadDto { Id = t.Id, Name = t.Name, SupplierCount = t.Suppliers.Count })
                .FirstOrDefaultAsync();

            if (type is null)
                return NotFoundResponse($"Supplier type {id} not found.");

            return Ok(type);
        }

        // POST api/suppliertypes — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<SupplierTypeReadDto>> Create([FromBody] SupplierTypeWriteDto dto)
        {
            if (await _db.SupplierTypes.AnyAsync(t => t.Name == dto.Name))
                return ConflictResponse($"Supplier type '{dto.Name}' already exists.");

            var type = new SupplierType { Name = dto.Name };

            _db.SupplierTypes.Add(type);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = type.Id },
                new SupplierTypeReadDto { Id = type.Id, Name = type.Name, SupplierCount = 0 });
        }

        // PUT api/suppliertypes/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SupplierTypeWriteDto dto)
        {
            var type = await _db.SupplierTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Supplier type {id} not found.");

            if (await _db.SupplierTypes.AnyAsync(t => t.Name == dto.Name && t.Id != id))
                return ConflictResponse($"Supplier type '{dto.Name}' already exists.");

            type.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/suppliertypes/5 — blocked while any supplier still references it.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await _db.SupplierTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Supplier type {id} not found.");

            if (await _db.Suppliers.AnyAsync(s => s.SupplierTypeId == id))
                return ConflictResponse("Cannot delete a supplier type with suppliers assigned to it.");

            _db.SupplierTypes.Remove(type);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `Unitscontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // CRUD for the Unit lookup table. Every UnitId FK across the schema
    // (Product, ProductPrice, PurchaseOrderItem, PurchaseInvoiceItem,
    // PurchaseReturnItem, SaleItem, SaleReturnItem, DailyPurchaseRequirementTable)
    // feeds its dropdown from here — previously seed-only.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class UnitsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public UnitsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/units
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UnitReadDto>>> GetAll()
        {
            var units = await _db.Units
                .AsNoTracking()
                .Select(u => new UnitReadDto { Id = u.Id, Name = u.Name })
                .ToListAsync();

            return Ok(units);
        }

        // GET api/units/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<UnitReadDto>> GetById(int id)
        {
            var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (unit is null)
                return NotFoundResponse($"Unit {id} not found.");

            return Ok(new UnitReadDto { Id = unit.Id, Name = unit.Name });
        }

        // POST api/units — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<UnitReadDto>> Create([FromBody] UnitWriteDto dto)
        {
            if (await _db.Units.AnyAsync(u => u.Name == dto.Name))
                return ConflictResponse($"Unit '{dto.Name}' already exists.");

            var unit = new Unit { Name = dto.Name };

            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = unit.Id },
                new UnitReadDto { Id = unit.Id, Name = unit.Name });
        }

        // PUT api/units/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UnitWriteDto dto)
        {
            var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id);
            if (unit is null)
                return NotFoundResponse($"Unit {id} not found.");

            if (await _db.Units.AnyAsync(u => u.Name == dto.Name && u.Id != id))
                return ConflictResponse($"Unit '{dto.Name}' already exists.");

            unit.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/units/5 — checks the two most direct references
        // (Product, ProductPrice); any other FK (purchase/sale line items)
        // that still points at this unit will surface as a 409 from the
        // underlying Restrict constraint if this soft check misses it.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id);
            if (unit is null)
                return NotFoundResponse($"Unit {id} not found.");

            if (await _db.Products.AnyAsync(p => p.UnitId == id))
                return ConflictResponse("Cannot delete a unit that products are using.");

            if (await _db.ProductPrices.AnyAsync(pp => pp.UnitId == id))
                return ConflictResponse("Cannot delete a unit that product prices are using.");

            try
            {
                _db.Units.Remove(unit);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return ConflictResponse("Cannot delete this unit — it is still referenced by purchase or sale records.");
            }

            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `Warehousescontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Branch;

namespace PharmacyV2.Controllers
{
    // CRUD for Warehouse (branch/store locations) + a photo endpoint pair.
    // Had a model + DbSet + WarehouseWriteDto/ReadDto already, but no
    // controller — WarehouseTypesController only manages the lookup type,
    // not the warehouse itself.
    //
    // Photo storage: unlike Supplier/Customer/Employee (which moved to
    // path-based files on disk with a PhotoPath column), Warehouse only has
    // Photo (byte[]) + PhotoContentType — no PhotoPath — so photos are kept
    // as raw bytes in the DB and streamed back directly.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class WarehousesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public WarehousesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/warehouses
        [HttpGet]
        public async Task<ActionResult<IEnumerable<WarehouseListItemDto>>> GetAll()
        {
            var warehouses = await _db.Warehouses
                .AsNoTracking()
                .Include(w => w.WarehouseType)
                .OrderBy(w => w.Name)
                .Select(w => new WarehouseListItemDto
                {
                    Id = w.Id,
                    Name = w.Name,
                    Address = w.Address,
                    IsActive = w.IsActive,
                    Capacity = w.Capacity,
                    EstablishedDate = w.EstablishedDate,
                    HasPhoto = w.Photo != null,
                    WarehouseTypeId = w.WarehouseTypeId,
                    WarehouseTypeName = w.WarehouseType.Name
                })
                .ToListAsync();

            return Ok(warehouses);
        }

        // GET api/warehouses/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<WarehouseReadDto>> GetById(int id)
        {
            var warehouse = await _db.Warehouses
                .AsNoTracking()
                .Include(w => w.WarehouseType)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (warehouse is null)
                return NotFoundResponse($"Warehouse {id} not found.");

            return Ok(MapToReadDto(warehouse));
        }

        // POST api/warehouses — Name is globally unique (DB index).
        [HttpPost]
        public async Task<ActionResult<WarehouseReadDto>> Create([FromBody] WarehouseWriteDto dto)
        {
            if (!await _db.WarehouseTypes.AnyAsync(t => t.Id == dto.WarehouseTypeId))
                return BadRequestResponse($"Warehouse type {dto.WarehouseTypeId} does not exist.");

            if (await _db.Warehouses.AnyAsync(w => w.Name == dto.Name))
                return ConflictResponse($"Warehouse '{dto.Name}' already exists.");

            var warehouse = new Warehouse
            {
                Name = dto.Name,
                Address = dto.Address,
                IsActive = dto.IsActive,
                Capacity = dto.Capacity,
                EstablishedDate = dto.EstablishedDate,
                WarehouseTypeId = dto.WarehouseTypeId
            };

            _db.Warehouses.Add(warehouse);
            await _db.SaveChangesAsync();

            await _db.Entry(warehouse).Reference(w => w.WarehouseType).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = warehouse.Id }, MapToReadDto(warehouse));
        }

        // PUT api/warehouses/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] WarehouseWriteDto dto)
        {
            var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
            if (warehouse is null)
                return NotFoundResponse($"Warehouse {id} not found.");

            if (!await _db.WarehouseTypes.AnyAsync(t => t.Id == dto.WarehouseTypeId))
                return BadRequestResponse($"Warehouse type {dto.WarehouseTypeId} does not exist.");

            if (await _db.Warehouses.AnyAsync(w => w.Name == dto.Name && w.Id != id))
                return ConflictResponse($"Warehouse '{dto.Name}' already exists.");

            warehouse.Name = dto.Name;
            warehouse.Address = dto.Address;
            warehouse.IsActive = dto.IsActive;
            warehouse.Capacity = dto.Capacity;
            warehouse.EstablishedDate = dto.EstablishedDate;
            warehouse.WarehouseTypeId = dto.WarehouseTypeId;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/warehouses/5 — blocked once anything actually depends on
        // this warehouse (stock, racks, purchases, transfers), matching the
        // Restrict FKs configured in PharmacyDbContext — checked here first
        // so the caller gets a friendly message instead of a raw SQL error.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
            if (warehouse is null)
                return NotFoundResponse($"Warehouse {id} not found.");

            if (await _db.ProductStocks.AnyAsync(s => s.WarehouseId == id))
                return ConflictResponse("Cannot delete a warehouse with existing stock.");

            if (await _db.ProductRaks.AnyAsync(r => r.WarehouseId == id))
                return ConflictResponse("Cannot delete a warehouse with existing racks.");

            if (await _db.PurchaseInvoices.AnyAsync(p => p.WarehouseId == id))
                return ConflictResponse("Cannot delete a warehouse with existing purchase invoices.");

            if (await _db.StockTransfers.AnyAsync(t => t.FromWarehouseId == id || t.ToWarehouseId == id))
                return ConflictResponse("Cannot delete a warehouse with existing stock transfers.");

            _db.Warehouses.Remove(warehouse);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/warehouses/5/photo — upload/replace, stored as raw bytes
        // in the DB (see class-level comment). Postman: Body -> form-data ->
        // key "file", type "File" -> pick an image from disk.
        [HttpPost("{id:int}/photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
        {
            var warehouse = await _db.Warehouses.FindAsync(id);
            if (warehouse is null)
                return NotFoundResponse($"Warehouse {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            string extension = Path.GetExtension(file.FileName);
            string[] allowed = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            if (string.IsNullOrWhiteSpace(extension) || !allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return BadRequestResponse("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp.");

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            warehouse.Photo = stream.ToArray();
            warehouse.PhotoContentType = file.ContentType;

            await _db.SaveChangesAsync();
            return Ok(new { warehouse.Id, HasPhoto = true });
        }

        // GET api/warehouses/5/photo — streams the raw bytes back.
        [HttpGet("{id:int}/photo")]
        public async Task<IActionResult> GetPhoto(int id)
        {
            var warehouse = await _db.Warehouses.AsNoTracking()
                .Where(w => w.Id == id)
                .Select(w => new { w.Photo, w.PhotoContentType })
                .FirstOrDefaultAsync();

            if (warehouse is null)
                return NotFoundResponse($"Warehouse {id} not found.");

            if (warehouse.Photo is null || warehouse.Photo.Length == 0)
                return NotFoundResponse($"Warehouse {id} has no photo.");

            return File(warehouse.Photo, warehouse.PhotoContentType ?? "application/octet-stream");
        }

        // DELETE api/warehouses/5/photo
        [HttpDelete("{id:int}/photo")]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            var warehouse = await _db.Warehouses.FindAsync(id);
            if (warehouse is null)
                return NotFoundResponse($"Warehouse {id} not found.");

            warehouse.Photo = null;
            warehouse.PhotoContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static WarehouseReadDto MapToReadDto(Warehouse w) => new()
        {
            Id = w.Id,
            Name = w.Name,
            Address = w.Address,
            IsActive = w.IsActive,
            Capacity = w.Capacity,
            EstablishedDate = w.EstablishedDate,
            HasPhoto = w.Photo != null,
            PhotoContentType = w.PhotoContentType,
            WarehouseTypeId = w.WarehouseTypeId,
            WarehouseTypeName = w.WarehouseType?.Name ?? string.Empty
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```

### `Warehousetypescontroller.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Branch;

namespace PharmacyV2.Controllers
{
    // CRUD for the WarehouseType lookup that Warehouse.WarehouseTypeId feeds
    // from. Previously seed-only (Main/Branch/Cold Storage). Pulled in as
    // part of completing Warehouse management, since Warehouse's own
    // controller (WarehousesController) can't work without this dropdown.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseTypesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public WarehouseTypesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/warehousetypes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<WarehouseTypeReadDto>>> GetAll()
        {
            var types = await _db.WarehouseTypes
                .AsNoTracking()
                .Select(t => new WarehouseTypeReadDto { Id = t.Id, Name = t.Name, WarehouseCount = t.Warehouses.Count })
                .ToListAsync();

            return Ok(types);
        }

        // GET api/warehousetypes/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<WarehouseTypeReadDto>> GetById(int id)
        {
            var type = await _db.WarehouseTypes
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new WarehouseTypeReadDto { Id = t.Id, Name = t.Name, WarehouseCount = t.Warehouses.Count })
                .FirstOrDefaultAsync();

            if (type is null)
                return NotFoundResponse($"Warehouse type {id} not found.");

            return Ok(type);
        }

        // POST api/warehousetypes — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<WarehouseTypeReadDto>> Create([FromBody] WarehouseTypeWriteDto dto)
        {
            if (await _db.WarehouseTypes.AnyAsync(t => t.Name == dto.Name))
                return ConflictResponse($"Warehouse type '{dto.Name}' already exists.");

            var type = new WarehouseType { Name = dto.Name };

            _db.WarehouseTypes.Add(type);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = type.Id },
                new WarehouseTypeReadDto { Id = type.Id, Name = type.Name, WarehouseCount = 0 });
        }

        // PUT api/warehousetypes/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] WarehouseTypeWriteDto dto)
        {
            var type = await _db.WarehouseTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Warehouse type {id} not found.");

            if (await _db.WarehouseTypes.AnyAsync(t => t.Name == dto.Name && t.Id != id))
                return ConflictResponse($"Warehouse type '{dto.Name}' already exists.");

            type.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/warehousetypes/5 — blocked while any warehouse still references it.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await _db.WarehouseTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Warehouse type {id} not found.");

            if (await _db.Warehouses.AnyAsync(w => w.WarehouseTypeId == id))
                return ConflictResponse("Cannot delete a warehouse type with warehouses assigned to it.");

            _db.WarehouseTypes.Remove(type);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
```
