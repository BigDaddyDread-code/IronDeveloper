export interface UiBuildInfo {
  version: string;
  branch: string;
  commit: string;
  commitShort: string;
  buildTimeUtc: string;
}

const unknown = 'unknown';

export const uiBuildInfo: UiBuildInfo = {
  version: readBuildValue(import.meta.env.VITE_IRONDEV_UI_VERSION),
  branch: readBuildValue(import.meta.env.VITE_IRONDEV_UI_BRANCH),
  commit: readBuildValue(import.meta.env.VITE_IRONDEV_UI_COMMIT_SHA),
  commitShort: shortenCommit(import.meta.env.VITE_IRONDEV_UI_COMMIT_SHA),
  buildTimeUtc: readBuildValue(import.meta.env.VITE_IRONDEV_UI_BUILD_TIME_UTC)
};

function readBuildValue(value: string | undefined) {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : unknown;
}

function shortenCommit(value: string | undefined) {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed.slice(0, 7) : unknown;
}
