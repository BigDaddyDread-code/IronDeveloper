/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_IRONDEV_API_BASE_URL?: string;
  readonly VITE_IRONDEV_PROJECT_ID?: string;
  readonly VITE_IRONDEV_DEV_TOKEN?: string;
  readonly VITE_IRONDEV_VERSION?: string;
  readonly VITE_IRONDEV_BRANCH?: string;
  readonly VITE_IRONDEV_COMMIT?: string;
  readonly VITE_IRONDEV_BUILD_TIME?: string;
  readonly VITE_IRONDEV_LOCALTEST_SESSION_ID?: string;
  readonly VITE_IRONDEV_LOCALTEST_REPOSITORY_COMMIT?: string;
  readonly VITE_IRONDEV_LOCALTEST_API_BASE_URL?: string;
  readonly VITE_IRONDEV_LOCALTEST_SESSION_MODE?: string;
  readonly VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED?: string;
  readonly VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED?: string;
  readonly VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT?: string;
  readonly VITE_IRONDEV_LOCALTEST_CAPABILITIES?: string;
  readonly VITE_IRONDEV_UI_VERSION?: string;
  readonly VITE_IRONDEV_UI_BRANCH?: string;
  readonly VITE_IRONDEV_UI_COMMIT_SHA?: string;
  readonly VITE_IRONDEV_UI_BUILD_TIME_UTC?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
