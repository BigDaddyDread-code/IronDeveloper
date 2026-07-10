import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { access, mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const frontendRoot = resolve(scriptDirectory, '..');
const checkedOpenApiPath = join(frontendRoot, 'openapi', 'irondev-api.openapi.json');
const checkedTypesPath = join(frontendRoot, 'src', 'api', 'generated', 'ironDevApiTypes.ts');
const generatorPath = join(frontendRoot, 'node_modules', 'openapi-typescript', 'bin', 'cli.js');
const packagePath = join(frontendRoot, 'package.json');
const installedGeneratorPackagePath = join(frontendRoot, 'node_modules', 'openapi-typescript', 'package.json');
const apiBaseUrl = (
  process.env.IRONDEV_API_BASE_URL ??
  process.env.VITE_IRONDEV_API_BASE_URL ??
  'http://localhost:5000'
).replace(/\/+$/, '');
const sourceUrl = `${apiBaseUrl}/swagger/v1/swagger.json`;
const requireClean = process.argv.includes('--require-clean');
const outputIndex = process.argv.indexOf('--output');
const outputPath = outputIndex >= 0 ? resolve(process.cwd(), process.argv[outputIndex + 1] ?? '') : null;
const tempRoot = await mkdtemp(join(tmpdir(), 'irondev-openapi-diagnose-'));

try {
  await access(generatorPath);

  const packageDocument = JSON.parse(await readFile(packagePath, 'utf8'));
  const installedGenerator = JSON.parse(await readFile(installedGeneratorPackagePath, 'utf8'));
  const dotnetVersion = (await execFileAsync('dotnet', ['--version'])).stdout.trim();
  const expectedToolchain = {
    dotnetSdk: '10.0.301',
    node: packageDocument.engines.node,
    npm: packageDocument.engines.npm,
    openapiTypescript: packageDocument.devDependencies['openapi-typescript']
  };
  const actualToolchain = {
    dotnetSdk: dotnetVersion,
    node: process.version.replace(/^v/, ''),
    npm: npmVersion(),
    openapiTypescript: installedGenerator.version
  };

  const firstOpenApiPath = join(tempRoot, 'first.openapi.json');
  const secondOpenApiPath = join(tempRoot, 'second.openapi.json');
  const firstTypesPath = join(tempRoot, 'first.types.ts');
  const secondTypesPath = join(tempRoot, 'second.types.ts');

  const firstOpenApi = await fetchContract(firstOpenApiPath);
  await generateTypes(firstOpenApiPath, firstTypesPath);
  const firstTypes = await readFile(firstTypesPath);

  const secondOpenApi = await fetchContract(secondOpenApiPath);
  await generateTypes(secondOpenApiPath, secondTypesPath);
  const secondTypes = await readFile(secondTypesPath);

  const checkedOpenApi = await readFile(checkedOpenApiPath);
  const checkedTypes = await readFile(checkedTypesPath);
  const checkedDocument = JSON.parse(checkedOpenApi.toString('utf8'));
  const liveDocument = JSON.parse(firstOpenApi.toString('utf8'));
  const structuralDrift = compareDocuments(checkedDocument, liveDocument);

  const report = {
    sourceUrl,
    toolchain: {
      expected: expectedToolchain,
      actual: actualToolchain,
      matchesPinned: stableJson(expectedToolchain) === stableJson(actualToolchain)
    },
    deterministic: {
      openApiByteIdentical: firstOpenApi.equals(secondOpenApi),
      generatedTypesByteIdentical: firstTypes.equals(secondTypes),
      firstOpenApiSha256: sha256(firstOpenApi),
      secondOpenApiSha256: sha256(secondOpenApi),
      firstTypesSha256: sha256(firstTypes),
      secondTypesSha256: sha256(secondTypes)
    },
    checkedIn: {
      openApiSha256: sha256(checkedOpenApi),
      generatedTypesSha256: sha256(checkedTypes),
      openApiBytes: checkedOpenApi.length,
      generatedTypesBytes: checkedTypes.length,
      openApiLineEndings: lineEndings(checkedOpenApi),
      ...summarize(checkedDocument)
    },
    live: {
      openApiSha256: sha256(firstOpenApi),
      generatedTypesSha256: sha256(firstTypes),
      openApiBytes: firstOpenApi.length,
      generatedTypesBytes: firstTypes.length,
      openApiLineEndings: lineEndings(firstOpenApi),
      ...summarize(liveDocument)
    },
    drift: {
      openApiByteIdentical: checkedOpenApi.equals(firstOpenApi),
      generatedTypesByteIdentical: checkedTypes.equals(firstTypes),
      ...structuralDrift
    }
  };

  printSummary(report);

  if (outputPath) {
    await mkdir(dirname(outputPath), { recursive: true });
    await writeFile(outputPath, `${JSON.stringify(report, null, 2)}\n`, 'utf8');
    console.log(`Diagnostic report: ${outputPath}`);
  }

  if (!report.deterministic.openApiByteIdentical || !report.deterministic.generatedTypesByteIdentical) {
    throw new Error('FAIL OpenAPI generation is not deterministic for two runs against the same API process.');
  }

  if (!report.toolchain.matchesPinned) {
    throw new Error('FAIL The active toolchain does not match the repository pins.');
  }

  if (requireClean && (!report.drift.openApiByteIdentical || !report.drift.generatedTypesByteIdentical)) {
    throw new Error('FAIL The checked-in OpenAPI contract does not match the running API.');
  }
} finally {
  await rm(tempRoot, { recursive: true, force: true });
}

async function fetchContract(destination) {
  const response = await fetch(sourceUrl);
  if (!response.ok) {
    throw new Error(`Failed to fetch ${sourceUrl}: HTTP ${response.status}`);
  }

  // Match fetch-openapi-spec.mjs exactly so --require-clean can become the
  // post-refresh CI gate without a trailing-newline false positive.
  const bytes = Buffer.from(`${(await response.text()).trim()}\n`, 'utf8');
  await writeFile(destination, bytes);
  return bytes;
}

async function generateTypes(input, output) {
  await execFileAsync(process.execPath, [generatorPath, input, '--output', output], {
    cwd: frontendRoot,
    maxBuffer: 10 * 1024 * 1024
  });
}

function summarize(document) {
  const paths = Object.values(document.paths ?? {});
  const operationMethods = new Set(['get', 'post', 'put', 'patch', 'delete', 'options', 'head', 'trace']);
  const operations = paths.reduce(
    (count, path) => count + Object.keys(path).filter((method) => operationMethods.has(method)).length,
    0
  );

  return {
    pathCount: Object.keys(document.paths ?? {}).length,
    operationCount: operations,
    schemaCount: Object.keys(document.components?.schemas ?? {}).length
  };
}

function compareDocuments(checkedDocument, liveDocument) {
  const checkedPaths = Object.keys(checkedDocument.paths ?? {});
  const livePaths = Object.keys(liveDocument.paths ?? {});
  const checkedSchemas = Object.keys(checkedDocument.components?.schemas ?? {});
  const liveSchemas = Object.keys(liveDocument.components?.schemas ?? {});
  const checkedPathSet = new Set(checkedPaths);
  const livePathSet = new Set(livePaths);
  const checkedSchemaSet = new Set(checkedSchemas);
  const liveSchemaSet = new Set(liveSchemas);
  const commonPaths = livePaths.filter((path) => checkedPathSet.has(path));
  const commonSchemas = liveSchemas.filter((name) => checkedSchemaSet.has(name));

  return {
    addedPaths: livePaths.filter((path) => !checkedPathSet.has(path)).sort(),
    removedPaths: checkedPaths.filter((path) => !livePathSet.has(path)).sort(),
    changedCommonPaths: commonPaths
      .filter((path) => stableJson(checkedDocument.paths[path]) !== stableJson(liveDocument.paths[path]))
      .sort(),
    addedSchemas: liveSchemas.filter((name) => !checkedSchemaSet.has(name)).sort(),
    removedSchemas: checkedSchemas.filter((name) => !liveSchemaSet.has(name)).sort(),
    changedCommonSchemas: commonSchemas
      .filter((name) => stableJson(checkedDocument.components.schemas[name]) !== stableJson(liveDocument.components.schemas[name]))
      .sort()
  };
}

function stableJson(value) {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function lineEndings(bytes) {
  let carriageReturns = 0;
  let lineFeeds = 0;
  for (const byte of bytes) {
    if (byte === 13) carriageReturns += 1;
    if (byte === 10) lineFeeds += 1;
  }
  return { carriageReturns, lineFeeds };
}

function npmVersion() {
  const userAgent = process.env.npm_config_user_agent ?? '';
  return userAgent.match(/npm\/([^ ]+)/)?.[1] ?? 'unknown';
}

function printSummary(report) {
  console.log(`OpenAPI source: ${report.sourceUrl}`);
  console.log(`Toolchain matches repository pins: ${report.toolchain.matchesPinned}`);
  console.log(`OpenAPI deterministic: ${report.deterministic.openApiByteIdentical}`);
  console.log(`Type generation deterministic: ${report.deterministic.generatedTypesByteIdentical}`);
  console.log(`Paths: checked-in ${report.checkedIn.pathCount}, live ${report.live.pathCount}, added ${report.drift.addedPaths.length}, removed ${report.drift.removedPaths.length}`);
  console.log(`Operations: checked-in ${report.checkedIn.operationCount}, live ${report.live.operationCount}`);
  console.log(`Schemas: checked-in ${report.checkedIn.schemaCount}, live ${report.live.schemaCount}, added ${report.drift.addedSchemas.length}, removed ${report.drift.removedSchemas.length}`);
  console.log(`Changed common paths: ${report.drift.changedCommonPaths.length}`);
  console.log(`Changed common schemas: ${report.drift.changedCommonSchemas.length}`);
  console.log(`Checked-in contract matches live API: ${report.drift.openApiByteIdentical}`);
  console.log(`Checked-in generated types match live API: ${report.drift.generatedTypesByteIdentical}`);
}
