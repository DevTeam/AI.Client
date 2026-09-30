export function timeZone() { return Intl.DateTimeFormat().resolvedOptions().timeZone; }
export function daysAgo(days) {
    const date = new Date();
    date.setDate(date.getDate() - days);
    const pad = n => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
export function toUtc(local) {
    const date = new Date(local);
    if (!local || Number.isNaN(date.getTime())) throw new Error('Choose a valid date and time.');
    return date.toISOString();
}

function preferenceKey(projectId) { return `ai.chatArchive.suggestions.${projectId}`; }
export function suggestionState(projectId) {
    try { return JSON.parse(localStorage.getItem(preferenceKey(projectId)) || '{}'); }
    catch { return {}; }
}
export function setSuggestions(projectId, enabled) {
    const state = suggestionState(projectId);
    localStorage.setItem(preferenceKey(projectId), JSON.stringify({ ...state, enabled }));
}
export function dismissSuggestion(projectId, fingerprint) {
    const state = suggestionState(projectId);
    localStorage.setItem(preferenceKey(projectId), JSON.stringify({ ...state, dismissed: fingerprint }));
}
export function setIndeterminate(element, value) { if (element) element.indeterminate = value; }
