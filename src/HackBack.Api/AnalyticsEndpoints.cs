using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics").WithTags("Analytics");

        // GET /api/v1/analytics/summary - сводная аналитика: распределение клиентов по полосам риска от порогов модели (финансовые показатели - нули до итерации скоринга).
        group.MapGet("/summary", async (AccountRepository accounts, ModelSettingsRepository modelSettings) =>
        {
            var settings = await modelSettings.GetAsync();
            var total = await accounts.CountAsync();
            var (high, medium, low, noScore) = await accounts.GetRiskBandCountsAsync(settings.HighRiskThreshold, settings.MediumRiskThreshold);
            double Pct(int n) => total == 0 ? 0 : Math.Round(n * 100.0 / total, 1);

            return Results.Ok(new AnalyticsSummaryResponse(
                total,
                new RiskDistributionResponse(
                    new RiskLevelStatResponse(high, Pct(high)),
                    new RiskLevelStatResponse(medium, Pct(medium)),
                    new RiskLevelStatResponse(low, Pct(low)),
                    new RiskLevelStatResponse(noScore, Pct(noScore))),
                new FinancialsResponse("EUR_CENTS", 0, 0, 0, 0),
                [],
                [],
                DateTime.UtcNow));
        })
        .RequireAuthorization()
        .WithSummary("Сводная аналитика и оценка потерь")
        .WithDescription("Агрегированные метрики по клиентам с распределением по полосам риска согласно порогам модели. Финансовые показатели нулевые до подключения скоринга.")
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
