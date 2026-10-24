using System.Globalization;
using System.Text.Json;
using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics").WithTags("Analytics");

        // GET /api/v1/analytics/summary - сводная аналитика: агрегаты по persist-колонкам Accounts (полосы риска, MRR-деньги, топ-причины из PrimaryReasons) и строка динамики за текущую ISO-неделю.
        group.MapGet("/summary", async (AccountRepository accounts, ModelSettingsRepository modelSettings) =>
        {
            var settings = await modelSettings.GetAsync();
            var accountList = await accounts.GetAllAccountsAsync();
            var total = accountList.Count;

            var high = 0;
            var medium = 0;
            var low = 0;
            var noScore = 0;
            long totalMrr = 0;
            long revenueAtRisk = 0;
            long expectedLoss = 0;
            long immediateBalanceDeficit = 0;
            var topReasonCounts = new Dictionary<string, int>();

            foreach (var account in accountList)
            {
                totalMrr += account.MonthlyRevenue;
                revenueAtRisk += account.RevenueAtRisk;
                immediateBalanceDeficit += account.BalanceDeficit;

                if (account.RiskScore is not int score)
                {
                    noScore++;
                    continue;
                }

                if (score >= settings.HighRiskThreshold)
                {
                    high++;
                }
                else if (score >= settings.MediumRiskThreshold)
                {
                    medium++;
                }
                else
                {
                    low++;
                }

                expectedLoss += (long)Math.Round(account.MonthlyRevenue * score / 100.0);

                if (!string.IsNullOrEmpty(account.PrimaryReasons))
                {
                    var codes = JsonSerializer.Deserialize<string[]>(account.PrimaryReasons);
                    if (codes is { Length: > 0 })
                    {
                        var code = codes[0];
                        topReasonCounts[code] = topReasonCounts.GetValueOrDefault(code) + 1;
                    }
                }
            }

            double Pct(int n) => total == 0 ? 0 : Math.Round(n * 100.0 / total, 1);
            var reasonsBreakdown = RiskScorer.FactorReasons
                .Select(f => new ReasonBreakdownItem(f.Reason, f.Title, topReasonCounts.GetValueOrDefault(f.Code)))
                .OrderByDescending(r => r.AffectedCustomersCount)
                .ToArray();
            var now = DateTime.UtcNow;
            var week = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            var period = FormattableString.Invariant($"{now.Year:0000}-W{week:00}");
            var factDeleted = await accounts.CountDeletedServicesAsync();
            var churnDynamics = new[] { new ChurnDynamicsItem(period, factDeleted, high) };
            var lastCalculatedAt = accountList.Max(a => a.ScoredAt) ?? now;

            return Results.Ok(new AnalyticsSummaryResponse(
                total,
                new RiskDistributionResponse(
                    new RiskLevelStatResponse(high, Pct(high)),
                    new RiskLevelStatResponse(medium, Pct(medium)),
                    new RiskLevelStatResponse(low, Pct(low)),
                    new RiskLevelStatResponse(noScore, Pct(noScore))),
                new FinancialsResponse("EUR_CENTS", totalMrr, revenueAtRisk, expectedLoss, immediateBalanceDeficit),
                reasonsBreakdown,
                churnDynamics,
                lastCalculatedAt));
        })
        .RequireAuthorization()
        .WithSummary("Сводная аналитика и оценка потерь")
        .WithDescription("Агрегированные метрики по клиентам из persist-колонок: распределение по полосам риска, финансовые агрегаты (MRR, деньги под риском, ожидаемые потери, дефицит баланса - евроценты), разбор причин оттока и динамика оттока по ISO-неделе.")
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
