using Microsoft.EntityFrameworkCore;
using PharmacyV2.Data;
using PharmacyV2.Enums;
using PharmacyV2.Models.Accounts;

namespace PharmacyV2.Services;

public interface IAccountingPostingService
{
    Task EnsureSystemAccountsAsync();
    Task PostAsync(LedgerSourceType sourceType, int sourceId, DateTime date, string description, params (string Code, decimal Debit, decimal Credit)[] lines);
    Task RecalculateRunningBalancesAsync(IEnumerable<int> chartOfAccountIds);
}

public sealed class AccountingPostingService : IAccountingPostingService
{
    private readonly PharmacyDbContext _db;
    public AccountingPostingService(PharmacyDbContext db) => _db = db;

    public async Task EnsureSystemAccountsAsync()
    {
        var accounts = new (string Code, string Name, string Type)[] { ("1000", "Cash on Hand", "Asset"), ("1010", "Bank Account", "Asset"), ("1020", "Mobile Financial Services", "Asset"), ("1100", "Accounts Receivable", "Asset"), ("1200", "Inventory", "Asset"), ("1500", "Fixed Assets", "Asset"), ("1600", "Accumulated Depreciation", "Asset"), ("2000", "Accounts Payable", "Liability"), ("2100", "Loans and Other Liabilities", "Liability"), ("4000", "Sales Revenue", "Revenue"), ("4100", "Sales Returns", "Revenue"), ("5000", "Cost of Goods Sold", "Expense"), ("5100", "Damage and Expiry Loss", "Expense"), ("5200", "Depreciation Expense", "Expense") };
        var codes = accounts.Select(x => x.Code).ToList();
        var existing = await _db.ChartOfAccounts.Where(a => codes.Contains(a.Code!)).Select(a => a.Code).ToListAsync();
        _db.ChartOfAccounts.AddRange(accounts.Where(a => !existing.Contains(a.Code)).Select(a => new ChartOfAccount { Code = a.Code, Name = a.Name, AccountType = a.Type, IsSystem = true, IsActive = true }));
        await _db.SaveChangesAsync();
    }

    public async Task PostAsync(LedgerSourceType sourceType, int sourceId, DateTime date, string description, params (string Code, decimal Debit, decimal Credit)[] lines)
    {
        if (lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit)) throw new InvalidOperationException("Automatic accounting entry is not balanced.");
        if (await _db.LedgerAccounts.AnyAsync(l => l.SourceType == sourceType && l.SourceId == sourceId)) return;
        var codes = lines.Select(x => x.Code).Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.Where(a => codes.Contains(a.Code!) && a.IsActive).ToDictionaryAsync(a => a.Code!);
        if (accounts.Count != codes.Count) throw new InvalidOperationException("Required system chart-of-account mapping is missing.");
        var voucher = $"AUTO-{sourceType}-{sourceId}";
        foreach (var line in lines.Where(x => x.Debit > 0 || x.Credit > 0))
        {
            var account = accounts[line.Code];
            _db.LedgerAccounts.Add(new LedgerAccount
            {
                ChartOfAccountId = account.Id,
                VoucherNo = voucher,
                TransactionDate = date,
                DebitAmount = line.Debit,
                CreditAmount = line.Credit,
                Description = description,
                SourceType = sourceType,
                SourceId = sourceId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = "system"
            });
        }
        // Persist first so new rows have deterministic IDs. Rebuild the
        // complete account sequence, not only the latest balance: this keeps
        // statements correct when a valid backdated transaction is entered.
        await _db.SaveChangesAsync();
        await RecalculateRunningBalancesAsync(accounts.Values.Select(a => a.Id));
    }

    public async Task RecalculateRunningBalancesAsync(IEnumerable<int> chartOfAccountIds)
    {
        foreach (var accountId in chartOfAccountIds.Distinct())
        {
            decimal balance = 0;
            var rows = await _db.LedgerAccounts.Where(l => l.ChartOfAccountId == accountId)
                .OrderBy(l => l.TransactionDate).ThenBy(l => l.Id).ToListAsync();
            foreach (var row in rows)
            {
                balance += row.DebitAmount - row.CreditAmount;
                row.RunningBalance = balance;
            }
        }
        await _db.SaveChangesAsync();
    }
}
