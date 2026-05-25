import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

const DEFAULT_API_BASE_URL = 'http://localhost:5000';
const DEFAULT_OUTPUT = 'openapi/irondev-api.openapi.json';

const apiBaseUrl = (
  process.env.IRONDEV_API_BASE_URL ??
  process.env.VITE_IRONDEV_API_BASE_URL ??
  DEFAULT_API_BASE_URL
).replace(/\/+$/, '');

const outputPath = resolve(process.cwd(), process.env.IRONDEV_OPENAPI_OUTPUT ?? DEFAULT_OUTPUT);
const sourceUrl = `${apiBaseUrl}/swagger/v1/swagger.json`;

const response = await fetch(sourceUrl);

if (!response.ok) {
  throw new Error(`Failed to fetch OpenAPI spec from ${sourceUrl}: HTTP ${response.status}`);
}

const spec = await response.text();
await mkdir(dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${spec.trim()}\n`, 'utf8');

console.log(`Fetched IronDev.Api OpenAPI spec from ${sourceUrl}`);
console.log(`Wrote ${outputPath}`);
