import {Component, inject, OnInit, signal} from '@angular/core';
import {QueueInfo} from '../model';
import {HangfireService} from '../jobs.service';

@Component({
  selector: 'app-jobs',
  imports: [],
  templateUrl: './jobs.html'
})
export class Jobs implements OnInit {
  private hangfireService = inject(HangfireService);

  queues = signal<QueueInfo[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  ngOnInit() {
    this.loadQueues();
  }

  loadQueues() {
    this.loading.set(true);
    this.error.set(null);

    this.hangfireService.getQueues().subscribe({
      next: (data) => {
        this.queues.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.message);
        this.loading.set(false);
      }
    });
  }
}
