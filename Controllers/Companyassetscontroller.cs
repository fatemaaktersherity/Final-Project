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
