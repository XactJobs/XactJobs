import { getCollection, type CollectionEntry } from 'astro:content';
import { SITE_URL, GITHUB_URL } from '../../site.config.mjs';
import { docSlugs } from '../sidebar.mjs';

type Doc = CollectionEntry<'docs'>;

const RAW_URL = 'https://raw.githubusercontent.com/XactJobs/XactJobs/main/';

export const pageUrl = (slug: string) => new URL(`${slug}/`, SITE_URL).href;

/** The docs pages, in the sidebar's order. */
export async function getDocs(): Promise<Doc[]> {
  const docs = await getCollection('docs');
  const bySlug = new Map(docs.map((doc) => [doc.id, doc]));

  return docSlugs.map((slug) => {
    const doc = bySlug.get(slug);
    if (!doc) throw new Error(`The sidebar has ${slug}, but there's no such page`);
    return doc;
  });
}

/**
 * A page's source as plain markdown: without the MDX imports and components (a tab becomes a bold label), with absolute links,
 * and the screenshots linked from the repository.
 */
export function toPlainMarkdown(doc: Doc): string {
  const base = new URL(`${doc.id}/`, SITE_URL);

  return (doc.body ?? '')
    .replace(/^import .*$/gm, '')
    .replace(/^[ \t]*<\/?(Tabs|Steps)\b[^>\n]*>[ \t]*$/gm, '')
    .replace(/^[ \t]*<TabItem label="([^"]+)">[ \t]*$/gm, '**$1**')
    .replace(/^[ \t]*<\/TabItem>[ \t]*$/gm, '')
    .replace(/\]\((?:\.\.\/)+docs\/images\/([^)]+)\)/g, `](${RAW_URL}docs/images/$1)`)
    .replace(/^[ \t]*<Screenshot name="([^"]+)" alt="([^"]+)" \/>[ \t]*$/gm, `![$2](${RAW_URL}docs/images/$1.png)`)
    .replace(/\]\((\.\.\/[^)]*)\)/g, (_, href: string) => `](${new URL(href, base).href})`)
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

export const intro = `# XactJobs

> Background jobs for EF Core that commit in the same transaction as your data, on SQL Server, PostgreSQL, MySQL, Oracle and SQLite (.NET 8, 9 and 10). A job enqueued with JobEnqueue is a row saved by your own SaveChanges: it runs only if your transaction commits. The worker saves a job's database changes in the transaction that completes it, so they happen exactly once.

XactJobs is pre-1.0: the API can still change between minor versions. Source: ${GITHUB_URL}`;

export const textResponse = (text: string) =>
  new Response(text + '\n', { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
