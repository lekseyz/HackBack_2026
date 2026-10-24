using System.Threading.Channels;
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
            foreach (var trigger in fullTriggers)
            {
                await hub.Clients.All.SendAsync(
                    "ModelRecalculated",
                    new ModelRecalculatedEvent(trigger, DateTime.UtcNow, new RecalcSummary(0, 0)),
                    ct);
                logger.LogInformation("Full metrics recalc pushed (trigger: {Trigger})", trigger);
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
                if (account is not null)
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
