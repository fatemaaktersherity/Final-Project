using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DoctorsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        public DoctorsController(PharmacyDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DoctorReadDto>>> GetAll([FromQuery] string? search)
        {
            var query = _db.Doctors.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.Name.Contains(search) || (d.Hospital != null && d.Hospital.Contains(search)));

            var doctors = await query.OrderBy(d => d.Name).ToListAsync();
            var counts = await _db.Prescriptions.AsNoTracking()
                .GroupBy(p => p.DoctorId)
                .Select(g => new { DoctorId = g.Key, Count = g.Count() })
                .ToListAsync();
            var countMap = counts.ToDictionary(x => x.DoctorId, x => x.Count);

            return Ok(doctors.Select(d => ToDto(d, countMap.GetValueOrDefault(d.DoctorId))));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<DoctorReadDto>> GetById(int id)
        {
            var doctor = await _db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor is null) return NotFound(new ApiError($"Doctor {id} not found."));
            var count = await _db.Prescriptions.CountAsync(p => p.DoctorId == id);
            return Ok(ToDto(doctor, count));
        }

        [HttpGet("{id:int}/prescriptions")]
        public async Task<ActionResult<IEnumerable<PrescriptionReadDto>>> GetPrescriptions(int id)
        {
            if (!await _db.Doctors.AnyAsync(d => d.DoctorId == id))
                return NotFound(new ApiError($"Doctor {id} not found."));

            var prescriptions = await _db.Prescriptions.AsNoTracking()
                .Include(p => p.Doctor).Include(p => p.Customer).Include(p => p.Items).ThenInclude(i => i.Product)
                .Where(p => p.DoctorId == id)
                .OrderByDescending(p => p.PrescriptionDate)
                .ToListAsync();

            return Ok(prescriptions.Select(PrescriptionsController.ToPrescriptionDto));
        }

        [HttpPost]
        public async Task<ActionResult<DoctorReadDto>> Create([FromBody] DoctorWriteDto dto)
        {
            var doctor = new Doctor
            {
                Name = dto.Name,
                Specialization = dto.Specialization,
                RegistrationNo = dto.RegistrationNo,
                Hospital = dto.Hospital,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address,
                IsActive = dto.IsActive
            };
            _db.Doctors.Add(doctor);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = doctor.DoctorId }, ToDto(doctor, 0));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DoctorWriteDto dto)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor is null) return NotFound(new ApiError($"Doctor {id} not found."));

            doctor.Name = dto.Name;
            doctor.Specialization = dto.Specialization;
            doctor.RegistrationNo = dto.RegistrationNo;
            doctor.Hospital = dto.Hospital;
            doctor.Phone = dto.Phone;
            doctor.Email = dto.Email;
            doctor.Address = dto.Address;
            doctor.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor is null) return NotFound(new ApiError($"Doctor {id} not found."));

            if (await _db.Prescriptions.AnyAsync(p => p.DoctorId == id))
                return Conflict(new ApiError("Cannot delete this doctor because prescriptions exist against them. Deactivate instead."));

            _db.Doctors.Remove(doctor);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static DoctorReadDto ToDto(Doctor d, int prescriptionCount) => new()
        {
            DoctorId = d.DoctorId,
            Name = d.Name,
            Specialization = d.Specialization,
            RegistrationNo = d.RegistrationNo,
            Hospital = d.Hospital,
            Phone = d.Phone,
            Email = d.Email,
            Address = d.Address,
            IsActive = d.IsActive,
            PrescriptionCount = prescriptionCount
        };
    }
}