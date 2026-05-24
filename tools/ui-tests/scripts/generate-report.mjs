import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

function readArg(name, defaultValue = null) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : defaultValue;
}

const inputPath = readArg('input', 'reports/playwright-report.json');
const fallbackPath = readArg('fallback', 'reports/sample-playwright-report.json');
const outputPath = readArg('output', 'reports/latest-ui-test-report.md');
const command = readArg('command', 'npm run ui-test:all');

const sourcePath = existsSync(inputPath) ? inputPath : fallbackPath;
if (!existsSync(sourcePath)) {
  throw new Error(`No Playwright report found at ${inputPath} and no fallback found at ${fallbackPath}.`);
}

const report = JSON.parse(readFileSync(sourcePath, 'utf8'));
const startedUtc = new Date().toISOString();

function flattenSuites(suites, parentTitle = '') {
  return suites.flatMap((suite) => {
    const title = [parentTitle, suite.title].filter(Boolean).join(' / ');
    const specs = (suite.specs ?? []).map((spec) => ({ suite: title, spec }));
    return specs.concat(flattenSuites(suite.suites ?? [], title));
  });
}

function attachmentPath(result, contentTypeOrName) {
  const attachment = (result.attachments ?? []).find((item) =>
    item.name?.toLowerCase().includes(contentTypeOrName) ||
    item.contentType?.toLowerCase().includes(contentTypeOrName)
  );

  return attachment?.path ?? null;
}

const specs = flattenSuites(report.suites ?? []);
const rows = specs.flatMap(({ suite, spec }) =>
  (spec.tests ?? []).map((testCase) => {
    const result = testCase.results?.[testCase.results.length - 1] ?? {};
    const status = result.status ?? testCase.status ?? 'unknown';
    const error = result.error?.message ?? result.errors?.[0]?.message ?? null;

    return {
      suite,
      name: spec.title,
      project: testCase.projectName ?? 'default',
      status,
      durationMs: result.duration ?? 0,
      error,
      screenshotPath: attachmentPath(result, 'screenshot'),
      tracePath: attachmentPath(result, 'trace'),
      videoPath: attachmentPath(result, 'video')
    };
  })
);

const failed = rows.filter((row) => row.status === 'failed' || row.status === 'timedOut' || row.status === 'interrupted');
const skipped = rows.filter((row) => row.status === 'skipped');
const passed = rows.filter((row) => row.status === 'passed');
const overallStatus = failed.length > 0 ? 'failed' : 'passed';

const firstFailure = failed[0] ?? null;
const likelySelector = firstFailure?.error?.match(/data-testid=([^\s"'`]+)/i)?.[1] ?? null;

const codexShape = {
  runId: `ui-${Date.now()}`,
  suite: 'IronDev UI contract journeys',
  status: overallStatus,
  startedUtc,
  finishedUtc: new Date().toISOString(),
  durationMs: rows.reduce((total, row) => total + row.durationMs, 0),
  baseUrl: process.env.IRONDEV_UI_BASE_URL ?? 'http://127.0.0.1:5173',
  steps: rows.map((row) => ({
    name: row.name,
    status: row.status === 'passed' || row.status === 'skipped' ? row.status : 'failed',
    error: row.error,
    screenshotPath: row.screenshotPath,
    tracePath: row.tracePath
  })),
  artifacts: {
    jsonReport: path.normalize(inputPath),
    htmlReport: path.normalize('reports/html/index.html'),
    traceFiles: rows.map((row) => row.tracePath).filter(Boolean),
    screenshots: rows.map((row) => row.screenshotPath).filter(Boolean),
    videos: rows.map((row) => row.videoPath).filter(Boolean)
  }
};

const markdown = `# IronDev UI Test Report

## Suite

- Name: ${codexShape.suite}
- Status: ${codexShape.status}
- Passed: ${passed.length}
- Failed: ${failed.length}
- Skipped: ${skipped.length}
- Source: ${path.normalize(sourcePath)}
- Command: \`${command}\`
- Base URL: ${codexShape.baseUrl}

## Failed Test Summary

${failed.length === 0 ? 'No failed tests.' : failed.map((row) => `- ${row.suite}: ${row.name} (${row.project})`).join('\n')}

## Failed Step

${firstFailure ? firstFailure.name : 'None.'}

## Likely Failing Selector

${likelySelector ?? 'None detected from error output.'}

## Artifacts

- JSON report: ${codexShape.artifacts.jsonReport}
- HTML report: ${codexShape.artifacts.htmlReport}
- Screenshot path: ${firstFailure?.screenshotPath ?? 'None'}
- Trace path: ${firstFailure?.tracePath ?? 'None'}
- Video path: ${firstFailure?.videoPath ?? 'None'}

## Console And Network Highlights

Not captured by this lightweight report generator. Use the Playwright trace viewer for request, console, and DOM details when a real shell exists.

## Next Suggested Investigation

${firstFailure ? 'Open the HTML report and trace for the first failed test, then verify the referenced data-testid exists and the seeded API state is present.' : 'No failure investigation needed. If tests are skipped, build or connect the future UI shell and remove the explicit skip guards one journey at a time.'}

## Codex Result Shape

\`\`\`json
${JSON.stringify(codexShape, null, 2)}
\`\`\`
`;

mkdirSync(path.dirname(outputPath), { recursive: true });
writeFileSync(outputPath, markdown, 'utf8');

console.log(`Wrote ${outputPath}`);
