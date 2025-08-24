// Types
export interface Job {
  id: string;
  state: 'Enqueued' | 'Scheduled' | 'Processing' | 'Succeeded' | 'Failed' | 'Deleted' | 'Awaiting';
  job: string;
  enqueuedTime?: string;
  failedTime?: string;
  retryCount?: number;
  batchId?: string;
  currentCulture?: string;
  parameters?: { [key: string]: any };
  exception?: string;
}

export interface RecurringJob {
  id: string;
  cron: string;
  timeZone: string;
  job: string;
  nextExecution: string;
  lastExecution: string;
  created: string;
}

export interface DashboardStats {
  xactJobsVersion: string;
  uptimeDays: number;
  connections: number;
  memoryUsage: string;
  peakMemoryUsage: string;
  pubSubChannels: number;
}

export interface JobStats {
  enqueued: number;
  scheduled: number;
  processing: number;
  succeeded: number;
  failed: number;
  deleted: number;
  awaiting: number;
  awaitingBatch: number;
}

export interface QueueInfo {
  name: string;
  length: number;
  fetched: number;
  jobs: Job[];
}
