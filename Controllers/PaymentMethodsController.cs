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