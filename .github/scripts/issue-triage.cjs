// Adds "area: …" labels to an issue from the domain chosen in its issue form.
//
// Issue forms can only apply fixed labels, so the "Affected domain(s)" dropdown answer is read back
// out of the rendered issue body ("### Affected domain" followed by the selected option text).
// Labels are only ever added, never removed, so manual triage is not undone when an issue is edited.
//
// Test locally with: node --test .github/scripts/issue-triage.test.cjs

'use strict';

const AREAS = {
  '00': 'area: 00-governance',
  '01': 'area: 01-core',
  '02': 'area: 02-caching',
  '03': 'area: 03-domain',
  '04': 'area: 04-contracts',
  '05': 'area: 05-application',
  '06': 'area: 06-persistence',
  '07': 'area: 07-messaging',
  '08': 'area: 08-storage',
  '09': 'area: 09-search',
  '10': 'area: 10-intelligence',
  '11': 'area: 11-communication',
  '12': 'area: 12-security',
  '13': 'area: 13-servicedefaults',
  '14': 'area: 14-presentation',
  '15': 'area: 15-integration',
  '16': 'area: 16-testing',
  '17': 'area: 17-workflows',
  '18': 'area: 18-idempotency',
  '19': 'area: 19-scheduling',
  '20': 'area: 20-reporting',
};

const BUILD_AREA = 'area: build-ci';

function areaLabels(body) {
  const text = (body || '').replace(/\r\n?/g, '\n');
  const heading = /^###[ \t]+Affected domains?[ \t]*$/m.exec(text);
  if (!heading) return [];

  const rest = text.slice(heading.index + heading[0].length);
  const next = rest.search(/^###[ \t]/m);
  const answer = next === -1 ? rest : rest.slice(0, next);

  const labels = new Set();
  for (const match of answer.matchAll(/(?:^|[\s,])(\d{2})\.[A-Za-z]/g)) {
    if (AREAS[match[1]]) labels.add(AREAS[match[1]]);
  }
  if (/Build, CI or packaging/.test(answer)) labels.add(BUILD_AREA);
  return [...labels];
}

async function run({ github, context, core }) {
  const issue = context.payload.issue;
  if (!issue || issue.pull_request) return;

  const existing = new Set((issue.labels || []).map((l) => l.name));
  const labels = areaLabels(issue.body).filter((l) => !existing.has(l));
  if (labels.length === 0) {
    core.info('No new area labels to add.');
    return;
  }

  await github.rest.issues.addLabels({ ...context.repo, issue_number: issue.number, labels });
  core.info(`Added labels: ${labels.join(', ')}`);
}

module.exports = { run, areaLabels, AREAS };
