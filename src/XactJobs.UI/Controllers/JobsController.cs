using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XactJobs.UI.Controllers
{
    [ApiController]
    [Route(Constants.RoutePrefix + "/api/[controller]")]
    public class JobsController : ControllerBase 
    {
        private readonly DbContext _db;

        public JobsController(XactJobsDbContextAccessor acc)
        {
            _db = acc.DbContext;
        }

        // List all jobs (active, scheduled, etc.)
        [HttpGet]
        public async Task<IActionResult> GetJobs()
        {
            var jobs = await _db.Set<XactJob>()
                .Take(1000)
                .OrderBy(x => x.ScheduledAt)
                .ToListAsync();

            return Ok(jobs);
        }

        // Get job details by id
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetJob(long id)
        {
            var job = await _db.Set<XactJob>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
            if (job == null) return NotFound();
            return Ok(job);
        }

        // Delete/cancel a job
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> DeleteJob(long id, CancellationToken cancellationToken)
        {
            if (!await _db.JobDeleteAsync(id, cancellationToken))
            {
                return NotFound();
            }

            await _db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        // List recurring jobs
        [HttpGet("periodic")]
        public async Task<IActionResult> GetPeriodicJobs()
        {
            var jobs = await _db.Set<XactJobPeriodic>().AsNoTracking().ToListAsync();
            return Ok(jobs);
        }

        // Delete a recurring job
        [HttpDelete("periodic/{id}")]
        public async Task<IActionResult> DeletePeriodicJob(string id, CancellationToken cancellationToken)
        {
            if (!await _db.JobDeletePeriodicAsync(id, cancellationToken))
            {
                return NotFound();
            }

            await _db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        // TODO: Add endpoints for enqueuing jobs, rescheduling, etc.
    }
}