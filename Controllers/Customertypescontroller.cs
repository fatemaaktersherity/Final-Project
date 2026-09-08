using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    // CRUD for the CustomerType lookup that Customer.CustomerTypeId feeds
    // from. Previously seed-only (Retail/Wholesale/Corporate).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CustomerTypesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public CustomerTypesController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/customertypes
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerTypeReadDto>>> GetAll()
        {
            var types = await _db.CustomerTypes
                .AsNoTracking()
                .Select(t => new CustomerTypeReadDto { Id = t.Id, Name = t.Name, CustomerCount = t.Customers.Count })
                .ToListAsync();

            return Ok(types);
        }

        // GET api/customertypes/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CustomerTypeReadDto>> GetById(int id)
        {
            var type = await _db.CustomerTypes
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new CustomerTypeReadDto { Id = t.Id, Name = t.Name, CustomerCount = t.Customers.Count })
                .FirstOrDefaultAsync();

            if (type is null)
                return NotFoundResponse($"Customer type {id} not found.");

            return Ok(type);
        }

        // POST api/customertypes — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<CustomerTypeReadDto>> Create([FromBody] CustomerTypeWriteDto dto)
        {
            if (await _db.CustomerTypes.AnyAsync(t => t.Name == dto.Name))
                return ConflictResponse($"Customer type '{dto.Name}' already exists.");

            var type = new CustomerType { Name = dto.Name };

            _db.CustomerTypes.Add(type);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = type.Id },
                new CustomerTypeReadDto { Id = type.Id, Name = type.Name, CustomerCount = 0 });
        }

        // PUT api/customertypes/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CustomerTypeWriteDto dto)
        {
            var type = await _db.CustomerTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Customer type {id} not found.");

            if (await _db.CustomerTypes.AnyAsync(t => t.Name == dto.Name && t.Id != id))
                return ConflictResponse($"Customer type '{dto.Name}' already exists.");

            type.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/customertypes/5 — blocked while any customer still references it.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var type = await _db.CustomerTypes.FirstOrDefaultAsync(t => t.Id == id);
            if (type is null)
                return NotFoundResponse($"Customer type {id} not found.");

            if (await _db.Customers.AnyAsync(c => c.CustomerTypeId == id))
                return ConflictResponse("Cannot delete a customer type with customers assigned to it.");

            _db.CustomerTypes.Remove(type);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}