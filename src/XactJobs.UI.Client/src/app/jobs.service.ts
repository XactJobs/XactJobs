import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {catchError, delay, map, Observable, of, throwError} from 'rxjs';
import {DashboardStats, Job, JobStats, QueueInfo, RecurringJob} from './model';

/*
export interface Job {
  id: number;
  scheduledAt: string; // DateTime from backend, parse to Date in UI if needed
  typeName: string;
  methodName: string;
  methodArgs: string;
  queue: string;
  periodicJobId?: string | null;
  cronExpression?: string | null;
  periodicJobVersion?: number | null;
  errorCount: number;
}

export interface PeriodicJob {
  id: string;
  createdAt: string;
  updatedAt: string;
  cronExpression: string;
  typeName: string;
  methodName: string;
  methodArgs: string;
  queue: string;
  isActive: boolean;
  version: number;
}

@Injectable({
  providedIn: 'root'
})
export class JobsService {
  private apiUrl = 'api/jobs';

  constructor(private http: HttpClient) {}

  getJobs(): Observable<Job[]> {
    //return this.http.get<Job[]>(this.apiUrl);
    return new BehaviorSubject(jobs).asObservable();
  }

  getJob(id: number): Observable<Job> {
    return this.http.get<Job>(`${this.apiUrl}/${id}`);
  }

  deleteJob(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  getPeriodicJobs(): Observable<PeriodicJob[]> {
    return this.http.get<PeriodicJob[]>(`${this.apiUrl}/periodic`);
  }

  deletePeriodicJob(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/periodic/${id}`);
  }
}
*/


@Injectable({
  providedIn: 'root'
})
export class HangfireService {
  private mockJobs: Job[] = [
    {
      id: '13f446b8...',
      state: 'Enqueued',
      job: 'IEmailService.SendCampaignEmailAsync',
      enqueuedTime: 'a minute ago'
    },
    {
      id: '13b75b83...',
      state: 'Enqueued',
      job: 'IEmailService.SendCampaignEmailAsync',
      enqueuedTime: 'a minute ago'
    },
    {
      id: '13be5477...',
      state: 'Enqueued',
      job: 'IEmailService.SendCampaignEmailAsync',
      enqueuedTime: 'a minute ago'
    },
    {
      id: '0accd8be...',
      state: 'Failed',
      job: 'IEmailService.SendCampaignEmailAsync',
      failedTime: 'a few seconds ago',
      exception: 'System.Net.Mail.SmtpException: Syntax error, command unrecognized.'
    },
    {
      id: '0a30d70f...',
      state: 'Failed',
      job: 'IEmailService.SendCampaignEmailAsync',
      failedTime: 'a few seconds ago'
    }
  ];

  private mockRecurringJobs: RecurringJob[] = [
    {
      id: 'clean-temp',
      cron: '0 * * * *',
      timeZone: 'UTC',
      job: 'IMaintenanceService.CleanTempDirectory',
      nextExecution: 'in 16 minutes',
      lastExecution: '42 minutes ago',
      created: '5 days ago'
    },
    {
      id: 'db-incr-backup',
      cron: '30 0 * * 2-7',
      timeZone: 'Europe/London',
      job: 'IDatabaseService.PerformIncrementalBackup',
      nextExecution: 'in 16 hours',
      lastExecution: 'a week ago',
      created: '5 days ago'
    },
    {
      id: 'db-clean-users',
      cron: '0 1 * * *',
      timeZone: 'Europe/London',
      job: 'IUsersService.RemoveDeletedUsers',
      nextExecution: 'in 16 hours',
      lastExecution: '8 hours ago',
      created: '5 days ago'
    },
    {
      id: 'db-defrag-indexes',
      cron: '0 0 * * 1',
      timeZone: 'Europe/London',
      job: 'IDatabaseService.DefragmentIndexesAsync',
      nextExecution: 'in 3 days',
      lastExecution: 'a week ago',
      created: '5 days ago'
    },
    {
      id: 'db-full-backup',
      cron: '30 0 * * 1',
      timeZone: 'Europe/London',
      job: 'IDatabaseService.PerformFullBackupAsync',
      nextExecution: 'in 3 days',
      lastExecution: 'a week ago',
      created: '5 days ago'
    },
    {
      id: 'send-newsletter',
      cron: '0 10 L * *',
      timeZone: 'UTC',
      job: 'IEmailService.SendNewsletterAsync',
      nextExecution: 'in 13 days',
      lastExecution: 'a month ago',
      created: '5 days ago'
    }
  ];

  private mockDashboardStats: DashboardStats = {
    xactJobsVersion: '6.2.6',
    uptimeDays: 0,
    connections: 3,
    memoryUsage: '371.31M',
    peakMemoryUsage: '371.32M',
    pubSubChannels: 0
  };

  // Simulate HTTP requests with Observables
  getJobs(): Observable<Job[]> {
    // Simulate network delay and potential error
    return of(this.mockJobs).pipe(
      delay(Math.random() * 1000 + 500), // Random delay between 500-1500ms
      catchError(error => throwError(() => new Error('Failed to fetch jobs')))
    );
  }

  getJobById(id: string): Observable<Job> {
    const job = this.mockJobs.find(j => j.id === id);
    if (!job) {
      return throwError(() => new Error('Job not found'));
    }

    return of(job).pipe(
      delay(300)
    );
  }

  getRecurringJobs(): Observable<RecurringJob[]> {
    return of(this.mockRecurringJobs).pipe(
      delay(Math.random() * 800 + 200)
    );
  }

  getDashboardStats(): Observable<DashboardStats> {
    return of(this.mockDashboardStats).pipe(
      delay(100)
    );
  }

  getQueues(): Observable<QueueInfo[]> {
    const queues: QueueInfo[] = [
      {
        name: 'default',
        length: 804,
        fetched: 20,
        jobs: this.mockJobs.filter(j => j.state === 'Enqueued').slice(0, 5)
      },
      {
        name: 'critical',
        length: 0,
        fetched: 0,
        jobs: []
      }
    ];

    return of(queues).pipe(
      delay(400)
    );
  }

  getJobStats(): Observable<JobStats> {
    return this.getJobs().pipe(
      map(jobs => ({
        enqueued: jobs.filter(j => j.state === 'Enqueued').length,
        scheduled: jobs.filter(j => j.state === 'Scheduled').length,
        processing: jobs.filter(j => j.state === 'Processing').length,
        succeeded: jobs.filter(j => j.state === 'Succeeded').length,
        failed: jobs.filter(j => j.state === 'Failed').length,
        deleted: jobs.filter(j => j.state === 'Deleted').length,
        awaiting: jobs.filter(j => j.state === 'Awaiting').length,
        awaitingBatch: 0
      }))
    );
  }

  // Action methods that simulate HTTP POST/PUT/DELETE
  requeueJob(jobId: string): Observable<void> {
    console.log(`Requeuing job ${jobId}`);
    return of(void 0).pipe(
      delay(500),
      catchError(error => throwError(() => new Error('Failed to requeue job')))
    );
  }

  deleteJob(jobId: string): Observable<void> {
    console.log(`Deleting job ${jobId}`);
    return of(void 0).pipe(
      delay(300)
    );
  }

  triggerRecurringJob(jobId: string): Observable<void> {
    console.log(`Triggering recurring job ${jobId}`);
    return of(void 0).pipe(
      delay(400)
    );
  }

  // Simulate real-time updates
  getRealtimeStats(): Observable<any> {
    return new Observable(observer => {
      const interval = setInterval(() => {
        observer.next({
          timestamp: new Date(),
          succeeded: Math.floor(Math.random() * 100) + 3000,
          failed: Math.floor(Math.random() * 5),
          deleted: Math.floor(Math.random() * 3)
        });
      }, 2000);

      return () => clearInterval(interval);
    });
  }
}
