/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_IRONDEV_API_BASE_URL?: string;
  readonly VITE_IRONDEV_PROJECT_ID?: string;
  readonly VITE_IRONDEV_DEV_TOKEN?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
