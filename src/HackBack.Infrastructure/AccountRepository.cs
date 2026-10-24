using HackBack.Domain;
using Microsoft.EntityFrameworkCore;

namespace HackBack.Infrastructure;

public class AccountRepository(AppDbContext db)
{
    public Task<List<Account>> GetPageAsync(string column, bool ascending, CustomersCursor? cursor, int take, int? riskScoreMin, int? riskScoreMax, string? search)
    {
        var query = ApplySearch(db.Accounts.AsQueryable(), search);

        if (riskScoreMin.HasValue)
            query = query.Where(a => a.RiskScore >= riskScoreMin);
        if (riskScoreMax.HasValue)
            query = query.Where(a => a.RiskScore <= riskScoreMax);

        switch (column)
        {
            case "email":
                if (cursor != null)
                {
                    var emailValue = cursor.TextValue!;
                    var emailId = cursor.Id;
                    query = ascending
                        ? query.Where(a => string.Compare(a.Email, emailValue) > 0 || (a.Email == emailValue && a.Id > emailId))
                        : query.Where(a => string.Compare(a.Email, emailValue) < 0 || (a.Email == emailValue && a.Id < emailId));
                }
                query = ascending
                    ? query.OrderBy(a => a.Email).ThenBy(a => a.Id)
                    : query.OrderByDescending(a => a.Email).ThenByDescending(a => a.Id);
                break;
            case "balance":
                if (cursor != null && cursor.NumberValue.HasValue)
                {
                    var balanceValue = cursor.NumberValue.Value;
                    var balanceId = cursor.Id;
                    query = ascending
                        ? query.Where(a => a.Balance > balanceValue || (a.Balance == balanceValue && a.Id > balanceId))
                        : query.Where(a => a.Balance < balanceValue || (a.Balance == balanceValue && a.Id < balanceId));
                }
                query = ascending
                    ? query.OrderBy(a => a.Balance).ThenBy(a => a.Id)
                    : query.OrderByDescending(a => a.Balance).ThenByDescending(a => a.Id);
                break;
            case "totalreplenished":
                if (cursor != null && cursor.NumberValue.HasValue)
                {
                    var totalValue = cursor.NumberValue.Value;
                    var totalId = cursor.Id;
                    query = ascending
                        ? query.Where(a => a.TotalReplenished > totalValue || (a.TotalReplenished == totalValue && a.Id > totalId))
                        : query.Where(a => a.TotalReplenished < totalValue || (a.TotalReplenished == totalValue && a.Id < totalId));
                }
                query = ascending
                    ? query.OrderBy(a => a.TotalReplenished).ThenBy(a => a.Id)
                    : query.OrderByDescending(a => a.TotalReplenished).ThenByDescending(a => a.Id);
                break;
            case "riskscore":
                if (cursor != null)
                {
                    var scoreId = cursor.Id;
                    if (cursor.NumberValue == null)
                    {
                        query = ascending
                            ? query.Where(a => a.RiskScore == null && a.Id > scoreId)
                            : query.Where(a => a.RiskScore == null && a.Id < scoreId);
                    }
                    else
                    {
                        var scoreValue = cursor.NumberValue.Value;
                        query = ascending
                            ? query.Where(a => a.RiskScore > scoreValue || (a.RiskScore == scoreValue && a.Id > scoreId) || a.RiskScore == null)
                            : query.Where(a => a.RiskScore < scoreValue || (a.RiskScore == scoreValue && a.Id < scoreId) || a.RiskScore == null);
                    }
                }
                query = ascending
                    ? query.OrderBy(a => a.RiskScore == null).ThenBy(a => a.RiskScore).ThenBy(a => a.Id)
                    : query.OrderByDescending(a => a.RiskScore).ThenByDescending(a => a.Id);
                break;
            default:
                if (cursor != null)
                {
                    var idValue = cursor.Id;
                    query = ascending
                        ? query.Where(a => a.Id > idValue)
                        : query.Where(a => a.Id < idValue);
                }
                query = ascending
                    ? query.OrderBy(a => a.Id)
                    : query.OrderByDescending(a => a.Id);
                break;
        }

        return query.Take(take).ToListAsync();
    }

    public Task<int> CountAsync() =>
        db.Accounts.CountAsync();

    public async Task<(int High, int Medium, int Low, int NoScore)> GetRiskBandCountsAsync(double highThreshold, double mediumThreshold)
    {
        var high = await db.Accounts.CountAsync(a => a.RiskScore >= highThreshold);
        var medium = await db.Accounts.CountAsync(a => a.RiskScore >= mediumThreshold && a.RiskScore < highThreshold);
        var low = await db.Accounts.CountAsync(a => a.RiskScore < mediumThreshold);
        var noScore = await db.Accounts.CountAsync(a => a.RiskScore == null);
        return (high, medium, low, noScore);
    }

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
