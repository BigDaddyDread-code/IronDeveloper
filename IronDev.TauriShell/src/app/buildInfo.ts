export interface UiBuildInfo {
  version: string;
  branch: string;
  commit: string;
  commitShort: string;
  buildTimeUtc: string;
}

const unknown = 'unknown';

export const uiBuildInfo: UiBuildInfo = {
  version: readBuildValue(import.meta.env.VITE_IRONDEV_VERSION, import.meta.env.VITE_IRONDEV_UI_VERSION),
  branch: readBuildValue(import.meta.env.VITE_IRONDEV_BRANCH, import.meta.env.VITE_IRONDEV_UI_BRANCH),
  commit: readBuildValue(import.meta.env.VITE_IRONDEV_COMMIT, import.meta.env.VITE_IRONDEV_UI_COMMIT_SHA),
  commitShort: shortenCommit(import.meta.env.VITE_IRONDEV_COMMIT, import.meta.env.VITE_IRONDEV_UI_COMMIT_SHA),
  buildTimeUtc: readBuildValue(import.meta.env.VITE_IRONDEV_BUILD_TIME, import.meta.env.VITE_IRONDEV_UI_BUILD_TIME_UTC)
};

function readBuildValue(...values: Array<string | undefined>) {
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed && trimmed.length > 0) {
      return trimmed;
    }
  }

  return unknown;
}

function shortenCommit(...values: Array<string | undefined>) {
  const commit = readBuildValue(...values);
  return commit === unknown ? unknown : commit.slice(0, 7);
}
