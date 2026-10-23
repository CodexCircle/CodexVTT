// Incorpora JS e CSS della build di preview in un unico preview.html, apribile con un doppio clic.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const dir = process.argv[2] ?? 'dist-preview';
let html = readFileSync(join(dir, 'index.html'), 'utf8');
html = html.replace(
  /<link rel="stylesheet"[^>]*href="\.\/([^"]+)"[^>]*>/g,
  (_, file) => `<style>${readFileSync(join(dir, file), 'utf8')}</style>`,
);
html = html.replace(/<script type="module"[^>]*src="\.\/([^"]+)"[^>]*><\/script>/g, (_, file) => {
  const code = readFileSync(join(dir, file), 'utf8').replace(/<\/script/gi, '<\\/script');
  return `<script type="module">${code}</script>`;
});
writeFileSync(join(dir, 'preview.html'), html);
console.log(`preview: ${join(dir, 'preview.html')} (${Math.round(html.length / 1024)} KB)`);
