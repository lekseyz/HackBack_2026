using System.Security.Claims;
using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class ModelEndpoints
{
    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/model").WithTags("Model");

        // GET /api/v1/model/settings — текущие веса и пороги модели; при первом запросе создаётся запись с дефолтами.
        group.MapGet("/settings", async (ModelSettingsRepository repository) =>
        {
            var settings = await repository.GetAsync();
            return Results.Ok(ToSettingsResponse(settings));
        })
        .RequireAuthorization()
        .WithSummary("Получение настроек модели скоринга")
        .WithDescription("Возвращает весовые коэффициенты признаков и пороги уровней риска. При первом запросе создаётся запись с дефолтами.")
        .Produces<SettingsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // PATCH /api/v1/model/settings — частичное обновление только переданных весов/порогов с валидацией и сохранением в БД.
        group.MapPatch("/settings", async (PatchSettingsRequest request, ModelSettingsRepository repository, ClaimsPrincipal principal) =>
        {
            var settings = await repository.GetAsync();

            if (request.Weights is not null)
            {
                var weights = request.Weights;
                if (weights.ProlongWaitWithoutFundsWeight is < 0
                    || weights.AutoprolongDisabledExpiringWeight is < 0
                    || weights.AutoprolongEnabledDeficitWeight is < 0
                    || weights.SuspendedServiceWeight is < 0
                    || weights.RecentFailedProlongationWeight is < 0)
                    return Results.BadRequest(new { success = false, message = "Веса не могут быть отрицательными" });

                if (weights.ProlongWaitWithoutFundsWeight is { } prolongWait) settings.ProlongWaitWithoutFundsWeight = prolongWait;
                if (weights.AutoprolongDisabledExpiringWeight is { } disabledExpiring) settings.AutoprolongDisabledExpiringWeight = disabledExpiring;
                if (weights.AutoprolongEnabledDeficitWeight is { } enabledDeficit) settings.AutoprolongEnabledDeficitWeight = enabledDeficit;
                if (weights.SuspendedServiceWeight is { } suspended) settings.SuspendedServiceWeight = suspended;
                if (weights.RecentFailedProlongationWeight is { } failedProlongation) settings.RecentFailedProlongationWeight = failedProlongation;
            }

            if (request.Thresholds is not null)
            {
                var thresholds = request.Thresholds;
                if (thresholds.HighRiskThreshold is { } high) settings.HighRiskThreshold = high;
                if (thresholds.MediumRiskThreshold is { } medium) settings.MediumRiskThreshold = medium;
                if (thresholds.ImminentExpiryDays is { } days) settings.ImminentExpiryDays = days;
            }

            if (settings.HighRiskThreshold <= settings.MediumRiskThreshold)
                return Results.BadRequest(new { success = false, message = "highRiskThreshold должен быть больше mediumRiskThreshold" });

            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedBy = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name ?? "unknown";
            await repository.SaveAsync(settings);

            return Results.Ok(new { success = true, message = "Настройки сохранены", settings = ToSettingsResponse(settings) });
        })
        .RequireAuthorization()
        .WithSummary("Изменение настроек модели")
        .WithDescription("Обновляет только переданные веса и пороги. Отрицательные веса или highRiskThreshold <= mediumRiskThreshold — 400. Настройки сохраняются в БД и переживают перезапуск.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static SettingsResponse ToSettingsResponse(ModelSettings settings) =>
        new(
            new WeightsResponse(
                settings.ProlongWaitWithoutFundsWeight,
                settings.AutoprolongDisabledExpiringWeight,
                settings.AutoprolongEnabledDeficitWeight,
                settings.SuspendedServiceWeight,
                settings.RecentFailedProlongationWeight),
            new ThresholdsResponse(
                settings.HighRiskThreshold,
                settings.MediumRiskThreshold,
                settings.ImminentExpiryDays),
            settings.UpdatedAt,
            settings.UpdatedBy);
}


// Пока так хз не придумал еще
public record WeightsResponse(
    double ProlongWaitWithoutFundsWeight,
    double AutoprolongDisabledExpiringWeight,
    double AutoprolongEnabledDeficitWeight,
    double SuspendedServiceWeight,
    double RecentFailedProlongationWeight);

public record ThresholdsResponse(double HighRiskThreshold, double MediumRiskThreshold, int ImminentExpiryDays);

public record SettingsResponse(WeightsResponse Weights, ThresholdsResponse Thresholds, DateTime UpdatedAt, string UpdatedBy);

public record PatchWeightsRequest(
    double? ProlongWaitWithoutFundsWeight,
    double? AutoprolongDisabledExpiringWeight,
    double? AutoprolongEnabledDeficitWeight,
    double? SuspendedServiceWeight,
    double? RecentFailedProlongationWeight);

public record PatchThresholdsRequest(double? HighRiskThreshold, double? MediumRiskThreshold, int? ImminentExpiryDays);

public record PatchSettingsRequest(PatchWeightsRequest? Weights, PatchThresholdsRequest? Thresholds);
