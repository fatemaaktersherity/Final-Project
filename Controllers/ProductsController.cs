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