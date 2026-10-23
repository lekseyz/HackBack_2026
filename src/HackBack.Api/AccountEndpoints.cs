using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AccountEndpoints
{
    private const string NoDataReason = "Недостаточно данных для оценки (скоринг будет подключён позже)";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").WithTags("Customers");

        // GET /api/v1/customers — пагинированный список клиентов; сортировка riskScore — мок, фолбэк на id.
        group.MapGet("/", async (AccountRepository accounts, int page = 1, int pageSize = 20, string? sortBy = null, string? sortDirection = null, string? riskLevel = null, string? search = null) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var items = await accounts.GetPageAsync(page, pageSize, sortBy, sortDirection ?? "desc", riskLevel, search);
            var total = !string.IsNullOrEmpty(riskLevel) && riskLevel != "no_data"
                ? 0
                : await accounts.CountAsync(search);

            return Results.Ok(new
            {
                items = items.Select(a => new
                {
                    a.Id,
                    a.Email,
                    a.Balance,
                    a.WithdrawBalance,
                    a.BonusBalance,
                    a.BonusState,
                    a.TotalReplenished,
                    riskScore = (double?)null,
                    riskLevel = "no_data",
                    status = "insufficient_data",
                    revenueAtRisk = 0L,
                    primaryReasons = new[] { NoDataReason },
                }),
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
            });
        })
        .RequireAuthorization()
        .WithSummary("Список клиентов с оценками риска")
        .WithDescription("Пагинация, сортировка (id/email/balance/totalReplenished/riskScore), поиск по подстроке id/email и фильтр по riskLevel.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/customers/{id} — детальная карточка клиента с балансовой сводкой и заглушкой риск-оценки.
        group.MapGet("/{id:long}", async (long id, AccountRepository accounts) =>
        {
            var account = await accounts.GetByIdAsync(id);
            if (account is null)
                return Results.NotFound();

            var services = await accounts.GetServicesByOwnerAsync(id);
            var transactions = await accounts.GetRecentTransactionsAsync(id, 20);

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
                    effectiveBalance = account.Balance + account.BonusBalance,
                },
                riskAssessment = new
                {
                    riskScore = (double?)null,
                    riskLevel = "no_data",
                    loyaltyScore = (double?)null,
                    revenueAtRisk = 0L,
                    expectedLoss = 0L,
                    riskFactors = Array.Empty<object>(),
                },
                services,
                recentTransactions = transactions.Select(t => new { t.Id, t.Amount, t.Type, t.Status, t.CreatedAt }),
            });
        })
        .RequireAuthorization()
        .WithSummary("Детальная карточка клиента")
        .WithDescription("Балансы с effectiveBalance, замоканная оценка риска, услуги владельца и последние транзакции. 404 — клиент не найден.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
