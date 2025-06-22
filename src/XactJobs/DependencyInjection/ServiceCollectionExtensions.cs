// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

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

        services.AddSingleton(optionsBuilder.Options);
        services.AddSingleton(CreateQuickPollChannels(optionsBuilder.Options));

        services.AddScoped<QuickPoll<TDbContext>>();
        services.AddScoped<XactJobMaintenance<TDbContext>>();

        services.AddHostedService<XactJobsRunnerDispatcher<TDbContext>>();
        services.AddHostedService<XactJobsCronOptionsScheduler<TDbContext>>();

        return services;
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
