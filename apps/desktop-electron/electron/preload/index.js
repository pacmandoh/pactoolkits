/**
 * Electron preload placeholder.
 *
 * Future: expose a minimal, typed bridge to the Nuxt renderer via contextBridge.
 * Business APIs should come from packages/application + packages/agent-contracts,
 * not direct database access from the renderer.
 */

/** @type {string} */
const PLACEHOLDER = 'PacToolkits Electron preload (placeholder)';

module.exports = { PLACEHOLDER };
