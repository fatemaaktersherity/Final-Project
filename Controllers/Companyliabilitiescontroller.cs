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
