using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

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

        qpc.Channels.Add(QueueNames.Default, Channel.CreateBounded<bool>(options.BatchSize));

        foreach (var (queueName, queueOptions) in options.IsolatedQueues)
        {
            qpc.Channels.Add(queueName, Channel.CreateBounded<bool>(queueOptions.BatchSize));
        }

        return qpc;
    }
}
