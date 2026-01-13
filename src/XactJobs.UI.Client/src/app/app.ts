import {Component, OnInit, signal} from '@angular/core';
import {RouterLink, RouterLinkActive, RouterOutlet} from '@angular/router';
import {HangfireService} from './jobs.service';
import {JobStats} from './model';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {

  protected readonly jobStats = signal<JobStats>({
    enqueued: 0,
    scheduled: 0,
    processing: 0,
    succeeded: 0,
    failed: 0,
    deleted: 0,
    awaiting: 0,
    awaitingBatch: 0
  });

  constructor(private readonly api: HangfireService) {
  }

  ngOnInit(): void {
    this.api.getJobStats().subscribe({
      next: (data) => {
        this.jobStats.set(data);
      },
      error: (err) => {
        console.error(err);
      }
    });
  }


}
