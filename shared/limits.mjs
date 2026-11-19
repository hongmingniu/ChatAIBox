export const SUMMARY_OUTPUT_TOKENS = 80 * 1024;
export const CHAT_OUTPUT_TOKENS = 24 * 1024;
export const MODEL_CONTEXT_TOKENS = 1024 * 1024;
export const CONTEXT_OVERHEAD_TOKENS = 4096;
export const CONTEXT_INPUT_BUDGET = MODEL_CONTEXT_TOKENS - CHAT_OUTPUT_TOKENS - CONTEXT_OVERHEAD_TOKENS;
// Character storage limits are independent of provider token counts.
export const SUMMARY_MAX_CHARACTERS = 1_000_000;
export const REQUEST_TIMEOUT_MS = 30 * 60 * 1000;
