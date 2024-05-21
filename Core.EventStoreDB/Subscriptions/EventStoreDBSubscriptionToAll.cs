using Core.EventStoreDB.Subscriptions.Batch;
using Core.EventStoreDB.Subscriptions.Checkpoints;
using Core.Extensions;
using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EventTypeFilter = EventStore.Client.EventTypeFilter;

namespace Core.EventStoreDB.Subscriptions;

using static ISubscriptionCheckpointRepository;

public class EventStoreDBSubscriptionToAllOptions
{
    public required string SubscriptionId { get; init; }

    public SubscriptionFilterOptions FilterOptions { get; set; } =
        new(EventTypeFilter.ExcludeSystemEvents());

    public Action<EventStoreClientOperationOptions>? ConfigureOperation { get; set; }
    public UserCredentials? Credentials { get; set; }
    public bool ResolveLinkTos { get; set; }
    public bool IgnoreDeserializationErrors { get; set; } = true;

    public int BatchSize { get; set; } = 1;
}

public class EventStoreDBSubscriptionToAll(
    EventStoreDBSubscriptionToAllOptions subscriptionOptions,
    EventStoreClient eventStoreClient,
    IServiceProvider serviceProvider,
    ILogger<EventStoreDBSubscriptionToAll> logger
)
{
    private string SubscriptionId => subscriptionOptions.SubscriptionId;

    public async Task SubscribeToAll(CancellationToken ct)
    {
        // see: https://github.com/dotnet/runtime/issues/36063
        await Task.Yield();

        logger.LogInformation("Subscription to all '{SubscriptionId}'", subscriptionOptions.SubscriptionId);

        try
        {
            var checkpoint = await LoadCheckpoint(ct).ConfigureAwait(false);

            var subscription = eventStoreClient.SubscribeToAll(
                checkpoint != Checkpoint.None ? FromAll.After(checkpoint) : FromAll.Start,
                subscriptionOptions.ResolveLinkTos,
                subscriptionOptions.FilterOptions,
                subscriptionOptions.Credentials,
                ct
            );

            logger.LogInformation("Subscription to all '{SubscriptionId}' started", SubscriptionId);

            await foreach (var events in subscription.BatchAsync(subscriptionOptions.BatchSize, ct)
                               .ConfigureAwait(false))
            {
                var result = await ProcessBatch(events, checkpoint, subscriptionOptions, ct).ConfigureAwait(false);

                switch (result)
                {
                    case StoreResult.Success success:
                        checkpoint = success.Checkpoint;
                        break;
                    case StoreResult.Ignored ignored:
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Checkpoint mismatch, ensure that you have a single subscription running!");
                }
            }
        }
        catch (RpcException rpcException) when (rpcException is { StatusCode: StatusCode.Cancelled } ||
                                                rpcException.InnerException is ObjectDisposedException)
        {
            logger.LogWarning(
                "Subscription to all '{SubscriptionId}' dropped by client",
                SubscriptionId
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning("Subscription was dropped: {Exception}", ex);

            // Sleep between reconnections to not flood the database or not kill the CPU with infinite loop
            // Randomness added to reduce the chance of multiple subscriptions trying to reconnect at the same time
            Thread.Sleep(1000 + new Random((int)DateTime.UtcNow.Ticks).Next(1000));

            await SubscribeToAll(ct).ConfigureAwait(false);
        }
    }

    private ValueTask<Checkpoint> LoadCheckpoint(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISubscriptionCheckpointRepository>().Load(SubscriptionId, ct);
    }

    private Task<StoreResult> ProcessBatch(
        ResolvedEvent[] events,
        Checkpoint lastCheckpoint,
        EventStoreDBSubscriptionToAllOptions subscriptionOptions,
        CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IEventsBatchCheckpointer>()
            .Process(events, lastCheckpoint, subscriptionOptions, ct);
    }
}
