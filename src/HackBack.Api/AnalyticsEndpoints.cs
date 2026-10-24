using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics").WithTags("Analytics");

        // GET /api/v1/analytics/summary - сводная аналитика (замокана до подключения скоринга): все клиенты в insufficientData.
        group.MapGet("/summary", async (AccountRepository accounts) =>
        {
            var total = await accounts.CountAsync();
            var insufficientPercentage = total == 0 ? 0d : 100.0;

            return Results.Ok(new AnalyticsSummaryResponse(
                total,
                new RiskDistributionResponse(
                    new RiskLevelStatResponse(0, 0),
                    new RiskLevelStatResponse(0, 0),
                    new RiskLevelStatResponse(0, 0),
                    new RiskLevelStatResponse(total, insufficientPercentage)),
                new FinancialsResponse("EUR_CENTS", 0, 0, 0, 0),
                [],
                [],
                DateTime.UtcNow));
        })
        .RequireAuthorization()
        .WithSummary("Сводная аналитика и оценка потерь")
        .WithDescription("Агрегированные метрики по клиентам, распределению рисков и деньгам. Скоринг ещё не подключён: всё распределение уходит в insufficientData, финансовые показатели нулевые.")
        .Produces<AnalyticsSummaryResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}

public record RiskLevelStatResponse(int Count, double Percentage);

public record RiskDistributionResponse(
    RiskLevelStatResponse High,
    RiskLevelStatResponse Medium,
    RiskLevelStatResponse Low,
    RiskLevelStatResponse InsufficientData);

public record FinancialsResponse(
    string Currency,
    long TotalMonthlyRecurringRevenue,
    long RevenueAtRisk,
    long ExpectedLoss,
    long ImmediateBalanceDeficit);

public record ReasonBreakdownItem(string Reason, string Label, int AffectedCustomersCount);

public record ChurnDynamicsItem(string Period, int FactDeleted, int ForecastRisk);

public record AnalyticsSummaryResponse(
    int TotalCustomers,
    RiskDistributionResponse RiskDistribution,
    FinancialsResponse Financials,
    ReasonBreakdownItem[] ReasonsBreakdown,
    ChurnDynamicsItem[] ChurnDynamics,
    DateTime LastCalculatedAt);
