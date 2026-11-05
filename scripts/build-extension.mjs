// Copies the shared renderer into the extension and (optionally) zips it.
// Usage: node scripts/build-extension.mjs [--zip]
import { cpSync, rmSync, mkdirSync, existsSync } from 'node:fs';
import { execSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const ext = join(root, 'apps', 'extension');
const dest = join(ext, 'renderer');
rmSync(dest, { recursive: true, force: true });
cpSync(join(root, 'packages', 'renderer'), dest, { recursive: true, filter: (p) => !p.endsWith('.md') && !p.endsWith('demo.html') });
console.log('renderer copied → apps/extension/renderer');

if (process.argv.includes('--zip')) {
  const out = join(root, 'out');
  if (!existsSync(out)) mkdirSync(out);
  const zip = join(out, 'nightverse-extension.zip');
  rmSync(zip, { force: true });
  if (process.platform === 'win32') {
    execSync(`powershell -NoProfile -Command "Compress-Archive -Path '${ext}\\*' -DestinationPath '${zip}'"`, { stdio: 'inherit' });
  } else {
    execSync(`cd "${ext}" && zip -qr "${zip}" .`, { stdio: 'inherit' });
  }
  console.log('zipped →', zip);
}
