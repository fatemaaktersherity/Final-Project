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
