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