using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class ServiceEndpoints
{
    private static readonly string[] Statuses = ["active", "blocked", "deleted", "suspended", "prolong_wait"];
    private static readonly string[] PaymentTerms = ["hour", "week", "month", "quarter_year", "half_year", "year"];

    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/services").WithTags("Services");

        // POST /api/v1/services - создание услуги: валидация владельца и полей, затем точечный пересчёт риска владельца.
        group.MapPost("/", async (AccountRepository accounts, MetricsRecalculationService recalc, CreateServiceRequest request) =>
        {
            if (!await accounts.AccountExistsAsync(request.OwnerId))
                return Results.NotFound(new { message = $"Клиент {request.OwnerId} не найден" });
            if (!Statuses.Contains(request.Status))
                return Results.BadRequest(new { message = $"Недопустимый статус услуги: {request.Status}" });
            if (!PaymentTerms.Contains(request.PaymentTerm))
                return Results.BadRequest(new { message = $"Недопустимый срок оплаты: {request.PaymentTerm}" });
            if (request.Price < 0)
                return Results.BadRequest(new { message = "Цена услуги не может быть отрицательной" });
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Название услуги не может быть пустым" });

            var service = new Service
            {
                Name = request.Name,
                OwnerId = request.OwnerId,
                PaymentTerm = request.PaymentTerm,
                AutoProlong = request.AutoProlong,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresAt,
                Status = request.Status,
                Price = request.Price,
            };
            await accounts.AddServiceAsync(service);
            recalc.MarkDirty(service.OwnerId);

            return Results.Created($"/api/v1/services/{service.Id}", service);
        })
        .RequireAuthorization()
        .WithSummary("Создание услуги")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // PATCH /api/v1/services/{id} - частичное обновление только переданных полей услуги с пересчётом риска владельца.
        group.MapPatch("/{id:long}", async (long id, AccountRepository accounts, MetricsRecalculationService recalc, PatchServiceRequest request) =>
        {
            var service = await accounts.GetServiceByIdAsync(id);
            if (service is null)
                return Results.NotFound(new { message = $"Услуга {id} не найдена" });
            if (request.Status is { } status && !Statuses.Contains(status))
                return Results.BadRequest(new { message = $"Недопустимый статус услуги: {status}" });
            if (request.PaymentTerm is { } paymentTerm && !PaymentTerms.Contains(paymentTerm))
                return Results.BadRequest(new { message = $"Недопустимый срок оплаты: {paymentTerm}" });
            if (request.Price is { } price && price < 0)
                return Results.BadRequest(new { message = "Цена услуги не может быть отрицательной" });
            if (request.Name is { } name && string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { message = "Название услуги не может быть пустым" });

            if (request.Name is { } newName)
                service.Name = newName;
            if (request.PaymentTerm is { } newPaymentTerm)
                service.PaymentTerm = newPaymentTerm;
            if (request.AutoProlong is { } newAutoProlong)
                service.AutoProlong = newAutoProlong;
            if (request.ExpiresAt is { } newExpiresAt)
                service.ExpiresAt = newExpiresAt;
            if (request.Status is { } newStatus)
                service.Status = newStatus;
            if (request.Price is { } newPrice)
                service.Price = newPrice;

            await accounts.SaveAsync();
            recalc.MarkDirty(service.OwnerId);

            return Results.Ok(service);
        })
        .RequireAuthorization()
        .WithSummary("Частичное обновление услуги")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // DELETE /api/v1/services/{id} - софт-удаление услуги (status = "deleted"), транзакции сохраняются, риск владельца пересчитывается.
        group.MapDelete("/{id:long}", async (long id, AccountRepository accounts, MetricsRecalculationService recalc) =>
        {
            var service = await accounts.GetServiceByIdAsync(id);
            if (service is null || service.Status == "deleted")
                return Results.NotFound(new { message = $"Услуга {id} не найдена" });

            service.Status = "deleted";
            await accounts.SaveAsync();
            recalc.MarkDirty(service.OwnerId);

            return Results.Ok(new { success = true, message = "Услуга удалена" });
        })
        .RequireAuthorization()
        .WithSummary("Софт-удаление услуги")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}

public record CreateServiceRequest(long OwnerId, string Name, string PaymentTerm, bool AutoProlong, DateTime ExpiresAt, string Status, long Price);

public record PatchServiceRequest(string? Name, string? PaymentTerm, bool? AutoProlong, DateTime? ExpiresAt, string? Status, long? Price);
