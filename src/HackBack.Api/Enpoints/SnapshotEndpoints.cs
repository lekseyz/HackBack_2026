using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class SnapshotEndpoints
{
    public static IEndpointRouteBuilder MapSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/source").WithTags("Source");

        // PUT /api/v1/source/snapshot - полная замена датасета клиентов, услуг и транзакций с валидацией ссылок.
        group.MapPut("/snapshot", async (SnapshotRequest request, AccountRepository accounts, MetricsRecalculationService recalc) =>
        {
            var accountList = request.Accounts ?? [];
            var serviceList = request.Services ?? [];
            var transactionList = request.Transactions ?? [];

            var accountIds = accountList.Select(a => a.Id).ToHashSet();
            var serviceIds = serviceList.Select(s => s.Id).ToHashSet();

            var brokenService = serviceList.FirstOrDefault(s => !accountIds.Contains(s.OwnerId));
            if (brokenService is not null)
                return Results.BadRequest(new { message = $"Услуга {brokenService.Id} ссылается на несуществующего клиента ownerId={brokenService.OwnerId}" });

            var brokenTransaction = transactionList.FirstOrDefault(t => !serviceIds.Contains(t.ServiceId));
            if (brokenTransaction is not null)
                return Results.BadRequest(new { message = $"Транзакция {brokenTransaction.Id} ссылается на несуществующую услугу serviceId={brokenTransaction.ServiceId}" });

            foreach (var account in accountList)
                account.RiskScore = null;

            await accounts.ReplaceAllAsync(accountList, serviceList, transactionList);

            recalc.RequestFullRecalc("SNAPSHOT_IMPORTED");

            return Results.Ok(new
            {
                success = true,
                importedAccounts = accountList.Count,
                importedServices = serviceList.Count,
                importedTransactions = transactionList.Count,
                recalculatedAt = DateTime.UtcNow,
            });
        })
        .RequireAuthorization()
        .WithSummary("Загрузка / обновление снимка данных")
        .WithDescription("Полностью заменяет текущий датасет клиентов, услуг и транзакций. Битые ссылки ownerId/serviceId - 400.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}

public record SnapshotRequest(List<Account> Accounts, List<Service> Services, List<Transaction> Transactions);
