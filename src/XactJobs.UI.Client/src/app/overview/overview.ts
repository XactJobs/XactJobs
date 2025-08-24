import {Component, OnInit, signal} from '@angular/core';
import {HangfireService} from '../jobs.service';
import {DashboardStats} from '../model';
import {DatePipe} from '@angular/common';

@Component({
  selector: 'app-overview',
  imports: [
    DatePipe
  ],
  templateUrl: './overview.html'
})
export class Overview implements OnInit {

  constructor(private readonly hangfireService: HangfireService) {
  }

  stats = signal<DashboardStats | null>(null);
  realtimeData = signal<any>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  ngOnInit() {
    this.loadData();
    this.startRealtimeUpdates();
  }

  loadData() {
    this.loading.set(true);
    this.error.set(null);

    this.hangfireService.getDashboardStats().subscribe({
      next: (data) => {
        this.stats.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.message);
        this.loading.set(false);
      }
    });
  }

  private startRealtimeUpdates() {
    this.hangfireService.getRealtimeStats().subscribe({
      next: (data) => this.realtimeData.set(data),
      error: (err) => console.error('Realtime update failed:', err)
    });
  }

}
