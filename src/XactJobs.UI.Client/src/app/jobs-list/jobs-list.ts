import {Component, OnInit, signal} from '@angular/core';
import { JobsService, Job } from '../jobs.service';
import {DatePipe} from '@angular/common';

@Component({
  selector: 'app-jobs-list',
  templateUrl: './jobs-list.html',
  imports: [
    DatePipe
  ]
})
export class JobsList implements OnInit {
  jobs = signal<Job[]>([]);
  loading = signal(false);
  error = signal<string | undefined>(undefined);

  constructor(private jobsService: JobsService) {}

  ngOnInit(): void {
    this.fetchJobs();
  }

  fetchJobs(): void {
    this.loading.set(true);
    this.error.set(undefined);

    this.jobsService.getJobs().subscribe({
      next: jobs => {
        this.jobs.set(jobs);
        this.loading.set(false);
      },
      error: err => {
        console.error(err);
        this.error.set('Failed to load jobs');
        this.loading.set(false);
      }
    });
  }

  deleteJob(job: Job): void {
    if (!confirm(`Delete job ${job.id}?`)) return;
    this.jobsService.deleteJob(job.id).subscribe({
      next: () => this.jobs.update(jobs => jobs.filter(j => j.id !== job.id)),
      error: err => console.error(err)
    });
  }
}
