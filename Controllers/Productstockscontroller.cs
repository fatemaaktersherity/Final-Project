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
