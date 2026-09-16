// Local tests for issue-triage.cjs. Run with: node --test .github/scripts/issue-triage.test.cjs

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { areaLabels, AREAS } = require('./issue-triage.cjs');

// Rendered issue bodies look like: "### <label>\n\n<answer>\n\n### <next label>\n\n…"
const rendered = (answers) => answers.map(([label, value]) => `### ${label}\n\n${value}`).join('\n\n');

test('a single-domain answer maps to its area label', () => {
  const body = rendered([
    ['Before you start', '- [X] I searched existing issues'],
    ['Affected domain', '02.Caching — FusionCache, Redis'],
    ['Package', 'SharedKernel.Caching.FusionCache'],
  ]);
  assert.deepEqual(areaLabels(body), ['area: 02-caching']);
});

test('a multi-select RFC answer maps to every selected area', () => {
  const body = rendered([
    ['Affected domains', '01.Core — primitives, Result, configuration, cryptography, validation, 12.Security — OIDC, API keys, mTLS, TOTP, Build, CI or packaging'],
    ['Detailed design', 'Uses 99.Unknown and mentions 06.Persistence in prose'],
  ]);
  assert.deepEqual(areaLabels(body).sort(), ['area: 01-core', 'area: 12-security', 'area: build-ci'].sort());
});

test('"Not sure", no answer, and non-form issues add nothing', () => {
  assert.deepEqual(areaLabels(rendered([['Affected domain', 'Not sure']])), []);
  assert.deepEqual(areaLabels(rendered([['Affected domain', '_No response_']])), []);
  assert.deepEqual(areaLabels('Something is broken in 02.Caching'), []);
  assert.deepEqual(areaLabels(null), []);
});

test('Windows line endings are accepted', () => {
  const body = rendered([['Affected domain', '17.Workflows — Temporal'], ['Package', 'x']]).replace(/\n/g, '\r\n');
  assert.deepEqual(areaLabels(body), ['area: 17-workflows']);
});

test('every domain option in the issue forms maps to an area label', () => {
  const dir = path.join(__dirname, '..', 'ISSUE_TEMPLATE');
  for (const file of fs.readdirSync(dir).filter((f) => /^\d{2}-.*\.yml$/.test(f))) {
    const yaml = fs.readFileSync(path.join(dir, file), 'utf8');
    for (const [, number] of yaml.matchAll(/^\s+- (\d{2})\.[A-Za-z]/gm)) {
      assert.ok(AREAS[number], `${file}: domain ${number} has no area label`);
    }
  }
});

test('every area label exists in .github/labels.yml', () => {
  const labels = fs.readFileSync(path.join(__dirname, '..', 'labels.yml'), 'utf8');
  for (const name of [...Object.values(AREAS), 'area: build-ci']) {
    assert.ok(labels.includes(`name: "${name}"`), `missing from labels.yml: ${name}`);
  }
});
