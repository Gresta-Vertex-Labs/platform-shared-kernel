// Guards against a label being used before it exists. GitHub silently ignores an issue-form label
// that does not exist, and the label sync deletes any label missing from labels.yml.
// Run with: node --test .github/scripts/labels.test.cjs

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { LABELS } = require('./pr-template-check.cjs');

const githubDir = path.join(__dirname, '..');
const read = (...parts) => fs.readFileSync(path.join(githubDir, ...parts), 'utf8');

const defined = [...read('labels.yml').matchAll(/^- name: "([^"]+)"$/gm)].map((m) => m[1]);

test('labels.yml has no duplicate names', () => {
  const duplicates = defined.filter((name, i) => defined.indexOf(name) !== i);
  assert.deepEqual(duplicates, []);
});

test('every issue-form label is defined', () => {
  const dir = path.join(githubDir, 'ISSUE_TEMPLATE');
  for (const file of fs.readdirSync(dir).filter((f) => f.endsWith('.yml') && f !== 'config.yml')) {
    const line = read('ISSUE_TEMPLATE', file).match(/^labels:\s*\[(.*)\]\s*$/m);
    assert.ok(line, `${file} declares no labels`);
    for (const [, name] of line[1].matchAll(/"([^"]+)"/g)) {
      assert.ok(defined.includes(name), `${file}: label "${name}" is not in labels.yml`);
    }
  }
});

test('every path label in labeler.yml is defined', () => {
  for (const [, name] of read('labeler.yml').matchAll(/^"([^"]+)":$/gm)) {
    assert.ok(defined.includes(name), `labeler.yml: label "${name}" is not in labels.yml`);
  }
});

test('every label the PR template check applies is defined', () => {
  for (const name of Object.values(LABELS)) {
    assert.ok(defined.includes(name), `pr-template-check.cjs: label "${name}" is not in labels.yml`);
  }
});
