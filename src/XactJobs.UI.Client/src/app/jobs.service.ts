import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Job {
  id: number;
  scheduledAt: string;     // DateTime from backend, parse to Date in UI if needed
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
    return this.http.get<Job[]>(this.apiUrl);
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
