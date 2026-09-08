using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.People;

namespace PharmacyV2.Controllers
{
    // Master (Customer) + details (CustomerAddress) CRUD.
    // CustomerTypeId is the relational/dropdown field — CustomerType itself
    // has no controller, it's seeded lookup data (see PharmacyDbContext).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IWebHostEnvironment _env;

        // Folder (under wwwroot) where customer photos physically live.
        private const string CustomerImagesRelativeFolder = "images/customers";

        public CustomersController(PharmacyDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // wwwroot may not exist yet on a fresh clone. Creates
        // "wwwroot/images/customers" on demand the first time it's needed.
        private string GetCustomerImagesFolderPath()
        {
            string webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            string folderPath = Path.Combine(webRoot, "images", "customers");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        // GET api/customers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerListItemDto>>> GetAll()
        {
            var customers = await _db.Customers
                .AsNoTracking()
                .Include(c => c.CustomerType)
                .Include(c => c.Addresses)
                .Select(c => new CustomerListItemDto
                {
                    CustomerId = c.CustomerId,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    Phone = c.Phone,
                    IsActive = c.IsActive,
                    HasPhoto = c.Photo != null || c.PhotoPath != null,
                    PhotoPath = c.PhotoPath,
                    CustomerTypeId = c.CustomerTypeId,
                    CustomerTypeName = c.CustomerType.Name,
                    Addresses = c.Addresses.Select(a => new CustomerAddressReadDto
                    {
                        Id = a.Id,
                        Label = a.Label,
                        AddressLine = a.AddressLine,
                        City = a.City,
                        IsDefault = a.IsDefault
                    }).ToList()
                })
                .ToListAsync();

            return Ok(customers);
        }

        // GET api/customers/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CustomerReadDto>> GetById(int id)
        {
            var customer = await _db.Customers
                .AsNoTracking()
                .Include(c => c.CustomerType)
                .Include(c => c.Addresses)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer is null)
                return NotFoundResponse($"Customer {id} not found.");

            return Ok(MapToReadDto(customer));
        }

        // POST api/customers — phone number must be unique if provided.
        [HttpPost]
        public async Task<ActionResult<CustomerReadDto>> Create([FromBody] CustomerCreateDto dto)
        {
            var typeExists = await _db.CustomerTypes.AnyAsync(t => t.Id == dto.CustomerTypeId);
            if (!typeExists)
                return BadRequestResponse($"Customer type {dto.CustomerTypeId} does not exist.");

            if (!string.IsNullOrWhiteSpace(dto.Phone) &&
                await _db.Customers.AnyAsync(c => c.Phone == dto.Phone))
            {
                return ConflictResponse($"Phone '{dto.Phone}' is already registered.");
            }

            var customer = new Customer
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Phone = dto.Phone,
                CreditLimit = dto.CreditLimit,
                DateOfBirth = dto.DateOfBirth,
                IsActive = dto.IsActive,
                Photo = dto.Photo,
                PhotoContentType = dto.PhotoContentType,
                PhotoPath = dto.PhotoPath,
                CustomerTypeId = dto.CustomerTypeId
            };

            if (dto.Addresses is { Count: > 0 })
            {
                foreach (var addressDto in dto.Addresses)
                {
                    customer.Addresses.Add(new CustomerAddress
                    {
                        Label = addressDto.Label,
                        AddressLine = addressDto.AddressLine,
                        City = addressDto.City,
                        IsDefault = addressDto.IsDefault
                    });
                }

                NormalizeDefaultAddress(customer.Addresses);
            }

            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            await _db.Entry(customer).Reference(c => c.CustomerType).LoadAsync();

            return CreatedAtAction(nameof(GetById), new { id = customer.CustomerId }, MapToReadDto(customer));
        }

        // PUT api/customers/5 — full update of master fields, with a full
        // add/update/delete sync of the Addresses collection.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CustomerUpdateDto dto)
        {
            var customer = await _db.Customers
                .Include(c => c.Addresses)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer is null)
                return NotFoundResponse($"Customer {id} not found.");

            var typeExists = await _db.CustomerTypes.AnyAsync(t => t.Id == dto.CustomerTypeId);
            if (!typeExists)
                return BadRequestResponse($"Customer type {dto.CustomerTypeId} does not exist.");

            if (!string.IsNullOrWhiteSpace(dto.Phone) &&
                await _db.Customers.AnyAsync(c => c.Phone == dto.Phone && c.CustomerId != id))
            {
                return ConflictResponse($"Phone '{dto.Phone}' is already registered.");
            }

            customer.FirstName = dto.FirstName;
            customer.LastName = dto.LastName;
            customer.Phone = dto.Phone;
            customer.CreditLimit = dto.CreditLimit;
            customer.DateOfBirth = dto.DateOfBirth;
            customer.IsActive = dto.IsActive;
            if (dto.Photo is not null) customer.Photo = dto.Photo;
            if (dto.PhotoContentType is not null) customer.PhotoContentType = dto.PhotoContentType;
            if (dto.PhotoPath is not null) customer.PhotoPath = dto.PhotoPath;
            customer.CustomerTypeId = dto.CustomerTypeId;

            if (dto.Addresses is not null)
            {
                var incomingIds = dto.Addresses.Where(a => a.Id is > 0).Select(a => a.Id!.Value).ToHashSet();

                var toRemove = customer.Addresses.Where(a => !incomingIds.Contains(a.Id)).ToList();
                foreach (var address in toRemove)
                    customer.Addresses.Remove(address);

                foreach (var addressDto in dto.Addresses)
                {
                    if (addressDto.Id is > 0)
                    {
                        var existingAddress = customer.Addresses.FirstOrDefault(a => a.Id == addressDto.Id);
                        if (existingAddress is null)
                            return BadRequestResponse($"Address {addressDto.Id} does not belong to customer {id}.");

                        existingAddress.Label = addressDto.Label;
                        existingAddress.AddressLine = addressDto.AddressLine;
                        existingAddress.City = addressDto.City;
                        existingAddress.IsDefault = addressDto.IsDefault;
                    }
                    else
                    {
                        customer.Addresses.Add(new CustomerAddress
                        {
                            Label = addressDto.Label,
                            AddressLine = addressDto.AddressLine,
                            City = addressDto.City,
                            IsDefault = addressDto.IsDefault
                        });
                    }
                }

                NormalizeDefaultAddress(customer.Addresses);
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/customers/5 — blocked once the customer has sales history;
        // addresses cascade-delete with the customer otherwise.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer is null)
                return NotFoundResponse($"Customer {id} not found.");

            if (await _db.Sales.AnyAsync(s => s.CustomerId == id))
                return ConflictResponse("Cannot delete a customer with existing sales.");

            _db.Customers.Remove(customer);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // POST api/customers/5/photo — upload/replace customer photo as an
        // actual file (multipart/form-data). In Postman: Body -> form-data ->
        // key "file", type "File" -> pick an image from disk.
        [HttpPost("{id:int}/photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(int id, IFormFile file)
        {
            var customer = await _db.Customers.FindAsync(id);
            if (customer is null) return NotFoundResponse($"Customer {id} not found.");

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

            string folderPath = GetCustomerImagesFolderPath();
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string fileName = id + "_" + shortGuid + extension;
            string fullFilePath = Path.Combine(folderPath, fileName);

            await System.IO.File.WriteAllBytesAsync(fullFilePath, fileBytes);

            // Remove the previous physical file, if any, now that the new one is saved.
            DeleteCustomerPhotoFile(customer.PhotoPath);

            customer.Photo = null; // stop keeping raw bytes in DB now that we have a file on disk
            customer.PhotoContentType = file.ContentType;
            customer.PhotoPath = "/" + CustomerImagesRelativeFolder + "/" + fileName;

            await _db.SaveChangesAsync();
            return Ok(new { customer.CustomerId, customer.PhotoPath });
        }

        // DELETE api/customers/5/photo — remove the customer's photo, both
        // the physical file and the DB fields.
        [HttpDelete("{id:int}/photo")]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            var customer = await _db.Customers.FindAsync(id);
            if (customer is null) return NotFoundResponse($"Customer {id} not found.");

            DeleteCustomerPhotoFile(customer.PhotoPath);

            customer.PhotoPath = null;
            customer.Photo = null;
            customer.PhotoContentType = null;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- Dedicated detail-row endpoints ----------

        // POST api/customers/5/addresses
        [HttpPost("{customerId:int}/addresses")]
        public async Task<ActionResult<CustomerAddressReadDto>> AddAddress(int customerId, [FromBody] CustomerAddressWriteDto dto)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (customer is null)
                return NotFoundResponse($"Customer {customerId} not found.");

            var address = new CustomerAddress
            {
                CustomerId = customerId,
                Label = dto.Label,
                AddressLine = dto.AddressLine,
                City = dto.City,
                IsDefault = dto.IsDefault
            };

            _db.CustomerAddresses.Add(address);
            await _db.SaveChangesAsync();

            if (address.IsDefault)
            {
                await ClearOtherDefaultAddressesAsync(customerId, address.Id);
                await _db.SaveChangesAsync();
            }

            return CreatedAtAction(nameof(GetById), new { id = customerId }, MapAddressToReadDto(address));
        }

        // PUT api/customers/5/addresses/9
        [HttpPut("{customerId:int}/addresses/{addressId:int}")]
        public async Task<IActionResult> UpdateAddress(int customerId, int addressId, [FromBody] CustomerAddressWriteDto dto)
        {
            var address = await _db.CustomerAddresses
                .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);

            if (address is null)
                return NotFoundResponse($"Address {addressId} not found for customer {customerId}.");

            address.Label = dto.Label;
            address.AddressLine = dto.AddressLine;
            address.City = dto.City;
            address.IsDefault = dto.IsDefault;

            await _db.SaveChangesAsync();
            if (address.IsDefault)
            {
                await ClearOtherDefaultAddressesAsync(customerId, address.Id);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }

        // DELETE api/customers/5/addresses/9
        [HttpDelete("{customerId:int}/addresses/{addressId:int}")]
        public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
        {
            var address = await _db.CustomerAddresses
                .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);

            if (address is null)
                return NotFoundResponse($"Address {addressId} not found for customer {customerId}.");

            _db.CustomerAddresses.Remove(address);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------- helpers ----------

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));

        private static void NormalizeDefaultAddress(ICollection<CustomerAddress> addresses)
        {
            var defaultAddress = addresses.LastOrDefault(a => a.IsDefault) ?? addresses.FirstOrDefault();
            foreach (var address in addresses)
                address.IsDefault = ReferenceEquals(address, defaultAddress);
        }

        private async Task ClearOtherDefaultAddressesAsync(int customerId, int keepAddressId)
        {
            var others = await _db.CustomerAddresses
                .Where(a => a.CustomerId == customerId && a.Id != keepAddressId && a.IsDefault)
                .ToListAsync();
            foreach (var other in others)
                other.IsDefault = false;
        }

        // Deletes the physical file behind a PhotoPath like
        // "/images/customers/5_a1b2...c1.jpg", if it exists. Safe to call
        // with null/empty/unrelated paths — it just does nothing then.
        private void DeleteCustomerPhotoFile(string? photoPath)
        {
            if (string.IsNullOrWhiteSpace(photoPath))
                return;

            if (photoPath.IndexOf(CustomerImagesRelativeFolder, StringComparison.OrdinalIgnoreCase) < 0)
                return; // not one of our managed files — don't touch it

            string fileName = Path.GetFileName(photoPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            string folderPath = GetCustomerImagesFolderPath();
            string fullFilePath = Path.Combine(folderPath, fileName);

            if (System.IO.File.Exists(fullFilePath))
            {
                System.IO.File.Delete(fullFilePath);
            }
        }

        private static CustomerReadDto MapToReadDto(Customer customer) => new()
        {
            CustomerId = customer.CustomerId,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Phone = customer.Phone,
            CreditLimit = customer.CreditLimit,
            DateOfBirth = customer.DateOfBirth,
            IsActive = customer.IsActive,
            Photo = customer.Photo,
            PhotoContentType = customer.PhotoContentType,
            PhotoPath = customer.PhotoPath,
            CustomerTypeId = customer.CustomerTypeId,
            CustomerTypeName = customer.CustomerType?.Name ?? string.Empty,
            Addresses = customer.Addresses.Select(MapAddressToReadDto).ToList()
        };

        private static CustomerAddressReadDto MapAddressToReadDto(CustomerAddress address) => new()
        {
            Id = address.Id,
            Label = address.Label,
            AddressLine = address.AddressLine,
            City = address.City,
            IsDefault = address.IsDefault
        };
    }
}
