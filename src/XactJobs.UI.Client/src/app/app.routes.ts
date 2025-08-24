import { Routes } from '@angular/router';
import {Overview} from './overview/overview';
import {JobDetail} from './job-detail/job-detail';
import {Jobs} from './jobs/jobs';
import {JobsFailed} from './jobs-failed/jobs-failed';
import {JobsPeriodic} from './jobs-periodic/jobs-periodic';

export const routes: Routes = [
  { path: '', redirectTo: '/overview', pathMatch: 'full' },
  { path: 'overview', component: Overview },
  { path: 'jobs', component: Jobs },
  { path: 'job-detail/:id', component: JobDetail },
  { path: 'failed-jobs', component: JobsFailed },
  { path: 'recurring-jobs', component: JobsPeriodic },
  { path: 'scheduled', component: Overview },
  { path: 'processing', component: Overview },
  { path: 'succeeded', component: Overview },
  { path: 'deleted', component: Overview },
  { path: 'awaiting', component: Overview },
  { path: 'awaiting-batch', component: Overview },
  { path: 'retries', component: Overview },
  { path: 'batches', component: Overview },
  { path: 'servers', component: Overview }
];
