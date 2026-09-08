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
