using System.Diagnostics;
using System.Globalization;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /metrics - выгрузка метрик Prometheus (без авторизации, по спеке): полосы риска, деньги под риском и ожидаемые потери из persist-колонок Accounts.
        app.MapGet("/metrics", async (AccountRepository accounts, ModelSettingsRepository modelSettings) =>
        {
            // гы украл из аналитики, пока так
            var stopwatch = Stopwatch.StartNew();
            var settings = await modelSettings.GetAsync();
            var accountList = await accounts.GetAllAccountsAsync();
            var total = accountList.Count;

            var high = 0;
            var medium = 0;
            var low = 0;
            var noScore = 0;
            long revenueAtRisk = 0;
            long expectedLoss = 0;

            foreach (var account in accountList)
            {
                revenueAtRisk += account.RevenueAtRisk;

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
            }
            stopwatch.Stop();

            var payload = string.Create(CultureInfo.InvariantCulture, $$"""
                # HELP churn_customers_total Общее количество клиентов в системе
                # TYPE churn_customers_total gauge
                churn_customers_total {{total}}

                # HELP churn_customers_by_risk_level Количество клиентов по уровням риска
                # TYPE churn_customers_by_risk_level gauge
                churn_customers_by_risk_level{level="high"} {{high}}
                churn_customers_by_risk_level{level="medium"} {{medium}}
                churn_customers_by_risk_level{level="low"} {{low}}
                churn_customers_by_risk_level{level="insufficient_data"} {{noScore}}

                # HELP churn_revenue_at_risk_eurocents Потенциальная сумма потерь бизнеса в еврокопейках
                # TYPE churn_revenue_at_risk_eurocents gauge
                churn_revenue_at_risk_eurocents {{revenueAtRisk}}

                # HELP churn_expected_loss_eurocents Математическое ожидание потерь в еврокопейках
                # TYPE churn_expected_loss_eurocents gauge
                churn_expected_loss_eurocents {{expectedLoss}}

                # HELP churn_model_calculation_duration_seconds Время расчёта модели риска
                # TYPE churn_model_calculation_duration_seconds gauge
                churn_model_calculation_duration_seconds {{stopwatch.Elapsed.TotalSeconds}}
                """);

            return Results.Text(payload, "text/plain; version=0.0.4");
        })
        .WithSummary("Метрики Prometheus")
        .WithDescription("Выгрузка агрегированных churn-метрик в формате text/plain; version=0.0.4. Доступно без авторизации.")
        .Produces(StatusCodes.Status200OK);

        return app;
    }
}
