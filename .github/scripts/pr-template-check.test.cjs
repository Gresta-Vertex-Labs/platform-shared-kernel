// Local tests for pr-template-check.cjs. Run with: node .github/scripts/pr-template-check.test.cjs
// Uses only Node's built-in test runner, so no package.json or npm install is needed.

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { evaluate, renderComment, MARKER, LABELS } = require('./pr-template-check.cjs');

const template = fs.readFileSync(path.join(__dirname, '..', 'pull_request_template.md'), 'utf8');

const tick = (body, label) => {
  const next = body.replace(`- [ ] ${label}`, `- [x] ${label}`);
  assert.notEqual(next, body, `checkbox not found in template: ${label}`);
  return next;
};
const fillAfter = (body, heading, text) => body.replace(new RegExp(`(## [^\\n]*${heading}[^\\n]*\\n)`), `$1\n${text}\n`);

function completeBody() {
  let body = template;
  body = fillAfter(body, 'Summary', 'Adds a fencing token to renewable distributed locks so stale holders are rejected.');
  body = fillAfter(body, 'Related issues', 'Closes #42');
  body = fillAfter(body, 'How was this tested', 'Added unit tests; dotnet test Platform.SharedKernel.Unit.slnf passes.');
  body = tick(body, 'No breaking changes');
  body = tick(body, 'None');
  for (const m of template.matchAll(/^- \[ \] (.+)$/gm)) {
    if (/breaks public API|No breaking changes|^None$|security-sensitive/.test(m[1])) continue;
    body = tick(body, m[1]);
  }
  return body;
}

const failed = (result) => result.results.filter((r) => !r.ok).map((r) => r.name);

test('the untouched template fails every required section', () => {
  const result = evaluate({ title: 'feat: x', body: template });
  assert.deepEqual(failed(result), ['Summary', 'Related issues', 'Breaking changes', 'Security impact', 'Testing', 'Checklist']);
});

test('an empty description is reported as not using the template', () => {
  assert.deepEqual(failed(evaluate({ title: 'feat: x', body: '' })), ['Template']);
  assert.deepEqual(failed(evaluate({ title: 'feat: x', body: null })), ['Template']);
});

test('a complete description passes and applies no labels', () => {
  const result = evaluate({ title: 'feat(caching): add fencing token', body: completeBody() });
  assert.deepEqual(failed(result), []);
  assert.deepEqual(result.labels.add, []);
  assert.deepEqual(result.labels.remove.sort(), [LABELS.breaking, LABELS.security].sort());
});

test('Windows line endings are accepted', () => {
  const result = evaluate({ title: 'feat: x', body: completeBody().replace(/\n/g, '\r\n') });
  assert.deepEqual(failed(result), []);
});

test('text inside HTML comments does not count as content', () => {
  const body = completeBody().replace(
    /## 📝 Summary\n\n[^\n]+\n/,
    '## 📝 Summary\n\n<!-- a very long comment that should never count as a real summary of the change -->\n',
  );
  assert.deepEqual(failed(evaluate({ title: 'feat: x', body })), ['Summary']);
});

test('an unticked checklist item fails and is named', () => {
  const body = completeBody().replace('- [x] Tests cover the change', '- [ ] Tests cover the change');
  const result = evaluate({ title: 'feat: x', body });
  assert.deepEqual(failed(result), ['Checklist']);
  assert.match(result.results.find((r) => r.name === 'Checklist').detail, /Tests cover the change/);
});

test('ticking both breaking-change boxes fails', () => {
  const body = tick(completeBody(), 'This PR breaks public API or behavior — migration notes below');
  assert.deepEqual(failed(evaluate({ title: 'feat!: x', body })), ['Breaking changes']);
});

test('a breaking change needs migration notes and a "!" title', () => {
  let body = completeBody()
    .replace('- [x] No breaking changes', '- [ ] No breaking changes')
    .replace('- [ ] This PR breaks', '- [x] This PR breaks');
  assert.deepEqual(failed(evaluate({ title: 'feat(domain)!: x', body })), ['Breaking changes']);

  body = body.replace(
    '- [x] This PR breaks public API or behavior — migration notes below\n',
    '- [x] This PR breaks public API or behavior — migration notes below\n\nIMoney.Divide now returns ValidationResult; callers must handle the failure case.\n',
  );
  assert.deepEqual(failed(evaluate({ title: 'feat(domain): x', body })), ['Breaking changes']);

  const ok = evaluate({ title: 'feat(domain)!: x', body });
  assert.deepEqual(failed(ok), []);
  assert.ok(ok.labels.add.includes(LABELS.breaking));
});

test('security-sensitive changes need an explanation and are labelled', () => {
  let body = completeBody()
    .replace('- [x] None', '- [ ] None')
    .replace('- [ ] Touches security-sensitive code', '- [x] Touches security-sensitive code');
  assert.deepEqual(failed(evaluate({ title: 'fix: x', body })), ['Security impact']);

  body = body.replace(
    '- [x] Touches security-sensitive code — explained below\n',
    '- [x] Touches security-sensitive code — explained below\n\nDPoP validation now rejects proofs missing the ath claim.\n',
  );
  const ok = evaluate({ title: 'fix: x', body });
  assert.deepEqual(failed(ok), []);
  assert.ok(ok.labels.add.includes(LABELS.security));
});

test('removing a section is reported', () => {
  const body = completeBody().replace(/## 🧪 How was this tested\?[\s\S]*?(?=## ✅)/, '');
  const result = evaluate({ title: 'feat: x', body });
  assert.deepEqual(failed(result), ['Template']);
  assert.match(result.results[0].detail, /how was this tested/);
});

test('upper-case X ticks and * bullets are accepted', () => {
  const body = completeBody().replace(/- \[x\]/g, '* [X]');
  assert.deepEqual(failed(evaluate({ title: 'feat: x', body })), []);
});

test('the comment starts with the marker and has a separated table', () => {
  const { results } = evaluate({ title: 'feat: x', body: template });
  const comment = renderComment(results, { isDraft: true });
  assert.ok(comment.startsWith(MARKER));
  assert.match(comment, /attention\n\n_This is a draft/);
  assert.match(comment, /\n\n\| \| Section \| What to do \|/);
  assert.ok(renderComment([{ name: 'Summary', ok: true, detail: '' }], { isDraft: false }).includes('all good'));
});
