using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.HR;

namespace PharmacyV2.Controllers
{
    // Master (Employee) + details (EmployeeDocument) CRUD.
    // DepartmentId is the relational/dropdown field — Department itself has
    // no controller, it's seeded lookup data (see PharmacyDbContext).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeesController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;

        // Folder (under wwwroot) where employee photos physically live, and
        // the matching public URL prefix used to build PhotoPath. Same
        // pattern as ProductsController.ProductImagesRelativeFolder.
        private const string EmployeeImagesRelativeFolder = "images/employees";

        public EmployeesController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // wwwroot may not exist yet on a fresh clone. Creates
        // "wwwroot/images/employees" on demand the first time it's needed.
        private string GetEmployeeImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "employees");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/employees?search=jane&page=1&pageSize=20
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeListItemDto>>> GetAll(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

            var query = _db.Employees.AsNoTracking().Include(e => e.Department).Include(e => e.Documents).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(e =>
                    e.FullName.Contains(search) ||
                    (e.Email != null && e.Email.Contains(search)));
            }

            var employees = await query
                .OrderBy(e => e.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new EmployeeListItemDto
                {
                    Id = e.Id,
                    FullName = e.FullName,
                    Email = e.Email,
                    Salary = e.Salary,
                    HireDate = e.HireDate,
                    IsActive = e.IsActive,
                    DepartmentId = e.DepartmentId,
                    DepartmentName = e.Department.Name,
                    HasPhoto = e.Photo != null || e.PhotoPath != null,
                    PhotoPath = e.PhotoPath,
                    Documents = e.Documents.Select(d => new EmployeeDocumentReadDto
                    {
                        Id = d.Id,
                        DocumentTitle = d.DocumentTitle,
                        DocumentNumber = d.DocumentNumber,
                        IssueDate = d.IssueDate,
                        ExpiryDate = d.ExpiryDate,
                        IsVerified = d.IsVerified
                    }).ToList()
                })
                .ToListAsync();

            return Ok(employees);
        }

        // GET api/employees/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<EmployeeReadDto>> GetById(int id)
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.Department)
                .Include(e => e.Documents)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee is null)
                return NotFoundResponse($"Employee {id} not found.");

            return Ok(MapToReadDto(employee));
        }

        // POST api/employees — create the master, optionally with nested documents.
        [HttpPost]
        public async Task<ActionResult<EmployeeReadDto>> Create([FromBody] EmployeeCreateDto dto)
        {
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == dto.DepartmentId);
            if (!departmentExists)
                return BadRequestResponse($"Department {dto.DepartmentId} does not exist.");

            var employee = new Employee
            {
                FullName = dto.FullName,
                Email = dto.Email,
                Salary = dto.Salary,
                HireDate = dto.HireDate,
                IsActive = dto.IsActive,
                Photo = dto.Photo,
                PhotoContentType = dto.PhotoContentType,
                PhotoPath = dto.PhotoPath,
                DepartmentId = dto.DepartmentId
            };

            if (dto.Documents is { Count: > 0 })
            {
                foreach (var docDto in dto.Documents)
                {
                    employee.Documents.Add(new EmployeeDocument
                    {
                        DocumentTitle = docDto.DocumentTitle,
                        DocumentNumber = docDto.DocumentNumber,
                        IssueDate = docDto.IssueDate,
                        ExpiryDate = docDto.ExpiryDate,
                        IsVerified = docDto.IsVerified
                    });
                }
            }

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync();

            await _db.Entry(employee).Reference(e => e.Department).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = employee.Id }, MapToReadDto(employee));
        }

        // PUT api/employees/5 — full update of master fields, with a full
        // add/update/delete sync of the Documents collection.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] EmployeeUpdateDto dto)
        {
            var employee = await _db.Employees
                .Include(e => e.Documents)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee is null)
                return NotFoundResponse($"Employee {id} not found.");

            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == dto.DepartmentId);
            if (!departmentExists)
                return BadRequestResponse($"Department {dto.DepartmentId} does not exist.");

            employee.FullName = dto.FullName;
            employee.Email = dto.Email;
            employee.Salary = dto.Salary;
            employee.HireDate = dto.HireDate;
            employee.IsActive = dto.IsActive;
            if (dto.Photo is not null) employee.Photo = dto.Photo;
            if (dto.PhotoContentType is not null) employee.PhotoContentType = dto.PhotoContentType;
            if (dto.PhotoPath is not null) employee.PhotoPath = dto.PhotoPath;
            employee.DepartmentId = dto.DepartmentId;

            if (dto.Documents is not null)
            {
                var incomingIds = dto.Documents.Where(d => d.Id is > 0).Select(d => d.Id!.Value).ToHashSet();

                // delete documents that were left out of the payload
                var toRemove = employee.Documents.Where(d => !incomingIds.Contains(d.Id)).ToList();
                foreach (var doc in toRemove)
                    employee.Documents.Remove(doc);

                foreach (var docDto in dto.Documents)
                {
                    if (docDto.Id is > 0)
                    {
                        var existingDoc = employee.Documents.FirstOrDefault(d => d.Id == docDto.Id);
                        if (existingDoc is null)
                            return BadRequestResponse($"Document {docDto.Id} does not belong to employee {id}.");

                        existingDoc.DocumentTitle = docDto.DocumentTitle;
                        existingDoc.DocumentNumber = docDto.DocumentNumber;
                        existingDoc.IssueDate = docDto.IssueDate;
                        existingDoc.ExpiryDate = docDto.ExpiryDate;
                        existingDoc.IsVerified = docDto.IsVerified;
                    }
                    else
                    {
                        employee.Documents.Add(new EmployeeDocument
                        {
                            DocumentTitle = docDto.DocumentTitle,
                            DocumentNumber = docDto.DocumentNumber,
                            IssueDate = docDto.IssueDate,
                            ExpiryDate = docDto.ExpiryDate,
                            IsVerified = docDto.IsVerified
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/employees/5 — documents cascade-delete with the employee.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (employee is null)
                return NotFoundResponse($"Employee {id} not found.");

            _db.Employees.Remove(employee);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/employees/5/photo — upload/replace the employee's photo as
        // an actual file (multipart/form-data), instead of hand-encoding
        // base64 into the JSON body. In Postman: Body -> form-data -> key
        // "file", type "File" -> pick an image from disk.
        //
        // Saves the file physically under wwwroot/images/employees and
        // stores the short public path in Employee.PhotoPath, e.g.
        // "/images/employees/5_a1b2c3d4.jpg", so it can be loaded straight
        // from <img src="https://your-host/images/employees/5_a1b2c3d4.jpg">
        // once app.UseStaticFiles() is serving wwwroot.
        [HttpPost("{id:int}/photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
        {
            var employee = await _db.Employees.FindAsync(id);
            if (employee is null) return NotFoundResponse($"Employee {id} not found.");

            if (file is null || file.Length == 0)
                return BadRequestResponse("No file was uploaded. Send it as form-data with key 'file'.");

            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            bool isAllowed = false;
            foreach (var allowedExtension in allowedExtensions)
            {
                if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            if (!isAllowed)
                return BadRequestResponse("Unsupported image type. Allowed: jpg, jpeg, png, gif, webp, bmp.");

            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            string folderPath = GetEmployeeImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteEmployeePhotoFile(employee.PhotoPath);

            employee.Photo = null; // stop keeping raw bytes in the DB now that we have a file on disk
            employee.PhotoContentType = file.ContentType;
            employee.PhotoPath = "/" + EmployeeImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { employee.Id, employee.PhotoPath });
        }

        // DELETE api/employees/5/photo — remove the employee's photo, both the
        // physical file under wwwroot/images/employees and the DB fields.
        [HttpDelete("{id:int}/photo")]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            var employee = await _db.Employees.FindAsync(id);
            if (employee is null) return NotFoundResponse($"Employee {id} not found.");

            DeleteEmployeePhotoFile(employee.PhotoPath);

            employee.PhotoPath = null;
            employee.Photo = null;
            employee.PhotoContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------
        // Handy in Postman when you only want to add/change/remove one
        // document without resending the whole employee payload.

        // POST api/employees/5/documents
        [HttpPost("{employeeId:int}/documents")]
        public async Task<ActionResult<EmployeeDocumentReadDto>> AddDocument(int employeeId, [FromBody] EmployeeDocumentWriteDto dto)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
            if (employee is null)
                return NotFoundResponse($"Employee {employeeId} not found.");

            var document = new EmployeeDocument
            {
                EmployeeId = employeeId,
                DocumentTitle = dto.DocumentTitle,
                DocumentNumber = dto.DocumentNumber,
                IssueDate = dto.IssueDate,
                ExpiryDate = dto.ExpiryDate,
                IsVerified = dto.IsVerified
            };

            _db.EmployeeDocuments.Add(document);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = employeeId }, MapDocumentToReadDto(document));
        }

        // PUT api/employees/5/documents/9
        [HttpPut("{employeeId:int}/documents/{documentId:int}")]
        public async Task<IActionResult> UpdateDocument(int employeeId, int documentId, [FromBody] EmployeeDocumentWriteDto dto)
        {
            var document = await _db.EmployeeDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId);

            if (document is null)
                return NotFoundResponse($"Document {documentId} not found for employee {employeeId}.");

            document.DocumentTitle = dto.DocumentTitle;
            document.DocumentNumber = dto.DocumentNumber;
            document.IssueDate = dto.IssueDate;
            document.ExpiryDate = dto.ExpiryDate;
            document.IsVerified = dto.IsVerified;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/employees/5/documents/9
        [HttpDelete("{employeeId:int}/documents/{documentId:int}")]
        public async Task<IActionResult> DeleteDocument(int employeeId, int documentId)
        {
            var document = await _db.EmployeeDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId);

            if (document is null)
                return NotFoundResponse($"Document {documentId} not found for employee {employeeId}.");

            _db.EmployeeDocuments.Remove(document);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- helpers ----------

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));

        // Deletes the physical file behind a PhotoPath like
        // "/images/employees/5_a1b2...c1.jpg", if it exists. Safe to call
        // with null/empty/unrelated paths — it just does nothing then.
        private void DeleteEmployeePhotoFile(string? photoPath)
        {
            if (string.IsNullOrWhiteSpace(photoPath))
                return;

            if (photoPath.IndexOf(EmployeeImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(photoPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetEmployeeImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private static EmployeeReadDto MapToReadDto(Employee employee) => new()
        {
            Id = employee.Id,
            FullName = employee.FullName,
            Email = employee.Email,
            Salary = employee.Salary,
            HireDate = employee.HireDate,
            IsActive = employee.IsActive,
            PhotoPath = employee.PhotoPath,
            PhotoContentType = employee.PhotoContentType,
            DepartmentId = employee.DepartmentId,
            DepartmentName = employee.Department?.Name ?? string.Empty,
            Documents = employee.Documents.Select(MapDocumentToReadDto).ToList()
        };

        private static EmployeeDocumentReadDto MapDocumentToReadDto(EmployeeDocument document) => new()
        {
            Id = document.Id,
            DocumentTitle = document.DocumentTitle,
            DocumentNumber = document.DocumentNumber,
            IssueDate = document.IssueDate,
            ExpiryDate = document.ExpiryDate,
            IsVerified = document.IsVerified
        };
    }
}
