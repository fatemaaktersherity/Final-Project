using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.DTOs;
using PharmacyV2.Enums;
using PharmacyV2.Models.Accounts;
using PharmacyV2.Services;
using System.Security.Claims;

namespace PharmacyV2.Controllers
{
    // API for LedgerAccount, the General Ledger. Had a model + DbSet + fluent
    // config (including the CK_LedgerAccount_DebitXorCredit check constraint)
    // but no controller.
    //
    // Deliberately NOT a plain CRUD controller: a posted ledger line is an
    // accounting record, not an editable row, so there is no PUT and no hard
    // DELETE here — only Create (as a balanced multi-line journal entry) and
    // Reverse (which posts an offsetting entry rather than mutating history).
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class LedgerAccountsController : ControllerBase
    {
        private readonly PharmacyDbContext _db;
        private readonly IAccountingPostingService _accounting;

        public LedgerAccountsController(PharmacyDbContext db, IAccountingPostingService accounting)
        {
            _db = db;
            _accounting = accounting;
        }

        // GET api/ledgeraccounts — filterable posting list.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<LedgerLineReadDto>>> GetAll(
            [FromQuery] int? chartOfAccountId,
            [FromQuery] string? voucherNo,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var query = _db.LedgerAccounts.AsNoTracking().Include(l => l.ChartOfAccount).AsQueryable();

            if (chartOfAccountId is not null)
                query = query.Where(l => l.ChartOfAccountId == chartOfAccountId);
            if (!string.IsNullOrWhiteSpace(voucherNo))
                query = query.Where(l => l.VoucherNo == voucherNo);
            if (fromDate is not null)
                query = query.Where(l => l.TransactionDate >= fromDate);
            if (toDate is not null)
                query = query.Where(l => l.TransactionDate <= toDate);

            var lines = await query
                .OrderByDescending(l => l.TransactionDate)
                .Select(l => MapLine(l))
                .ToListAsync();

            return Ok(lines);
        }

        // GET api/ledgeraccounts/5 — a single posting line.
        [HttpGet("{id:int}")]
        public async Task<ActionResult<LedgerLineReadDto>> GetById(int id)
        {
            var line = await _db.LedgerAccounts
                .AsNoTracking()
                .Include(l => l.ChartOfAccount)
                .Where(l => l.Id == id)
                .Select(l => MapLine(l))
                .FirstOrDefaultAsync();

            if (line is null)
                return NotFoundResponse($"Ledger posting {id} not found.");

            return Ok(line);
        }

        // GET api/ledgeraccounts/voucher/JV-20260101-abcd1234 — every line of
        // one journal entry, i.e. the full transaction.
        [HttpGet("voucher/{voucherNo}")]
        public async Task<ActionResult<JournalEntryReadDto>> GetByVoucher(string voucherNo)
        {
            var lines = await _db.LedgerAccounts
                .AsNoTracking()
                .Include(l => l.ChartOfAccount)
                .Where(l => l.VoucherNo == voucherNo)
                .OrderBy(l => l.Id)
                .ToListAsync();

            if (lines.Count == 0)
                return NotFoundResponse($"No ledger postings found for voucher '{voucherNo}'.");

            return Ok(new JournalEntryReadDto
            {
                VoucherNo = voucherNo,
                TransactionDate = lines[0].TransactionDate,
                Lines = lines.Select(MapLine).ToList(),
                TotalDebit = lines.Sum(l => l.DebitAmount),
                TotalCredit = lines.Sum(l => l.CreditAmount)
            });
        }

        // POST api/ledgeraccounts/journal-entries — posts one balanced
        // double-entry transaction: 2+ lines sharing a VoucherNo, each line
        // strictly a debit OR a credit, total debits == total credits.
        [Authorize(Roles = "Admin")]
        [HttpPost("journal-entries")]
        public async Task<ActionResult<JournalEntryReadDto>> CreateJournalEntry([FromBody] JournalEntryCreateDto dto)
        {
            var postedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(postedBy)) return Unauthorized();
            if (dto.Lines.Count < 2)
                return BadRequestResponse("A journal entry needs at least two lines (one debit, one credit).");

            foreach (var line in dto.Lines)
            {
                bool isDebit = line.DebitAmount > 0 && line.CreditAmount == 0;
                bool isCredit = line.CreditAmount > 0 && line.DebitAmount == 0;
                if (!isDebit && !isCredit)
                    return BadRequestResponse("Each line must have exactly one of DebitAmount or CreditAmount greater than zero, not both or neither.");

                var account = await _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == line.ChartOfAccountId);
                if (account is null || !account.IsActive) return BadRequestResponse($"Account {line.ChartOfAccountId} does not exist or is inactive.");
                if (await _db.ChartOfAccounts.AnyAsync(a => a.ParentId == line.ChartOfAccountId)) return BadRequestResponse($"Account {line.ChartOfAccountId} is a header account and cannot receive ledger postings.");
            }

            decimal totalDebit = dto.Lines.Sum(l => l.DebitAmount);
            decimal totalCredit = dto.Lines.Sum(l => l.CreditAmount);
            if (totalDebit != totalCredit)
                return BadRequestResponse($"Entry is not balanced: total debits {totalDebit} != total credits {totalCredit}.");

            string voucherNo = string.IsNullOrWhiteSpace(dto.VoucherNo)
                ? $"JV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}"
                : dto.VoucherNo;

            if (await _db.LedgerAccounts.AnyAsync(l => l.VoucherNo == voucherNo))
                return ConflictResponse($"Voucher '{voucherNo}' already has postings — use a different VoucherNo.");

            var transactionDate = dto.TransactionDate ?? DateTime.Now;
            var postedLines = new List<LedgerAccount>();

            foreach (var lineDto in dto.Lines)
            {
                decimal previousBalance = await _db.LedgerAccounts
                    .Where(l => l.ChartOfAccountId == lineDto.ChartOfAccountId)
                    .OrderByDescending(l => l.TransactionDate).ThenByDescending(l => l.Id)
                    .Select(l => l.RunningBalance)
                    .FirstOrDefaultAsync();

                // Running balance = previous + debit - credit. This treats every
                // account on a debit-normal basis; for a credit-normal account
                // (revenue/liability/equity) a "rising" balance will show as
                // more negative — consistent, just invert the sign when you
                // display statements for those account types.
                decimal newBalance = previousBalance + lineDto.DebitAmount - lineDto.CreditAmount;

                var posted = new LedgerAccount
                {
                    ChartOfAccountId = lineDto.ChartOfAccountId,
                    VoucherNo = voucherNo,
                    TransactionDate = transactionDate,
                    DebitAmount = lineDto.DebitAmount,
                    CreditAmount = lineDto.CreditAmount,
                    RunningBalance = newBalance,
                    Description = lineDto.Description ?? dto.Description,
                    SourceType = dto.SourceType,
                    SourceId = dto.SourceId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = postedBy
                };

                postedLines.Add(posted);
                _db.LedgerAccounts.Add(posted);
            }

            await _db.SaveChangesAsync();
            await _accounting.RecalculateRunningBalancesAsync(postedLines.Select(l => l.ChartOfAccountId));

            foreach (var line in postedLines)
                await _db.Entry(line).Reference(l => l.ChartOfAccount).LoadAsync();

            return CreatedAtAction(nameof(GetByVoucher), new { voucherNo }, new JournalEntryReadDto
            {
                VoucherNo = voucherNo,
                TransactionDate = transactionDate,
                Lines = postedLines.Select(MapLine).ToList(),
                TotalDebit = totalDebit,
                TotalCredit = totalCredit
            });
        }

        // POST api/ledgeraccounts/voucher/JV-.../reverse — posts a new,
        // opposite-sign journal entry that cancels this one out, and flags
        // the original lines IsReversed so they're excluded from open
        // balances going forward. History is kept, nothing is edited/deleted.
        [Authorize(Roles = "Admin")]
        [HttpPost("voucher/{voucherNo}/reverse")]
        public async Task<ActionResult<JournalEntryReadDto>> ReverseVoucher(string voucherNo)
        {
            var reversedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(reversedBy)) return Unauthorized();
            var originalLines = await _db.LedgerAccounts
                .Where(l => l.VoucherNo == voucherNo)
                .ToListAsync();

            if (originalLines.Count == 0)
                return NotFoundResponse($"No ledger postings found for voucher '{voucherNo}'.");

            if (originalLines.Any(l => l.IsReversed))
                return ConflictResponse($"Voucher '{voucherNo}' has already been reversed.");

            string reversalVoucherNo = $"REV-{voucherNo}";
            if (await _db.LedgerAccounts.AnyAsync(l => l.VoucherNo == reversalVoucherNo))
                return ConflictResponse($"A reversal for voucher '{voucherNo}' already exists.");

            var reversalLines = new List<LedgerAccount>();

            foreach (var original in originalLines)
            {
                decimal previousBalance = await _db.LedgerAccounts
                    .Where(l => l.ChartOfAccountId == original.ChartOfAccountId)
                    .OrderByDescending(l => l.TransactionDate).ThenByDescending(l => l.Id)
                    .Select(l => l.RunningBalance)
                    .FirstOrDefaultAsync();

                // Swap debit <-> credit to cancel the original out.
                decimal newBalance = previousBalance + original.CreditAmount - original.DebitAmount;

                var reversal = new LedgerAccount
                {
                    ChartOfAccountId = original.ChartOfAccountId,
                    VoucherNo = reversalVoucherNo,
                    TransactionDate = DateTime.Now,
                    DebitAmount = original.CreditAmount,
                    CreditAmount = original.DebitAmount,
                    RunningBalance = newBalance,
                    Description = $"Reversal of {voucherNo}" + (original.Description is null ? "" : $" — {original.Description}"),
                    SourceType = original.SourceType,
                    SourceId = original.SourceId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = reversedBy
                };

                reversalLines.Add(reversal);
                _db.LedgerAccounts.Add(reversal);
                original.IsReversed = true;
            }

            await _db.SaveChangesAsync();
            await _accounting.RecalculateRunningBalancesAsync(reversalLines.Select(l => l.ChartOfAccountId));

            foreach (var line in reversalLines)
                await _db.Entry(line).Reference(l => l.ChartOfAccount).LoadAsync();

            return Ok(new JournalEntryReadDto
            {
                VoucherNo = reversalVoucherNo,
                TransactionDate = reversalLines[0].TransactionDate,
                Lines = reversalLines.Select(MapLine).ToList(),
                TotalDebit = reversalLines.Sum(l => l.DebitAmount),
                TotalCredit = reversalLines.Sum(l => l.CreditAmount)
            });
        }

        private static LedgerLineReadDto MapLine(LedgerAccount l) => new()
        {
            Id = l.Id,
            ChartOfAccountId = l.ChartOfAccountId,
            ChartOfAccountName = l.ChartOfAccount != null ? l.ChartOfAccount.Name : string.Empty,
            VoucherNo = l.VoucherNo,
            TransactionDate = l.TransactionDate,
            DebitAmount = l.DebitAmount,
            CreditAmount = l.CreditAmount,
            RunningBalance = l.RunningBalance,
            Description = l.Description,
            SourceType = l.SourceType,
            SourceId = l.SourceId,
            IsReversed = l.IsReversed,
            CreatedAt = l.CreatedAt,
            CreatedByUserId = l.CreatedByUserId
        };

        private ActionResult NotFoundResponse(string message) => NotFound(new ApiError(message));
        private ActionResult BadRequestResponse(string message) => BadRequest(new ApiError(message));
        private ActionResult ConflictResponse(string message) => Conflict(new ApiError(message));
    }
}
