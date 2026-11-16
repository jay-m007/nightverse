// Copies the shared renderer into the extension and (optionally) zips it.
// Usage: node scripts/build-extension.mjs [--zip]
import { cpSync, rmSync, mkdirSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
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

// Firefox build: MV3 with an event-page background (Firefox has no extension service workers)
// plus the gecko id and data-collection declaration AMO requires.
if (process.argv.includes('--firefox')) {
  const out = join(root, 'out');
  if (!existsSync(out)) mkdirSync(out);
  const stage = join(out, 'firefox-stage');
  rmSync(stage, { recursive: true, force: true });
  cpSync(ext, stage, { recursive: true });
  const m = JSON.parse(readFileSync(join(stage, 'manifest.json'), 'utf8'));
  m.background = { scripts: ['background.js'] };
  m.browser_specific_settings = {
    gecko: {
      id: 'nightverse@jay-m007.github.io',
      strict_min_version: '128.0',
      data_collection_permissions: { required: ['websiteContent'] }
    }
  };
  writeFileSync(join(stage, 'manifest.json'), JSON.stringify(m, null, 2));
  const zip = join(out, 'nightverse-firefox.zip');
  rmSync(zip, { force: true });
  if (process.platform === 'win32') {
    execSync(`powershell -NoProfile -Command "Compress-Archive -Path '${stage}\\*' -DestinationPath '${zip}'"`, { stdio: 'inherit' });
  } else {
    execSync(`cd "${stage}" && zip -qr "${zip}" .`, { stdio: 'inherit' });
  }
  console.log('zipped →', zip);
}
