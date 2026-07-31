// Builds the InkWell chapter editor bundle.
//
// The authored editor sources deliberately live next to the page that loads them, under
// ../Resources/Raw/wwwroot (editor.js, live-preview.js), so that the whole editor surface is in
// one place. Only the npm dependency graph and this bundler live here. esbuild is pointed at
// this workspace's node_modules via `nodePaths` because the entry point sits outside it.
//
// Output: ../Resources/Raw/wwwroot/editor.bundle.js — the single script index.html loads.
// Run with `npm run build` (or `npm run watch` during editor development).

import { build, context } from 'esbuild';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const wwwroot = resolve(here, '..', 'Resources', 'Raw', 'wwwroot');

/** @type {import('esbuild').BuildOptions} */
const options = {
  entryPoints: [resolve(wwwroot, 'editor.js')],
  outfile: resolve(wwwroot, 'editor.bundle.js'),
  bundle: true,
  format: 'iife',
  target: ['es2020'],
  // The shipped bundle carries no source map — inlining one tripled the asset the app installs on
  // every device. `npm run watch` turns it back on for editor development.
  sourcemap: process.argv.includes('--watch') ? 'inline' : false,
  minify: true,
  nodePaths: [resolve(here, 'node_modules')],
  logLevel: 'info',
};

if (process.argv.includes('--watch')) {
  const ctx = await context(options);
  await ctx.watch();
  console.log('watching editor sources…');
} else {
  await build(options);
}
