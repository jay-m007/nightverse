// Tiny test for the LRC parser: node scripts/test-lrc.mjs
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const { parseLrc, lineIndexAt } = require('../packages/renderer/lrc.js');

const lines = parseLrc('[ti:x]\n[00:01.00][00:10.50]Hello world\n[00:05.123]<00:05.20>Word <00:06.00>timed\n[01:02]Last');
assert.deepEqual(lines.map((l) => l.t), [1000, 5123, 10500, 62000]);
assert.equal(lines[1].words[1].t, 6000, 'enhanced word timing');
assert.equal(lines[0].words.length, 2, 'words split');
assert.equal(lineIndexAt(lines, 0), -1);
assert.equal(lineIndexAt(lines, 5200), 1);
assert.equal(lineIndexAt(lines, 999999), 3);
assert.deepEqual(parseLrc(''), []);
console.log('lrc tests passed');
