// The docs' sidebar, also the order of the pages in /llms.txt and /llms-full.txt.
export const sidebar = [
  {
    label: 'Start here',
    items: [
      { label: 'Getting started', slug: 'docs/getting-started' },
      { label: 'Jobs', slug: 'docs/jobs' },
      { label: 'A job is a unit of work', slug: 'docs/unit-of-work' },
    ],
  },
  {
    label: 'Guides',
    items: [
      { label: 'Scheduling', slug: 'docs/scheduling' },
      { label: 'Batches and continuations', slug: 'docs/batches-and-continuations' },
      { label: 'Queues and polling', slug: 'docs/queues-and-polling' },
      { label: 'Failures, retries and stopping', slug: 'docs/failures-and-retries' },
      { label: 'Idempotent external calls', slug: 'docs/idempotent-external-calls' },
      { label: 'Dashboard and API', slug: 'docs/dashboard' },
      { label: 'Using XactJobs with AI agents', slug: 'docs/ai-agents' },
    ],
  },
  {
    label: 'Reference',
    items: [
      { label: 'Configuration', slug: 'docs/configuration' },
      { label: 'Databases', slug: 'docs/databases' },
      { label: 'Upgrading', slug: 'docs/upgrading' },
      { label: 'Throughput and benchmarks', slug: 'docs/throughput' },
    ],
  },
];

export const docSlugs = sidebar.flatMap((group) => group.items.map((item) => item.slug));
