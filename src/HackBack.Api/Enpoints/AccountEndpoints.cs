using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").WithTags("Customers");

        // GET /api/v1/customers - курсорный (keyset) список клиентов: сортировка по колонке, поиск и фильтр по диапазону riskScore, null-скоры всегда в конце.
        group.MapGet("/", async (AccountRepository accounts,
                                 string? cursorOn = null,
                                 string? cursorNext = null,
                                 int takeAmount = 20,
                                 string? sortDirection = null,
                                 int? riskScoreMin = null,
                                 int? riskScoreMax = null,
                                 string? search = null) =>
        {
            var column = (cursorOn ?? "id").ToLowerInvariant();
            if (column is not ("id" or "email" or "balance" or "totalreplenished" or "riskscore"))
                return Results.BadRequest(new { message = $"Неизвестная колонка сортировки: {column}" });

            if (cursorNext != null && cursorOn == null)
                return Results.BadRequest(new { message = "cursorNext требует cursorOn" });

            var ascending = sortDirection?.ToLowerInvariant() == "asc";

            CustomersCursor? cursor = null;
            if (cursorNext != null && !CustomersCursor.TryParse(cursorNext, column, out cursor))
                return Results.BadRequest(new { message = "Некорректный cursorNext" });

            if (riskScoreMin.HasValue && riskScoreMax.HasValue && riskScoreMin.Value > riskScoreMax.Value)
                return Results.BadRequest(new { message = "riskScoreMin больше riskScoreMax" });

            takeAmount = Math.Clamp(takeAmount, 1, 100);

            var rows = await accounts.GetPageAsync(column, ascending, cursor, takeAmount + 1, riskScoreMin, riskScoreMax, search);
            var hasMore = rows.Count > takeAmount;
            if (hasMore)
                rows.RemoveAt(rows.Count - 1);

            return Results.Ok(new
            {
                items = rows.Select(r => CustomerItems.ToItem(r)),
                nextCursor = hasMore && rows.Count > 0 ? CustomersCursor.Format(rows[^1], column) : null,
            });
        })
        .RequireAuthorization()
        .WithSummary("Список клиентов с оценками риска")
        .WithDescription("Курсорная (keyset) пагинация: cursorOn/cursorNext/takeAmount/sortDirection, поиск по подстроке id/email и фильтр по диапазону riskScore.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/customers/{id} - детальная карточка клиента с балансовой сводкой, скорингом риска (факторы, лояльность, деньги под риском) и историей услуг и транзакций.
        group.MapGet("/{id:long}", async (long id, 
                                          AccountRepository accounts, 
                                          ModelSettingsRepository modelSettings) =>
        {
            var account = await accounts.GetByIdAsync(id);
            if (account is null)
                return Results.NotFound();

            var settings = await modelSettings.GetAsync();
            var services = await accounts.GetServicesByOwnerAsync(id);
            var transactions = await accounts.GetTransactionsByOwnersAsync([id]);

            var riskInput = new RiskInput(account, services, transactions, settings, DateTime.UtcNow);
            var riskResult = RiskScorer.Score(riskInput);
            var expectedLoss = account.RiskScore is int score
                ? (long)Math.Round(account.MonthlyRevenue * score / 100.0)
                : 0L;

            return Results.Ok(new
            {
                account.Id,
                account.Email,
                balances = new
                {
                    account.Balance,
                    account.WithdrawBalance,
                    account.BonusBalance,
                    account.BonusState,
                    account.TotalReplenished,
                    effectiveBalance = account.Balance + (account.BonusState == "unlocked" ? account.BonusBalance : 0),
                },
                riskAssessment = new
                {
                    riskScore = account.RiskScore,
                    loyaltyScore = RiskScorer.LoyaltyScore(riskInput),
                    revenueAtRisk = account.RevenueAtRisk,
                    expectedLoss,
                    riskFactors = riskResult.Factors
                        .Select(f => new RiskFactorResponse(f.Code, f.Title, f.Description, f.Points, f.ServiceId))
                        .ToArray(),
                },
                services,
                recentTransactions = transactions
                    .OrderByDescending(t => t.CreatedAt)
                    .ThenByDescending(t => t.Id)
                    .Take(20)
                    .Select(t => new { t.Id, t.Amount, t.Type, t.Status, t.CreatedAt }),
            });
        })
        .RequireAuthorization()
        .WithSummary("Детальная карточка клиента")
        .WithDescription("Балансы с effectiveBalance (с учётом bonusState), скоринг риска с факторами и лояльностью, MRR-деньги под риском, услуги владельца и последние транзакции. 404 - клиент не найден.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}

public record RiskFactorResponse(string Code, string Title, string Description, double WeightContribution, long? ServiceId);
