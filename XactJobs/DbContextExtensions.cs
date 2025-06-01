using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using XactJobs.Annotations;

namespace XactJobs
{
    public static class DbContextExtensions
    {
        public static XactJob Enqueue(this DbContext? dbContext, [InstantHandle] Expression<Action> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        public static XactJob Enqueue<T>(this DbContext? dbContext, [InstantHandle] Expression<Action<T>> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        public static XactJob Enqueue(this DbContext? dbContext, [InstantHandle] Expression<Func<Task>> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        public static XactJob Enqueue<T>(this DbContext? dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression)
        {
            return AddJob(dbContext, jobExpression);
        }

        private static XactJob AddJob(DbContext? dbContext, LambdaExpression lambdaExp)
        {
            var job = XactJob.FromExpression(lambdaExp, null);

            dbContext?.Set<XactJob>().Add(job);

            return job;
        }
    }
}
