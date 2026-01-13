import {Component, inject, OnInit, signal} from '@angular/core';
import {HangfireService} from '../jobs.service';
import {RecurringJob} from '../model';

@Component({
  selector: 'app-jobs-periodic',
  imports: [],
  templateUrl: './jobs-periodic.html'
})
export class JobsPeriodic implements OnInit {

  private hangfireService = inject(HangfireService);

  recurringJobs = signal<RecurringJob[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  ngOnInit() {
    this.loadJobs();
  }

  loadJobs() {
    this.loading.set(true);
    this.error.set(null);

    this.hangfireService.getRecurringJobs().subscribe({
      next: (data) => {
        this.recurringJobs.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.message);
        this.loading.set(false);
      }
    });
  }

}
