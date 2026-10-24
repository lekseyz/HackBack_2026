using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using HackBack.Domain;
using HackBack.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace HackBack.Api;

public sealed class MetricsRecalculationService(
    IHubContext<AnalyticsHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<MetricsRecalculationService> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly Channel<RecalcRequest> _channel = Channel.CreateBounded<RecalcRequest>(
        new BoundedChannelOptions(1000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    public void RequestFullRecalc(string trigger)
    {
        _channel.Writer.TryWrite(RecalcRequest.Full(trigger));
    }

    public void MarkDirty(long accountId)
    {
        _channel.Writer.TryWrite(RecalcRequest.Partial(accountId));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<RecalcRequest> batch;
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(stoppingToken))
                    break;

                batch = [];
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var request))
                    batch.Add(request);

                if (batch.Count < BatchSize)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeoutCts.CancelAfter(FlushInterval);
                    try
                    {
                        while (batch.Count < BatchSize)
                        {
                            if (!await _channel.Reader.WaitToReadAsync(timeoutCts.Token))
                                return;

                            while (batch.Count < BatchSize && _channel.Reader.TryRead(out var request))
                                batch.Add(request);
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await FlushAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Metrics recalculation flush failed");
            }
        }
    }

    private async Task FlushAsync(List<RecalcRequest> batch, CancellationToken ct)
    {
        var fullTriggers = batch
            .Select(r => r.Trigger)
            .OfType<string>()
            .Distinct()
            .ToList();

        var accountIds = batch
            .Select(r => r.AccountId)
            .OfType<long>()
            .Distinct()
            .ToList();

        if (fullTriggers.Count > 0)
        {
            using var scope = scopeFactory.CreateScope();
            var modelSettings = scope.ServiceProvider.GetRequiredService<ModelSettingsRepository>();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountRepository>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await modelSettings.GetAsync();
            var (accountList, serviceList, transactionList) = await accounts.GetAllForScoringAsync();

            var servicesByOwner = serviceList.ToLookup(s => s.OwnerId);
            var ownerByServiceId = serviceList.ToDictionary(s => s.Id, s => s.OwnerId);
            var transactionsByOwner = new Dictionary<long, List<Transaction>>();
            foreach (var transaction in transactionList)
            {
                if (!ownerByServiceId.TryGetValue(transaction.ServiceId, out var ownerId))
                    continue;
                if (!transactionsByOwner.TryGetValue(ownerId, out var ownerTransactions))
                {
                    ownerTransactions = [];
                    transactionsByOwner[ownerId] = ownerTransactions;
                }
                ownerTransactions.Add(transaction);
            }

            var stopwatch = Stopwatch.StartNew();
            var changed = 0;
            var now = DateTime.UtcNow;
            foreach (var account in accountList)
            {
                IReadOnlyList<Service> ownerServices = servicesByOwner[account.Id].ToList();
                IReadOnlyList<Transaction> ownerTransactions =
                    transactionsByOwner.TryGetValue(account.Id, out var list) ? list : [];
                var assessment = RiskScorer.Assess(new RiskInput(account, ownerServices, ownerTransactions, settings, now));
                var reasons = assessment.Score is null ? null : JsonSerializer.Serialize(assessment.ReasonCodes);
                if (account.RiskScore != assessment.Score
                    || account.MonthlyRevenue != assessment.MonthlyRevenue
                    || account.RevenueAtRisk != assessment.RevenueAtRisk
                    || account.BalanceDeficit != assessment.BalanceDeficit
                    || account.PrimaryReasons != reasons)
                {
                    account.RiskScore = assessment.Score;
                    account.MonthlyRevenue = assessment.MonthlyRevenue;
                    account.RevenueAtRisk = assessment.RevenueAtRisk;
                    account.BalanceDeficit = assessment.BalanceDeficit;
                    account.PrimaryReasons = reasons;
                    account.ScoredAt = now;
                    changed++;
                }
            }
            stopwatch.Stop();

            if (changed > 0)
                await db.SaveChangesAsync();

            var (high, _, _, _) = await accounts.GetRiskBandCountsAsync(settings.HighRiskThreshold, settings.MediumRiskThreshold);

            var revenueAtRisk = accountList.Sum(a => a.RevenueAtRisk);

            logger.LogInformation(
                "Перечситаны метрикидля: {Accounts}, скоры: {Changed} за: {Duration} ms",
                accountList.Count, changed, stopwatch.ElapsedMilliseconds);

            foreach (var trigger in fullTriggers)
            {
                await hub.Clients.All.SendAsync(
                    "ModelRecalculated",
                    new ModelRecalculatedEvent(trigger, DateTime.UtcNow, new RecalcSummary(revenueAtRisk, high)),
                    ct);
                logger.LogInformation("Full metrics recalc pushed (trigger: {Trigger}, highRisk: {HighRisk}, revenueAtRisk: {RevenueAtRisk})", trigger, high, revenueAtRisk);
            }

            return;
        }

        if (accountIds.Count == 0)
            return;

        var items = new List<object>();
        using (var scope = scopeFactory.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountRepository>();
            foreach (var id in accountIds)
            {
                var account = await accounts.GetByIdAsync(id);
                if (account is null)
                    continue;
                items.Add(CustomerItems.ToItem(account));
            }
        }

        if (items.Count > 0)
        {
            await hub.Clients.All.SendAsync("CustomerRiskUpdated", new CustomerRiskUpdatedEvent(items), ct);
            logger.LogInformation("Partial metrics update pushed ({Count} customers)", items.Count);
        }
    }
}

public sealed record RecalcRequest(string? Trigger, long? AccountId)
{
    public static RecalcRequest Full(string trigger) => new(trigger, null);

    public static RecalcRequest Partial(long accountId) => new(null, accountId);
}
