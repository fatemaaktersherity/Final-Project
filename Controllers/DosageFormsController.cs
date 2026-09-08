using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // CRUD for the DosageForm lookup table — feeds the "Dosage Form" filter
    // dropdown on the Brand Search page and the DosageFormId FK on Product.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DosageFormsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public DosageFormsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/dosageforms
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DosageFormReadDto>>> GetAll()
        {
            var forms = await _db.DosageForms.AsNoTracking()
                .OrderBy(d => d.Name)
                .Select(d => new DosageFormReadDto { Id = d.Id, Name = d.Name, ProductCount = d.Products.Count })
                .ToListAsync();

            return Ok(forms);
        }

        // GET api/dosageforms/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DosageFormReadDto>> GetById(int id)
        {
            var form = await _db.DosageForms.AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new DosageFormReadDto { Id = d.Id, Name = d.Name, ProductCount = d.Products.Count })
                .FirstOrDefaultAsync();

            if (form is null) return NotFoundResponse($"Dosage form {id} not found.");
            return Ok(form);
        }

        // POST api/dosageforms
        [HttpPost]
        public async Task<ActionResult<DosageFormReadDto>> Create([FromBody] DosageFormWriteDto dto)
        {
            if (await _db.DosageForms.AnyAsync(d => d.Name == dto.Name))
                return ConflictResponse($"Dosage form '{dto.Name}' already exists.");

            var form = new DosageForm { Name = dto.Name };
            _db.DosageForms.Add(form);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = form.Id },
                new DosageFormReadDto { Id = form.Id, Name = form.Name, ProductCount = 0 });
        }

        // PUT api/dosageforms/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DosageFormWriteDto dto)
        {
            var form = await _db.DosageForms.FirstOrDefaultAsync(d => d.Id == id);
            if (form is null) return NotFoundResponse($"Dosage form {id} not found.");

            if (await _db.DosageForms.AnyAsync(d => d.Name == dto.Name && d.Id != id))
                return ConflictResponse($"Dosage form '{dto.Name}' already exists.");

            form.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/dosageforms/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var form = await _db.DosageForms.FirstOrDefaultAsync(d => d.Id == id);
            if (form is null) return NotFoundResponse($"Dosage form {id} not found.");

            if (await _db.Products.AnyAsync(p => p.DosageFormId == id))
                return ConflictResponse("Cannot delete a dosage form that products are using.");

            _db.DosageForms.Remove(form);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
