// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;

using XactJobs;
using XactJobs.DependencyInjection;
using XactJobs.Internal;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXactJobs<TDbContext>(this IServiceCollection services, Action<XactJobsOptionsBuilder<TDbContext>>? configureOptions = null)
        where TDbContext: DbContext
    {
        var optionsBuilder = new XactJobsOptionsBuilder<TDbContext>();

        configureOptions?.Invoke(optionsBuilder);

        // once the queues and the other periodic jobs are known
        XactPeriodicJobScanner.AddTo(optionsBuilder.Options);

        // the scheduler saves them at start, and an invalid one there would stop the ones after it. Other problems of a periodic job
        // are reported by the scheduler, as before
        CheckRetryAttributesOfPeriodicJobs(optionsBuilder.Options);

        services.AddSingleton(optionsBuilder.Options);
        services.AddSingleton(CreateQuickPollChannels(optionsBuilder.Options));

        services.AddScoped<QuickPoll<TDbContext>>();
        services.AddScoped<XactJobMaintenance<TDbContext>>();

        services.AddHostedService<XactJobsRunnerDispatcher<TDbContext>>();
        services.AddHostedService<XactJobsCronOptionsScheduler<TDbContext>>();

        return services;
    }

    /// <exception cref="InvalidOperationException">With every periodic job whose method has an invalid [XactRetry].</exception>
    private static void CheckRetryAttributesOfPeriodicJobs<TDbContext>(XactJobsOptions<TDbContext> options) where TDbContext : DbContext
    {
        var errors = options.PeriodicJobs
            .Concat(options.IsolatedQueues.Values.SelectMany(q => q.PeriodicJobs))
            .Select(p => (p.Key, Error: XactJobSerializer.CheckRetryAttribute(p.Value.JobExpression)))
            .Where(p => p.Error != null)
            .Select(p => $"periodic job '{p.Key}': {p.Error}")
            .ToList();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid [XactRetry] attributes:\n- {string.Join("\n- ", errors)}");
        }
    }

    private static QuickPollChannels CreateQuickPollChannels<TDbContext>(XactJobsOptions<TDbContext> options) where TDbContext : DbContext
    {
        // 1k notifications kept so that another worker can quick poll (or the same worker in the next run),
        // if more than a batch of notifications are added to the channel.
        const int channelCapacity = 1_000; 

        var channels = new Dictionary<string, QuickPollChannel>
        {
            { QueueNames.Default, new QuickPollChannel(channelCapacity) }
        };

        foreach (var (queueName, _) in options.IsolatedQueues)
        {
            channels.Add(queueName, new QuickPollChannel(channelCapacity));
        }

        return new QuickPollChannels(channels);
    }
}
