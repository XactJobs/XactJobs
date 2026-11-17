// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace XactJobs.Internal
{
    /// <summary>The app's job filters and failure handlers, shared by the runners of a dispatcher.</summary>
    internal sealed class XactJobHooks(IReadOnlyList<Type> filters, IReadOnlyList<Type> failureHandlers)
    {
        public IReadOnlyList<Type> Filters { get; } = filters;

        public IReadOnlyList<Type> FailureHandlers { get; } = failureHandlers;

        /// <summary>
        /// The filters' context for the job, or null without filters: then <see cref="RunJobAsync"/> runs the job alone, with nothing
        /// allocated for them.
        /// </summary>
        public XactJobExecutingContext? CreateContext(XactJobCall call, XactJob job, IServiceProvider services, DbContext dbContext,
                                                      XactJobContext jobContext, string? timeZoneId, CancellationToken stoppingToken)
        {
            return Filters.Count == 0
                ? null
                : new XactJobExecutingContext(job, call.Method, call.Arguments, services, dbContext, jobContext, timeZoneId, stoppingToken);
        }

        /// <summary>
        /// Runs the job, inside the filters if there are any (<paramref name="context"/> isn't null): each one resolved from the job's
        /// scope, the first one outermost. A filter may skip the run (<see cref="XactJobExecutingContext.SkipReason"/>).
        /// </summary>
        public Task RunJobAsync(XactJobCall call, IServiceProvider services, XactJobExecutingContext? context)
        {
            return context == null
                ? call.InvokeAsync(services)
                : new FilterChain(Filters, call, services, context).RunAsync();
        }

        /// <summary>
        /// The filters of one job's run: <see cref="NextAsync"/> is the <c>next</c> each filter gets, which runs the following filter, and
        /// after the last one the job. One allocation for the whole chain.
        /// </summary>
        private sealed class FilterChain
        {
            private readonly IReadOnlyList<Type> _filters;
            private readonly XactJobCall _call;
            private readonly IServiceProvider _services;
            private readonly XactJobExecutingContext _context;
            private readonly Func<Task> _next;

            private int _nextFilter;
            private ExceptionDispatchInfo? _jobFailure;

            public FilterChain(IReadOnlyList<Type> filters, XactJobCall call, IServiceProvider services, XactJobExecutingContext context)
            {
                _filters = filters;
                _call = call;
                _services = services;
                _context = context;
                _next = NextAsync;
            }

            public async Task RunAsync()
            {
                try
                {
                    await NextAsync().ConfigureAwait(false);
                }
                catch (XactJobFilterMisuseException) when (_jobFailure != null)
                {
                    // e.g. Skip() after the job threw: the attempt fails with the job's exception, not with the misuse
                    _jobFailure.Throw();
                }
                finally
                {
                    _context.IsChainFinished = true;
                }

                // a filter that swallowed the job's failure: it's still a failure, because the job's changes (saved or not, in its own
                // transaction too) can't be told apart from a completed job's, and the worker would save and commit them
                _jobFailure?.Throw();

                // a misuse that a filter caught fails the attempt too
                if (_context.Misuse != null) ExceptionDispatchInfo.Throw(_context.Misuse);

                // the job didn't run: a new state would be lost without a word
                if (_context.SkipReason != null && _context.JobContext.IsPeriodicStateSet)
                {
                    throw _context.SetMisuse("A skipped run can't set the periodic state: set it only in a run that isn't skipped.");
                }
            }

            private Task NextAsync()
            {
                // before the inner filters too: they don't run for a skipped job either
                if (_context.SkipReason != null)
                {
                    throw _context.SetMisuse("A job filter called next() after skipping the run: the skipped job doesn't run.");
                }

                if (_nextFilter < _filters.Count)
                {
                    var filter = (IXactJobFilter)_services.GetRequiredService(_filters[_nextFilter++]);

                    return filter.InvokeAsync(_context, _next);
                }

                if (_context.IsJobInvoked)
                {
                    throw _context.SetMisuse("A job filter called next() more than once: a job runs once per attempt.");
                }

                _context.IsJobInvoked = true;

                return InvokeJobAsync();
            }

            private async Task InvokeJobAsync()
            {
                try
                {
                    await _call.InvokeAsync(_services).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _jobFailure = ExceptionDispatchInfo.Capture(ex);
                    throw;
                }
            }
        }
    }
}
