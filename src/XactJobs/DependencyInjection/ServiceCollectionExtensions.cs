using Microsoft.EntityFrameworkCore;

using XactJobs;
using XactJobs.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXactJobs<TDbContext>(this IServiceCollection services, Action<XactJobsOptionsBuilder<TDbContext>>? configureOptions = null)
        where TDbContext: DbContext
    {
        var optionsBuilder = new XactJobsOptionsBuilder<TDbContext>();

        configureOptions?.Invoke(optionsBuilder);

        services.AddSingleton(optionsBuilder.Options);
        services.AddSingleton(CreateQuickPollChannels(optionsBuilder.Options));

        services.AddScoped<XactJobsQuickPoll<TDbContext>>();

        services.AddHostedService<XactJobsRunnerDispatcher<TDbContext>>();
        services.AddHostedService<XactJobsCronOptionsScheduler<TDbContext>>();

        return services;
    }

    private static XactJobsQuickPollChannels CreateQuickPollChannels<TDbContext>(XactJobsOptions<TDbContext> options) where TDbContext : DbContext
    {
        var qpc = new XactJobsQuickPollChannels();

        // 1k notifications kept so that another worker can quick poll (or the same worker in the next run),
        // if more than a batch of notifications are added to the channel.
        const int channelCapacity = 1_000; 

        qpc.Channels.Add(QueueNames.Default, new XactJobsQuickPollChannel(channelCapacity));

        foreach (var (queueName, queueOptions) in options.IsolatedQueues)
        {
            qpc.Channels.Add(queueName, new XactJobsQuickPollChannel(channelCapacity));
        }

        return qpc;
    }
}
