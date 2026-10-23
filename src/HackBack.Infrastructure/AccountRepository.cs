using HackBack.Domain;
using Microsoft.EntityFrameworkCore;

namespace HackBack.Infrastructure;

public class AccountRepository(AppDbContext db)
{
    public Task<List<Account>> GetPageAsync(int page, int pageSize, string? sortBy, string sortDirection, string? riskLevel, string? search)
    {
        if (!string.IsNullOrEmpty(riskLevel) && riskLevel != "no_data")
            return Task.FromResult(new List<Account>());

        var query = ApplySearch(db.Accounts.AsQueryable(), search);

        var sort = sortBy?.ToLowerInvariant();
        var descending = sortDirection?.ToLowerInvariant() != "asc";
        query = sort switch
        {
            "email" => descending ? query.OrderByDescending(a => a.Email) : query.OrderBy(a => a.Email),
            "balance" => descending ? query.OrderByDescending(a => a.Balance) : query.OrderBy(a => a.Balance),
            "totalreplenished" => descending ? query.OrderByDescending(a => a.TotalReplenished) : query.OrderBy(a => a.TotalReplenished),
            _ => descending ? query.OrderByDescending(a => a.Id) : query.OrderBy(a => a.Id),
        };

        return query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public Task<int> CountAsync(string? search) =>
        ApplySearch(db.Accounts.AsQueryable(), search).CountAsync();

    public Task<int> CountAsync() =>
        db.Accounts.CountAsync();

    public Task<Account?> GetByIdAsync(long id) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id);

    public Task<List<Service>> GetServicesByOwnerAsync(long ownerId) =>
        db.Services.Where(s => s.OwnerId == ownerId).ToListAsync();

    public Task<List<Transaction>> GetRecentTransactionsAsync(long ownerId, int take) =>
        db.Transactions
            .Join(db.Services, t => t.ServiceId, s => s.Id, (t, s) => new { Transaction = t, s.OwnerId })
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.Transaction.CreatedAt)
            .ThenByDescending(x => x.Transaction.Id)
            .Take(take)
            .Select(x => x.Transaction)
            .ToListAsync();

    public async Task ReplaceAllAsync(List<Account> accounts, List<Service> services, List<Transaction> transactions)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Transactions.ExecuteDeleteAsync();
        await db.Services.ExecuteDeleteAsync();
        await db.Accounts.ExecuteDeleteAsync();
        db.Accounts.AddRange(accounts);
        db.Services.AddRange(services);
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static IQueryable<Account> ApplySearch(IQueryable<Account> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var pattern = search.Trim().ToLower();
        return query.Where(a => a.Email.ToLower().Contains(pattern) || a.Id.ToString().Contains(pattern));
    }
}
