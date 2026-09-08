using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.HR;

namespace PharmacyV2.Controllers
{
    // CRUD for the Department lookup table that Employee.DepartmentId feeds
    // from. Previously seed-only (see PharmacyDbContext) with no way to
    // add/rename/remove departments except editing the seed data by hand.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DepartmentsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public DepartmentsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/departments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DepartmentReadDto>>> GetAll()
        {
            var departments = await _db.Departments
                .AsNoTracking()
                .Select(d => new DepartmentReadDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    EmployeeCount = d.Employees.Count
                })
                .ToListAsync();

            return Ok(departments);
        }

        // GET api/departments/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<DepartmentReadDto>> GetById(int id)
        {
            var department = await _db.Departments
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new DepartmentReadDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    EmployeeCount = d.Employees.Count
                })
                .FirstOrDefaultAsync();

            if (department is null)
                return NotFoundResponse($"Department {id} not found.");

            return Ok(department);
        }

        // POST api/departments — Name must be unique (DB index).
        [HttpPost]
        public async Task<ActionResult<DepartmentReadDto>> Create([FromBody] DepartmentWriteDto dto)
        {
            if (await _db.Departments.AnyAsync(d => d.Name == dto.Name))
                return ConflictResponse($"Department '{dto.Name}' already exists.");

            var department = new Department { Name = dto.Name };

            _db.Departments.Add(department);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = department.Id },
                new DepartmentReadDto { Id = department.Id, Name = department.Name, EmployeeCount = 0 });
        }

        // PUT api/departments/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DepartmentWriteDto dto)
        {
            var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
            if (department is null)
                return NotFoundResponse($"Department {id} not found.");

            if (await _db.Departments.AnyAsync(d => d.Name == dto.Name && d.Id != id))
                return ConflictResponse($"Department '{dto.Name}' already exists.");

            department.Name = dto.Name;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/departments/5 — blocked while any employee still
        // references it (FK is Restrict, so this mirrors the DB behavior
        // with a clearer message).
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id);
            if (department is null)
                return NotFoundResponse($"Department {id} not found.");

            if (await _db.Employees.AnyAsync(e => e.DepartmentId == id))
                return ConflictResponse("Cannot delete a department with employees assigned to it.");

            _db.Departments.Remove(department);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}