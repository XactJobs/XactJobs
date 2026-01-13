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
using System.Linq.Expressions;
using XactJobs.Annotations;

namespace XactJobs
{
    public class XactJobsOptionsBuilderBase<TDbContext, TOptions, TBuilder>
        where TDbContext : DbContext
        where TOptions: XactJobsOptionsBase<TDbContext>, new()
        where TBuilder: XactJobsOptionsBuilderBase<TDbContext, TOptions, TBuilder>
    {
        public TOptions Options { get; private set; } = new();

        public TBuilder WithBatchSize(int batchSize)
        {
            Options.BatchSize = batchSize;
            return (this as TBuilder)!;
        }

        public TBuilder WithWorkerCount(int workerCount)
        {
            Options.WorkerCount = workerCount;
            return (this as TBuilder)!;
        }

        public TBuilder WithPollingInterval(int intervalInSeconds)
        {
            Options.PollingIntervalInSeconds = intervalInSeconds;
            return (this as TBuilder)!;
        }

        public TBuilder WithLeaseDuration(int durationInSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(durationInSeconds, 1);

            Options.LeaseDurationInSeconds = durationInSeconds;
            return (this as TBuilder)!;
        }

        public TBuilder WithRetryStrategy(IRetryStrategy retryStrategy)
        {
            Options.RetryStrategy = retryStrategy;
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob([InstantHandle] Expression<Action> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob<T>( [InstantHandle] Expression<Action<T>> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob([InstantHandle] Expression<Func<Task>> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }
    }

    public class XactJobsQueueOptionsBuilder<TDbContext>: XactJobsOptionsBuilderBase<TDbContext, XactJobsOptionsBase<TDbContext>, XactJobsQueueOptionsBuilder<TDbContext>>
        where TDbContext : DbContext
    {
    }
    
    public class XactJobsOptionsBuilder<TDbContext>: XactJobsOptionsBuilderBase<TDbContext, XactJobsOptions<TDbContext>, XactJobsOptionsBuilder<TDbContext>>
        where TDbContext : DbContext
    {
        public XactJobsOptionsBuilder<TDbContext> WithHistoryRetentionPeriodInDays(int days)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

            Options.HistoryRetentionPeriodInDays = days;
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithPriorityQueue(Action<XactJobsQueueOptionsBuilder<TDbContext>>? configureAction = null)
        {
            WithIsolatedQueue(QueueNames.Priority, options =>
            {
                options
                    .WithPollingInterval(4)
                    .WithWorkerCount(2);

                configureAction?.Invoke(options);
            });
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithLongRunningQueue(Action<XactJobsQueueOptionsBuilder<TDbContext>>? configureAction = null)
        {
            WithIsolatedQueue(QueueNames.LongRunning, options =>
            {
                options
                    .WithPollingInterval(10)
                    .WithWorkerCount(2);

                configureAction?.Invoke(options);
            });
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithIsolatedQueue(string queueName, Action<XactJobsQueueOptionsBuilder<TDbContext>>? configureAction = null)
        {
            var builder = new XactJobsQueueOptionsBuilder<TDbContext>();

            configureAction?.Invoke(builder);

            Options.IsolatedQueues[queueName] = builder.Options;

            return this;
        }
    }
}
