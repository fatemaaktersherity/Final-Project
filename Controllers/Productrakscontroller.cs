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