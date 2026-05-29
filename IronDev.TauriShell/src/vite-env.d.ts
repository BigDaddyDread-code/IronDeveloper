/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_IRONDEV_API_BASE_URL?: string;
  readonly VITE_IRONDEV_PROJECT_ID?: string;
  readonly VITE_IRONDEV_DEV_TOKEN?: string;
  readonly VITE_IRONDEV_UI_VERSION?: string;
  readonly VITE_IRONDEV_UI_BRANCH?: string;
  readonly VITE_IRONDEV_UI_COMMIT_SHA?: string;
  readonly VITE_IRONDEV_UI_BUILD_TIME_UTC?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
