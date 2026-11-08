// @ts-check
import { defineConfig, fontProviders } from 'astro/config';
import starlight from '@astrojs/starlight';
import { SITE_URL, GITHUB_URL } from './site.config.mjs';
import { sidebar } from './src/sidebar.mjs';

const siteUrl = new URL(SITE_URL);
const base = siteUrl.pathname.replace(/\/$/, '');

export default defineConfig({
  site: siteUrl.origin,
  // '/XactJobs' on the GitHub Pages project URL, '/' on a domain of its own
  base: base || '/',
  trailingSlash: 'ignore',
  output: 'static',
  redirects: {
    // the destination isn't prefixed with the base path on its own
    '/docs': `${base}/docs/getting-started/`,
  },
  // self-hosted from the npm packages: preloaded, with a fallback font adjusted to their metrics, so the text doesn't shift
  fonts: [
    {
      provider: fontProviders.local(),
      name: 'Inter',
      cssVariable: '--font-inter',
      fallbacks: ['ui-sans-serif', 'system-ui', 'sans-serif'],
      options: {
        variants: [{ src: ['@fontsource-variable/inter/files/inter-latin-wght-normal.woff2'], weight: '100 900', style: 'normal' }],
      },
    },
    {
      provider: fontProviders.local(),
      name: 'JetBrains Mono',
      cssVariable: '--font-mono',
      fallbacks: ['ui-monospace', 'monospace'],
      options: {
        variants: [{ src: ['@fontsource-variable/jetbrains-mono/files/jetbrains-mono-latin-wght-normal.woff2'], weight: '100 800', style: 'normal' }],
      },
    },
  ],
  vite: {
    server: {
      // the dashboard screenshots are imported from ../docs/images, outside the website folder
      fs: { allow: ['..'] },
    },
  },
  integrations: [
    starlight({
      title: 'XactJobs',
      description: 'Background jobs for EF Core that commit in the same transaction as your data. SQL Server, PostgreSQL, MySQL, Oracle and SQLite.',
      logo: {
        light: './src/assets/logo-light.svg',
        dark: './src/assets/logo-dark.svg',
        replacesTitle: true,
        alt: 'XactJobs',
      },
      favicon: '/favicon.svg',
      // src/pages/404.astro, in the landing page's layout
      disable404Route: true,
      social: [{ icon: 'github', label: 'GitHub', href: GITHUB_URL }],
      editLink: { baseUrl: `${GITHUB_URL}/edit/main/website/` },
      customCss: ['./src/styles/theme.css'],
      components: {
        // adds the fonts
        Head: './src/components/StarlightHead.astro',
      },
      expressiveCode: {
        themes: ['github-dark-default', 'github-light'],
        styleOverrides: { borderRadius: '0.6rem' },
        // a first-line comment like "// Microsoft.EntityFrameworkCore.SqlServer" is a comment, not a file name for the frame's title
        frames: { extractFileNameFromCode: false },
      },
      sidebar,
    }),
  ],
});
