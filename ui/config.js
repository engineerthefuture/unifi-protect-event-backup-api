// config.js
// Centralized config for UI deployment variables
window.__CONFIG__ = {
    API_URL: '%%API_URL%%', // PATCHED BY WORKFLOW
    API_KEY: '%%API_KEY%%', // PATCHED BY WORKFLOW
    LOGIN_URL: '%%COGNITO_REPLACEMENT%%' // PATCHED BY WORKFLOW
};