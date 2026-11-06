/**
 * The same build runs in every environment. The paths are relative to the page's own origin, where
 * nginx in production and proxy.conf.json in development send them to the API (docs/SPEC.md §15).
 */
export const environment = {
  apiBaseUrl: '/api',
  hubsBaseUrl: '/hubs',
} as const;
