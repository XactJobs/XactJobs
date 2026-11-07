# XactJobs website

The landing page and the docs, built with [Astro](https://astro.build) and [Starlight](https://starlight.astro.build) into static files.

- `src/pages/index.astro` - the landing page, with its layout in `src/layouts/Landing.astro` and its parts in `src/components/`
- `src/content/docs/docs/` - the docs pages, served under `/docs/`. The sidebar is in `src/sidebar.mjs`
- `src/pages/llms.txt.ts`, `src/pages/llms-full.txt.ts` - `/llms.txt` (an index of the docs) and `/llms-full.txt` (all the docs as markdown), generated from the docs pages
- `src/styles/theme.css` - the colors (one accent and neutrals) and fonts, shared by the docs and the landing page
- The dashboard screenshots come from `../docs/images/`, and are resized and converted (AVIF, WebP) at build time

The docs follow the repository's README and code: when either changes, update the page too.

## Run it locally

It needs Node.js 22.23 or later (Astro 7).

```bash
cd website
npm ci
npm run dev        # http://localhost:4321/XactJobs/, reloads on changes
npm run build      # the static site, in dist/
npm run preview    # serves dist/ (Astro 7 runs it in the background: npx astro preview stop)
```

## Deployment

`.github/workflows/website.yml` builds the site and deploys it to GitHub Pages on every push to `main` that changes `website/`,
`docs/images/` or the workflow, and on demand (Actions > Website > Run workflow). The repository's Settings > Pages > Source must be
"GitHub Actions" once.

## The domain

The site's address is one value, `SITE_URL` in `site.config.mjs`. Until the domain is bought, it's the GitHub Pages project URL,
`https://xactjobs.github.io/XactJobs/`, and every page and asset is under `/XactJobs/`. When the domain is ready:

1. Set `SITE_URL` to it, e.g. `https://xactjobs.dev/`. The base path then becomes `/`.
2. Put the same host name in `public/CNAME` (it holds a placeholder).
3. Set it in the repository's Settings > Pages > Custom domain, and add the DNS records GitHub shows there. With a workflow deployment,
   GitHub takes the domain from this setting, not from the CNAME file.
