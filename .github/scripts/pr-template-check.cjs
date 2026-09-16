// Validates a pull request description against .github/pull_request_template.md.
//
// Called from .github/workflows/pr-template.yml through actions/github-script. The evaluation itself
// (evaluate) is a pure function over the PR title and body so it can be tested locally with Node:
//
//   node .github/scripts/pr-template-check.test.cjs
//
// Security: the workflow runs on pull_request_target, so this file always comes from the default
// branch, never from the pull request. The PR body is untrusted input — it is only ever parsed as
// text here and never interpolated into a shell command or evaluated.

'use strict';

const MARKER = '<!-- pr-template-check -->';

const LABELS = {
  exempt: 'template: exempt',
  incomplete: 'template: incomplete',
  breaking: 'breaking change',
  security: 'security: sensitive',
};

// Keys are matched case-insensitively against "## " headings, so emoji and wording tweaks in the
// template do not break the check as long as the key word stays in the heading.
const SECTIONS = {
  summary: 'summary',
  issues: 'related issues',
  breaking: 'breaking changes',
  security: 'security impact',
  testing: 'how was this tested',
  checklist: 'checklist',
};

const MIN_LENGTH = { summary: 30, issues: 4, testing: 15, explanation: 20 };

const CONVENTIONAL_BREAKING_TITLE = /^[a-z]+(\([^)]*\))?!:\s/;

function normalize(body) {
  return (body || '').replace(/\r\n?/g, '\n').replace(/<!--[\s\S]*?-->/g, '');
}

function parseSections(body) {
  const text = normalize(body);
  const heading = /^##[ \t]+(.+?)[ \t]*$/gm;
  const found = [];
  let match;
  while ((match = heading.exec(text)) !== null) {
    found.push({ title: match[1].toLowerCase(), start: match.index, contentStart: heading.lastIndex });
  }
  return found.map((section, i) => ({
    title: section.title,
    content: text.slice(section.contentStart, i + 1 < found.length ? found[i + 1].start : text.length).trim(),
  }));
}

function checkboxes(content) {
  return [...content.matchAll(/^[ \t]*[-*][ \t]+\[([ xX])\][ \t]+(.+)$/gm)].map((m) => ({
    checked: m[1] !== ' ',
    label: m[2].trim(),
  }));
}

function prose(content) {
  return content.replace(/^[ \t]*[-*][ \t]+\[[ xX]\][ \t]+.+$/gm, '').trim();
}

// Returns { results: [{ name, ok, detail }], labels: { add: [], remove: [] } }.
function evaluate({ title, body }) {
  const sections = parseSections(body);
  const section = (key) => sections.find((s) => s.title.includes(SECTIONS[key]));
  const results = [];
  const labels = { add: [], remove: [] };
  const pass = (name) => results.push({ name, ok: true, detail: '' });
  const fail = (name, detail) => results.push({ name, ok: false, detail });

  const missing = Object.keys(SECTIONS).filter((key) => !section(key));
  if (missing.length === Object.keys(SECTIONS).length) {
    fail(
      'Template',
      'The description does not use the pull request template. Copy it from `.github/pull_request_template.md` and fill it in.',
    );
    return { results, labels };
  }
  if (missing.length > 0) {
    fail('Template', `Restore the removed section(s): ${missing.map((k) => `**${SECTIONS[k]}**`).join(', ')}.`);
  }

  const summary = section('summary');
  if (summary) {
    prose(summary.content).length >= MIN_LENGTH.summary
      ? pass('Summary')
      : fail('Summary', 'Describe what the PR changes and why (at least a sentence).');
  }

  const issues = section('issues');
  if (issues) {
    prose(issues.content).length >= MIN_LENGTH.issues
      ? pass('Related issues')
      : fail('Related issues', 'Link an issue (`Closes #123`) or write `None` with a short reason.');
  }

  const breaking = section('breaking');
  if (breaking) {
    const boxes = checkboxes(breaking.content);
    const ticked = boxes.filter((b) => b.checked);
    const isBreaking = ticked.some((b) => /\bbreaks\b/i.test(b.label));
    if (ticked.length !== 1) {
      fail('Breaking changes', `Tick exactly one box (currently ${ticked.length}).`);
    } else if (isBreaking && prose(breaking.content).length < MIN_LENGTH.explanation) {
      fail('Breaking changes', 'Describe what breaks, who is affected, and how to migrate.');
    } else if (isBreaking && !CONVENTIONAL_BREAKING_TITLE.test(title || '')) {
      fail('Breaking changes', 'Mark the PR title as breaking with `!`, for example `feat(caching)!: …`.');
    } else {
      pass('Breaking changes');
    }
    (isBreaking && ticked.length === 1 ? labels.add : labels.remove).push(LABELS.breaking);
  }

  const security = section('security');
  if (security) {
    const ticked = checkboxes(security.content).filter((b) => b.checked);
    const isSensitive = ticked.some((b) => /sensitive/i.test(b.label));
    if (ticked.length !== 1) {
      fail('Security impact', `Tick exactly one box (currently ${ticked.length}).`);
    } else if (isSensitive && prose(security.content).length < MIN_LENGTH.explanation) {
      fail('Security impact', 'Explain what security-sensitive code changed and why it is safe.');
    } else {
      pass('Security impact');
    }
    (isSensitive && ticked.length === 1 ? labels.add : labels.remove).push(LABELS.security);
  }

  const testing = section('testing');
  if (testing) {
    prose(testing.content).length >= MIN_LENGTH.testing
      ? pass('Testing')
      : fail('Testing', 'Describe how the change was tested — tests added, commands run, manual checks.');
  }

  const checklist = section('checklist');
  if (checklist) {
    const boxes = checkboxes(checklist.content);
    const unticked = boxes.filter((b) => !b.checked);
    if (boxes.length === 0) {
      fail('Checklist', 'The checklist items were removed. Restore them from the template.');
    } else if (unticked.length > 0) {
      fail('Checklist', `${unticked.length} unticked: ${unticked.map((b) => `“${b.label}”`).join('; ')}`);
    } else {
      pass('Checklist');
    }
  }

  return { results, labels };
}

function escapeCell(text) {
  return text.replace(/\|/g, '\\|').replace(/\n/g, ' ');
}

function renderComment(results, { isDraft }) {
  const failures = results.filter((r) => !r.ok);
  if (failures.length === 0) {
    return [
      MARKER,
      '### 📋 PR template check — ✅ all good',
      '',
      'Every section is filled in. Thanks for the thorough description!',
    ].join('\n');
  }

  const lines = [
    MARKER,
    `### 📋 PR template check — ❌ ${failures.length} ${failures.length === 1 ? 'item needs' : 'items need'} attention`,
    '',
  ];
  if (isDraft) {
    lines.push('_This is a draft, so there is no rush — but this must pass before the PR can be merged._', '');
  }
  lines.push(
    '| | Section | What to do |',
    '|:-:|---|---|',
    ...results.map((r) => `| ${r.ok ? '✅' : '❌'} | **${r.name}** | ${escapeCell(r.detail)} |`),
    '',
    '<details><summary>How do I fix this?</summary>',
    '',
    '1. Open the **···** menu on the PR description and choose **Edit**.',
    '2. Fill in the sections marked ❌. Tick a box by changing `[ ]` to `[x]`.',
    '3. Save. The check re-runs automatically and this comment updates itself.',
    '',
    'The template lives in `.github/pull_request_template.md`.',
    '',
    '</details>',
  );
  return lines.join('\n');
}

async function findStickyComment(github, owner, repo, issue_number) {
  const comments = await github.paginate(github.rest.issues.listComments, { owner, repo, issue_number, per_page: 100 });
  return comments.find((c) => c.user && c.user.type === 'Bot' && (c.body || '').startsWith(MARKER));
}

async function run({ github, context, core }) {
  if (context.eventName === 'merge_group') {
    core.info('Merge queue run: the description was already validated on the pull request.');
    return;
  }

  const pr = context.payload.pull_request;
  const { owner, repo } = context.repo;
  const issue_number = pr.number;
  const currentLabels = new Set((pr.labels || []).map((l) => l.name));

  const addLabels = async (names) => {
    const missing = names.filter((n) => !currentLabels.has(n));
    if (missing.length === 0) return;
    try {
      await github.rest.issues.addLabels({ owner, repo, issue_number, labels: missing });
    } catch (error) {
      core.warning(`Could not add labels ${missing.join(', ')}: ${error.message}`);
    }
  };
  const removeLabels = async (names) => {
    for (const name of names.filter((n) => currentLabels.has(n))) {
      try {
        await github.rest.issues.removeLabel({ owner, repo, issue_number, name });
      } catch (error) {
        core.warning(`Could not remove label ${name}: ${error.message}`);
      }
    }
  };

  if (pr.user && pr.user.type === 'Bot') {
    core.info(`Skipped: pull request opened by bot account ${pr.user.login}.`);
    return;
  }
  if (currentLabels.has(LABELS.exempt)) {
    core.notice(`Skipped: the pull request is labelled "${LABELS.exempt}".`);
    await removeLabels([LABELS.incomplete]);
    return;
  }

  const { results, labels } = evaluate({ title: pr.title, body: pr.body });
  const failures = results.filter((r) => !r.ok);

  await addLabels(labels.add);
  await removeLabels(labels.remove);

  const existing = await findStickyComment(github, owner, repo, issue_number);
  const body = renderComment(results, { isDraft: pr.draft });
  if (existing) {
    if (existing.body !== body) {
      await github.rest.issues.updateComment({ owner, repo, comment_id: existing.id, body });
    }
  } else if (failures.length > 0) {
    await github.rest.issues.createComment({ owner, repo, issue_number, body });
  }

  const summary = core.summary.addHeading('PR template check', 2).addTable([
    [{ data: '', header: true }, { data: 'Section', header: true }, { data: 'Result', header: true }],
    ...results.map((r) => [r.ok ? '✅' : '❌', r.name, r.ok ? 'OK' : r.detail]),
  ]);
  await summary.write();

  if (failures.length > 0) {
    await addLabels([LABELS.incomplete]);
    core.setFailed(
      `The pull request description is incomplete: ${failures.map((f) => f.name).join(', ')}. See the bot comment on the PR.`,
    );
  } else {
    await removeLabels([LABELS.incomplete]);
    core.info('The pull request description is complete.');
  }
}

module.exports = { run, evaluate, renderComment, MARKER, LABELS };
