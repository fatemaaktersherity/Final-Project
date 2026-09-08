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
