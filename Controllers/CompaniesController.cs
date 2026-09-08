using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Product;

namespace PharmacyV2.Controllers
{
    // CRUD for the Company lookup table — feeds the "Company" filter
    // dropdown on the Brand Search page and the CompanyId FK on Product.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CompaniesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public CompaniesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/companies?search=square
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CompanyReadDto>>> GetAll([FromQuery] string? search)
        {
            var query = _db.Companies.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search));

            var companies = await query
                .OrderBy(c => c.Name)
                .Select(c => new CompanyReadDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    IsActive = c.IsActive,
                    ProductCount = c.Products.Count
                })
                .ToListAsync();

            return Ok(companies);
        }

        // GET api/companies/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CompanyReadDto>> GetById(int id)
        {
            var company = await _db.Companies.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CompanyReadDto { Id = c.Id, Name = c.Name, IsActive = c.IsActive, ProductCount = c.Products.Count })
                .FirstOrDefaultAsync();

            if (company is null) return NotFoundResponse($"Company {id} not found.");
            return Ok(company);
        }

        // POST api/companies
        [HttpPost]
        public async Task<ActionResult<CompanyReadDto>> Create([FromBody] CompanyWriteDto dto)
        {
            if (await _db.Companies.AnyAsync(c => c.Name == dto.Name))
                return ConflictResponse($"Company '{dto.Name}' already exists.");

            var company = new Company { Name = dto.Name, IsActive = dto.IsActive };
            _db.Companies.Add(company);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = company.Id },
                new CompanyReadDto { Id = company.Id, Name = company.Name, IsActive = company.IsActive, ProductCount = 0 });
        }

        // PUT api/companies/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CompanyWriteDto dto)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is null) return NotFoundResponse($"Company {id} not found.");

            if (await _db.Companies.AnyAsync(c => c.Name == dto.Name && c.Id != id))
                return ConflictResponse($"Company '{dto.Name}' already exists.");

            company.Name = dto.Name;
            company.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/companies/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is null) return NotFoundResponse($"Company {id} not found.");

            if (await _db.Products.AnyAsync(p => p.CompanyId == id))
                return ConflictResponse("Cannot delete a company that products are using.");

            _db.Companies.Remove(company);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
