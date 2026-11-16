// Copies the Keyframe branding assets from the repository's assets/ folder into public/
// so the favicon and logos are never duplicated by hand (plan D9). When the dashboard is
// built on its own (for example inside the Docker build context), ../assets is absent and
// the copies already present in public/ are used as-is.
import { copyFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(root, '..', 'assets');
const target = join(root, 'public');

const files = [
  ['glyph.ico', 'favicon.ico'],
  ['logo-dark.png', 'logo-dark.png'],
  ['logo-light.png', 'logo-light.png']
];

if (!existsSync(source)) {
  console.log('[sync-assets] ../assets not found; using the copies in public/');
} else {
  mkdirSync(target, { recursive: true });
  for (const [from, to] of files) {
    const src = join(source, from);
    if (existsSync(src)) copyFileSync(src, join(target, to));
  }
  console.log('[sync-assets] copied branding assets into public/');
}
