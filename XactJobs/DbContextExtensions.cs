using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

using XactJobs.Annotations;

namespace XactJobs
{
    public static class DbContextExtensions
    {
        public static XactJob JobEnqueue(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJobPeriodic JobSchedulePeriodic(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action> jobExpression)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression);
        }

        public static XactJobPeriodic JobSchedulePeriodic<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action<T>> jobExpression)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression);
        }

        public static XactJobPeriodic JobSchedulePeriodic(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<Task>> jobExpression)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression);
        }

        public static XactJobPeriodic JobSchedulePeriodic<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<T, Task>> jobExpression)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression);
        }

        private static XactJob AddJob(DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
        {
            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var id = dialect.NewJobId();

            var job = XactJobSerializer.FromExpression(lambdaExp, id, scheduledAt, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }

        internal static XactJobPeriodic AddJobPeriodic(this DbContext dbContext, LambdaExpression lambdaExp, string id, string cronExp)
        {
            var job = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, cronExp);

            dbContext.Set<XactJobPeriodic>().Add(job);

            return job;
        }
    }
}
