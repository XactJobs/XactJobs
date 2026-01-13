import {Component, inject, OnInit, signal} from '@angular/core';
import {HangfireService} from '../jobs.service';
import {Job} from '../model';

@Component({
  selector: 'app-jobs-failed',
  imports: [],
  templateUrl: './jobs-failed.html'
})
export class JobsFailed implements OnInit {

  private hangfireService = inject(HangfireService);

  failedJobs = signal<Job[]>([]);
  loading = signal(false);
  processing = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  selectedJobs = signal<Set<string>>(new Set());

  ngOnInit() {
    this.loadFailedJobs();
  }

  loadFailedJobs() {
    this.loading.set(true);
    this.error.set(null);

    this.hangfireService.getJobs().subscribe({
      next: (jobs) => {
        this.failedJobs.set(jobs.filter(job => job.state === 'Failed'));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.message);
        this.loading.set(false);
      }
    });
  }

  toggleSelection(jobId: string, event: any) {
    const selected = new Set(this.selectedJobs());
    if (event.target.checked) {
      selected.add(jobId);
    } else {
      selected.delete(jobId);
    }
    this.selectedJobs.set(selected);
  }

  requeueSelected() {
    const selected = Array.from(this.selectedJobs());
    if (selected.length === 0) return;

    this.processing.set(true);
    this.successMessage.set(null);
    this.error.set(null);

    // Simulate processing multiple jobs
    const requests = selected.map(jobId => this.hangfireService.requeueJob(jobId));

    // In a real app, you'd use forkJoin or similar
    Promise.all(requests.map(req => req.toPromise()))
      .then(() => {
        this.successMessage.set(`Successfully requeued ${selected.length} job(s)`);
        this.selectedJobs.set(new Set());
        this.processing.set(false);
        this.loadFailedJobs(); // Refresh the list
      })
      .catch((err) => {
        this.error.set('Failed to requeue jobs: ' + err.message);
        this.processing.set(false);
      });
  }

  deleteSelected() {
    const selected = Array.from(this.selectedJobs());
    if (selected.length === 0) return;

    this.processing.set(true);
    this.successMessage.set(null);
    this.error.set(null);

    const requests = selected.map(jobId => this.hangfireService.deleteJob(jobId));

    Promise.all(requests.map(req => req.toPromise()))
      .then(() => {
        this.successMessage.set(`Successfully deleted ${selected.length} job(s)`);
        this.selectedJobs.set(new Set());
        this.processing.set(false);
        this.loadFailedJobs();
      })
      .catch((err) => {
        this.error.set('Failed to delete jobs: ' + err.message);
        this.processing.set(false);
      });
  }

}
