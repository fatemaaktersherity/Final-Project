using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Models.Accounts;

namespace PharmacyV2.Controllers
{
    // CRUD for ChartOfAccount, the self-referencing tree of account
    // definitions (Cash, Sales Revenue, Accounts Payable, ...) that
    // LedgerAccount postings point at. Had a model + DbSet + fluent config
    // but no controller.
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChartOfAccountsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;

        public ChartOfAccountsController(PharmacyDbContext db)
        {
            _db = db;
        }

        // GET api/chartofaccounts — flat list, optionally ?parentId=3 for one branch's
        // direct children, or ?parentId=0 / omitted for top-level roots + everything
        // (pass parentId to page through the tree from the client).
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ChartOfAccountListItemDto>>> GetAll([FromQuery] int? parentId)
        {
            var query = _db.ChartOfAccounts.AsNoTracking().Include(a => a.Parent).AsQueryable();

            if (parentId is not null)
                query = query.Where(a => a.ParentId == parentId);

            var accounts = await query
                .Select(a => new ChartOfAccountListItemDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Code = a.Code,
                    AccountType = a.AccountType,
                    ParentId = a.ParentId,
                    ParentName = a.Parent != null ? a.Parent.Name : null,
                    IsActive = a.IsActive,
                    IsSystem = a.IsSystem,
                    BudgetAmount = a.BudgetAmount,
                    ChildCount = a.Children.Count
                })
                .ToListAsync();

            return Ok(accounts);
        }

        // GET api/chartofaccounts/5 — includes its direct children.
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ChartOfAccountReadDto>> GetById(int id)
        {
            var account = await _db.ChartOfAccounts
                .AsNoTracking()
                .Include(a => a.Parent)
                .Include(a => a.Children)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (account is null)
                return NotFoundResponse($"Account {id} not found.");

            return Ok(new ChartOfAccountReadDto
            {
                Id = account.Id,
                Name = account.Name,
                Code = account.Code,
                AccountType = account.AccountType,
                ParentId = account.ParentId,
                ParentName = account.Parent?.Name,
                IsActive = account.IsActive,
                IsSystem = account.IsSystem,
                BudgetAmount = account.BudgetAmount,
                ChildCount = account.Children.Count,
                Children = account.Children.Select(c => new ChartOfAccountListItemDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Code = c.Code,
                    AccountType = c.AccountType,
                    ParentId = c.ParentId,
                    ParentName = account.Name,
                    IsActive = c.IsActive,
                    IsSystem = c.IsSystem,
                    BudgetAmount = c.BudgetAmount,
                    ChildCount = 0
                }).ToList()
            });
        }

        // POST api/chartofaccounts
        [HttpPost]
        public async Task<ActionResult<ChartOfAccountReadDto>> Create([FromBody] ChartOfAccountWriteDto dto)
        {
            if (!IsValidAccountType(dto.AccountType)) return BadRequestResponse("AccountType must be Asset, Liability, Equity, Revenue, or Expense.");
            if (!string.IsNullOrWhiteSpace(dto.Code) && await _db.ChartOfAccounts.AnyAsync(a => a.Code == dto.Code)) return ConflictResponse($"Account code '{dto.Code}' is already in use.");
            if (dto.ParentId is not null && !await _db.ChartOfAccounts.AnyAsync(a => a.Id == dto.ParentId))
                return BadRequestResponse($"Parent account {dto.ParentId} does not exist.");

            var account = new ChartOfAccount
            {
                Name = dto.Name,
                Code = dto.Code,
                AccountType = dto.AccountType,
                ParentId = dto.ParentId,
                IsActive = dto.IsActive,
                BudgetAmount = dto.BudgetAmount
            };

            _db.ChartOfAccounts.Add(account);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = account.Id }, new ChartOfAccountReadDto
            {
                Id = account.Id,
                Name = account.Name,
                Code = account.Code,
                AccountType = account.AccountType,
                ParentId = account.ParentId,
                IsActive = account.IsActive,
                IsSystem = account.IsSystem,
                BudgetAmount = account.BudgetAmount
            });
        }

        // PUT api/chartofaccounts/5 — IsSystem accounts (seeded/built-in, e.g.
        // a default Cash or Accounts Payable account) can't be re-parented or
        // renamed, since other modules may assume they exist under a fixed
        // name/Code; toggling IsActive on them is still allowed.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ChartOfAccountWriteDto dto)
        {
            if (!IsValidAccountType(dto.AccountType)) return BadRequestResponse("AccountType must be Asset, Liability, Equity, Revenue, or Expense.");
            var account = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null)
                return NotFoundResponse($"Account {id} not found.");

            if (await _db.LedgerAccounts.AnyAsync(l => l.ChartOfAccountId == id) &&
                (account.AccountType != dto.AccountType || account.ParentId != dto.ParentId || account.Code != dto.Code))
                return ConflictResponse("An account with ledger postings cannot have its type, code, or hierarchy changed.");

            if (account.IsSystem &&
                (account.Name != dto.Name || account.AccountType != dto.AccountType || account.ParentId != dto.ParentId))
            {
                return ConflictResponse("This is a system account — only IsActive/BudgetAmount can be changed on it.");
            }

            if (dto.ParentId == id)
                return BadRequestResponse("An account cannot be its own parent.");

            if (dto.ParentId is not null)
            {
                if (!await _db.ChartOfAccounts.AnyAsync(a => a.Id == dto.ParentId))
                    return BadRequestResponse($"Parent account {dto.ParentId} does not exist.");

                if (await WouldCreateCycleAsync(id, dto.ParentId.Value))
                    return BadRequestResponse("That parent is a descendant of this account — would create a circular hierarchy.");
            }

            if (!string.IsNullOrWhiteSpace(dto.Code) && await _db.ChartOfAccounts.AnyAsync(a => a.Code == dto.Code && a.Id != id)) return ConflictResponse($"Account code '{dto.Code}' is already in use.");

            account.Name = dto.Name;
            account.Code = dto.Code;
            account.AccountType = dto.AccountType;
            account.ParentId = dto.ParentId;
            account.IsActive = dto.IsActive;
            account.BudgetAmount = dto.BudgetAmount;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE api/chartofaccounts/5 — blocked for system accounts, accounts
        // with children, or accounts with existing ledger postings.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var account = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account is null)
                return NotFoundResponse($"Account {id} not found.");

            if (account.IsSystem)
                return ConflictResponse("Cannot delete a system account.");

            if (await _db.ChartOfAccounts.AnyAsync(a => a.ParentId == id))
                return ConflictResponse("Cannot delete an account that has child accounts — remove or reassign them first.");

            if (await _db.LedgerAccounts.AnyAsync(l => l.ChartOfAccountId == id))
                return ConflictResponse("Cannot delete an account with existing ledger postings.");

            _db.ChartOfAccounts.Remove(account);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // Walks upward from candidateParentId through Parent links; if it
        // reaches accountId, setting that parent on accountId would create a cycle.
        private async Task<bool> WouldCreateCycleAsync(int accountId, int candidateParentId)
        {
            int? currentId = candidateParentId;
            var visited = new HashSet<int>();

            while (currentId is not null)
            {
                if (currentId == accountId)
                    return true;

                if (!visited.Add(currentId.Value))
                    break; // already-broken cycle elsewhere in the data; stop rather than loop forever

                currentId = await _db.ChartOfAccounts
                    .Where(a => a.Id == currentId)
                    .Select(a => a.ParentId)
                    .FirstOrDefaultAsync();
            }

            return false;
        }

        private static bool IsValidAccountType(string type) => new[] { "Asset", "Liability", "Equity", "Revenue", "Expense" }.Contains(type, StringComparer.OrdinalIgnoreCase);

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
