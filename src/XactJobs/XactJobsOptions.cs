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

namespace XactJobs
{
    public class XactJobsOptionsBase<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// The most jobs to claim at a time. Default is 100.
        /// Jobs are only claimed for free slots, so a worker never has more jobs in flight than the lower of this
        /// and <see cref="MaxDegreeOfParallelism"/>.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Number of paraller workers running. Default 2.
        /// The workers will be started with initial delay, to distribute workers execution in time.
        /// By default, 2 workers will be started with 4 second polling interval. 
        /// The initial delay between workers is (4 seconds / 2 workers) = 2 seconds.
        /// </summary>
        public int WorkerCount { get; set; } = 2;

        /// <summary>
        /// Max degree of paralell jobs running per worker. Default -1 (means ProcessorCount)
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = -1;

        /// <summary>
        /// Default 120 (jobs runner will prolong the lease every 3/4 of this time)
        /// </summary>
        public int LeaseDurationInSeconds { get; set; } = 120;

        /// <summary>
        /// Database polling interval in seconds, per worker (Default 6 seconds).
        /// The workers will be started with initial delay, to distribute workers execution in time.
        /// By default, 2 workers will be started with 6 second polling interval. 
        /// The initial delay between workers is (6 seconds / 2 workers) = 3 seconds.
        /// It means these workers will take turns every 3 seconds.
        /// </summary>
        public int PollingIntervalInSeconds { get; set; } = 6;

        /// <summary>
        /// On worker stop, how long running jobs get to finish before they are cancelled. (Default 10 seconds)
        /// Make sure this fits in the shutdown timeout of the host, along with <see cref="ClearLeaseTimeoutInSeconds"/>
        /// and 5 seconds for the jobs to react to the cancellation.
        /// </summary>
        public int ShutdownTimeoutInSeconds { get; set; } = 10;

        /// <summary>
        /// Time the workers will wait for to clear pending leases, on worker stop. (Default 10 seconds)
        /// </summary>
        public int ClearLeaseTimeoutInSeconds { get; set; } = 10;

        /// <summary>
        /// Retry strategy - if not set, the default retry strategy is used. <see cref="DefaultRetryStrategy"/>
        /// </summary>
        public IRetryStrategy RetryStrategy { get; set; } = new DefaultRetryStrategy();

        /// <summary>
        /// Gets or sets the collection of periodic jobs, each defined by a unique identifier,  a job expression, and a
        /// cron schedule.
        /// </summary>
        /// <remarks>The cron schedule string must follow the standard cron format. Ensure that the  <see
        /// cref="LambdaExpression"/> provided for each job is valid and executable.</remarks>
        public Dictionary<string, (LambdaExpression JobExpression, string CronExpression)> PeriodicJobs { get; set; } = [];
    }

    public class XactJobsOptions<TDbContext>: XactJobsOptionsBase<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// Gets or sets the number of days for which historical data is retained.
        /// </summary>
        /// <remarks>Adjust this value to control how long historical data is kept. Setting a lower value
        /// may reduce storage usage,  while a higher value retains data for longer periods.</remarks>
        public int HistoryRetentionPeriodInDays { get; set; } = 30;

        /// <summary>
        /// How often each node looks for active periodic jobs without a pending run, and schedules their next run.
        /// Default 300 seconds.
        /// </summary>
        public int PeriodicJobReconciliationIntervalInSeconds { get; set; } = 300;

        /// <summary>
        /// List of isolated queues for which workers will be started
        /// </summary>
        public Dictionary<string, XactJobsOptionsBase<TDbContext>> IsolatedQueues { get; set; } = [];
    }
}
