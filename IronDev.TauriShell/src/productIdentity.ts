export const IRONDEV_PRODUCT_NAME = 'IronDev';
export const IRONDEV_VERSION = '0.5.0';
export const IRONDEV_BUILD = import.meta.env.VITE_IRONDEV_BUILD_SHA?.trim() || 'development';

export function ironDevVersionText() {
  return `${IRONDEV_PRODUCT_NAME} ${IRONDEV_VERSION} (${IRONDEV_BUILD})`;
}
