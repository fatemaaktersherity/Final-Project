using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
    public class PrescriptionsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;
        private const string PrescriptionImagesRelativeFolder = "images/prescriptions";

        public PrescriptionsController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private string GetImagesFolderPath()
        {
            string webRoot = string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
            string folderPath = Path.Combine(webRoot, "images", "prescriptions");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            return folderPath;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PrescriptionReadDto>>> GetAll([FromQuery] int? customerId, [FromQuery] int? doctorId)
        {
            var query = _db.Prescriptions.AsNoTracking()
                .Include(p => p.Doctor).Include(p => p.Customer).Include(p => p.Items).ThenInclude(i => i.Product)
                .AsQueryable();
            if (customerId is not null) query = query.Where(p => p.CustomerId == customerId);
            if (doctorId is not null) query = query.Where(p => p.DoctorId == doctorId);

            var list = await query.OrderByDescending(p => p.PrescriptionDate).ToListAsync();
            return Ok(list.Select(ToPrescriptionDto));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<PrescriptionReadDto>> GetById(int id)
        {
            var p = await _db.Prescriptions.AsNoTracking()
                .Include(x => x.Doctor).Include(x => x.Customer).Include(x => x.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));
            return Ok(ToPrescriptionDto(p));
        }

        [HttpPost]
        public async Task<ActionResult<PrescriptionReadDto>> Create([FromBody] PrescriptionCreateDto dto)
        {
            if (!await _db.Doctors.AnyAsync(d => d.DoctorId == dto.DoctorId))
                return BadRequest(new ApiError($"Doctor {dto.DoctorId} does not exist."));
            if (!await _db.Customers.AnyAsync(c => c.CustomerId == dto.CustomerId))
                return BadRequest(new ApiError($"Customer {dto.CustomerId} does not exist."));
            if (dto.SaleId is not null && !await _db.Sales.AnyAsync(s => s.SaleId == dto.SaleId))
                return BadRequest(new ApiError($"Sale {dto.SaleId} does not exist."));

            var prescription = new Prescription
            {
                DoctorId = dto.DoctorId,
                CustomerId = dto.CustomerId,
                PrescriptionDate = dto.PrescriptionDate,
                Diagnosis = dto.Diagnosis,
                Notes = dto.Notes,
                SaleId = dto.SaleId,
                CreatedAt = DateTime.UtcNow
            };

            if (dto.Items is { Count: > 0 })
            {
                foreach (var itemDto in dto.Items)
                {
                    if (itemDto.ProductId is not null && !await _db.Products.AnyAsync(p => p.Id == itemDto.ProductId))
                        return BadRequest(new ApiError($"Product {itemDto.ProductId} does not exist."));

                    prescription.Items.Add(new PrescriptionItem
                    {
                        ProductId = itemDto.ProductId,
                        MedicineName = itemDto.MedicineName,
                        Dosage = itemDto.Dosage,
                        Duration = itemDto.Duration,
                        Instructions = itemDto.Instructions
                    });
                }
            }

            _db.Prescriptions.Add(prescription);
            await _db.SaveChangesAsync();

            await _db.Entry(prescription).Reference(p => p.Doctor).LoadAsync();
            await _db.Entry(prescription).Reference(p => p.Customer).LoadAsync();
            await _db.Entry(prescription).Collection(p => p.Items).Query().Include(i => i.Product).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = prescription.PrescriptionId }, ToPrescriptionDto(prescription));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PrescriptionUpdateDto dto)
        {
            var existing = await _db.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.PrescriptionId == id);
            if (existing is null) return NotFound(new ApiError($"Prescription {id} not found."));

            if (!await _db.Doctors.AnyAsync(d => d.DoctorId == dto.DoctorId))
                return BadRequest(new ApiError($"Doctor {dto.DoctorId} does not exist."));
            if (!await _db.Customers.AnyAsync(c => c.CustomerId == dto.CustomerId))
                return BadRequest(new ApiError($"Customer {dto.CustomerId} does not exist."));
            if (dto.SaleId is not null && !await _db.Sales.AnyAsync(s => s.SaleId == dto.SaleId))
                return BadRequest(new ApiError($"Sale {dto.SaleId} does not exist."));

            existing.DoctorId = dto.DoctorId;
            existing.CustomerId = dto.CustomerId;
            existing.PrescriptionDate = dto.PrescriptionDate ?? existing.PrescriptionDate;
            existing.Diagnosis = dto.Diagnosis;
            existing.Notes = dto.Notes;
            existing.SaleId = dto.SaleId;

            if (dto.Items is not null)
            {
                var incomingIds = dto.Items.Where(i => i.Id is > 0).Select(i => i.Id!.Value).ToHashSet();

                var toRemove = existing.Items.Where(i => !incomingIds.Contains(i.Id)).ToList();
                foreach (var item in toRemove)
                {
                    _db.PrescriptionItems.Remove(item);
                    existing.Items.Remove(item);
                }

                foreach (var itemDto in dto.Items.Where(i => i.Id is > 0))
                {
                    var item = existing.Items.FirstOrDefault(i => i.Id == itemDto.Id);
                    if (item is null) continue;
                    item.ProductId = itemDto.ProductId;
                    item.MedicineName = itemDto.MedicineName;
                    item.Dosage = itemDto.Dosage;
                    item.Duration = itemDto.Duration;
                    item.Instructions = itemDto.Instructions;
                }

                foreach (var itemDto in dto.Items.Where(i => i.Id is null or 0))
                {
                    existing.Items.Add(new PrescriptionItem
                    {
                        ProductId = itemDto.ProductId,
                        MedicineName = itemDto.MedicineName,
                        Dosage = itemDto.Dosage,
                        Duration = itemDto.Duration,
                        Instructions = itemDto.Instructions
                    });
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var p = await _db.Prescriptions.FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));

            DeleteImageFile(p.ImagePath);
            _db.Prescriptions.Remove(p);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id:int}/image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImage(int id, IFormFile file)
        {
            var p = await _db.Prescriptions.FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));
            if (file is null || file.Length == 0) return BadRequest(new ApiError("No file was uploaded. Send it as form-data with key 'file'."));

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            string[] allowed = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            if (!allowed.Contains(extension.ToLowerInvariant()))
                return BadRequest(new ApiError("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp."));

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            string folderPath = GetImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N")[..8];
            string fileName = $"{id}_{shortGuid}{extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folderPath, fileName), ms.ToArray());

            DeleteImageFile(p.ImagePath);
            p.Image = null;
            p.ImageContentType = file.ContentType;
            p.ImagePath = "/" + PrescriptionImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { p.PrescriptionId, p.ImagePath });
        }

        [HttpDelete("{id:int}/image")]
        public async Task<IActionResult> DeleteImage(int id)
        {
            var p = await _db.Prescriptions.FirstOrDefaultAsync(x => x.PrescriptionId == id);
            if (p is null) return NotFound(new ApiError($"Prescription {id} not found."));

            DeleteImageFile(p.ImagePath);
            p.ImagePath = null;
            p.Image = null;
            p.ImageContentType = null;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private void DeleteImageFile(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return;
            if (imagePath.IndexOf(PrescriptionImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0) return;
            string fileName = Path.GetFileName(imagePath);
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string fullPath = Path.Combine(GetImagesFolderPath(), fileName);
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }

        public static PrescriptionReadDto ToPrescriptionDto(Prescription p) => new()
        {
            PrescriptionId = p.PrescriptionId,
            DoctorId = p.DoctorId,
            DoctorName = p.Doctor?.Name,
            CustomerId = p.CustomerId,
            CustomerName = p.Customer is null ? null : $"{p.Customer.FirstName} {p.Customer.LastName}".Trim(),
            PrescriptionDate = p.PrescriptionDate,
            Diagnosis = p.Diagnosis,
            Notes = p.Notes,
            ImagePath = p.ImagePath,
            ImageContentType = p.ImageContentType,
            SaleId = p.SaleId,
            CreatedAt = p.CreatedAt,
            Items = p.Items.Select(i => new PrescriptionItemReadDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product?.ProductName,
                MedicineName = i.MedicineName,
                Dosage = i.Dosage,
                Duration = i.Duration,
                Instructions = i.Instructions
            }).ToList()
        };
    }
}