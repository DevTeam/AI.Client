import { highlightText } from './codeHighlight.js';

export function attach(id) {
    const panel = document.getElementById(id);
    const handle = panel.querySelector('.file-preview-resizer');
    const apply = width => {
        const value = Math.max(Math.min(320, innerWidth), Math.min(innerWidth * .94, width));
        panel.style.width = `${value}px`;
        handle.setAttribute('aria-valuenow', Math.round(value));
        return value;
    };
    const saved = Number(localStorage.getItem('file-preview-width'));
    if (saved > 0) apply(saved);
    const down = event => {
        if (event.button !== 0) return;
        event.preventDefault();
        handle.setPointerCapture(event.pointerId);
    };
    const move = event => {
        if (handle.hasPointerCapture(event.pointerId)) apply(innerWidth - event.clientX);
    };
    const up = event => {
        if (!handle.hasPointerCapture(event.pointerId)) return;
        handle.releasePointerCapture(event.pointerId);
        localStorage.setItem('file-preview-width', handle.getAttribute('aria-valuenow'));
    };
    const key = event => {
        if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
        event.preventDefault();
        localStorage.setItem('file-preview-width', apply(panel.clientWidth + (event.key === 'ArrowLeft' ? 24 : -24)));
    };
    const resize = () => apply(panel.clientWidth);
    handle.addEventListener('pointerdown', down);
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
    handle.addEventListener('keydown', key);
    window.addEventListener('resize', resize);
    return { dispose() {
        handle.removeEventListener('pointerdown', down);
        handle.removeEventListener('pointermove', move);
        handle.removeEventListener('pointerup', up);
        handle.removeEventListener('pointercancel', up);
        handle.removeEventListener('keydown', key);
        window.removeEventListener('resize', resize);
    } };
}

export const copy = text => navigator.clipboard.writeText(text);
export const imageWidth = id => document.getElementById(id)?.querySelector('img')?.naturalWidth ?? 0;
export async function fullscreen(id) {
    if (document.fullscreenElement) await document.exitFullscreen();
    else await document.getElementById(id).requestFullscreen();
}
export async function download(url, name) {
    const response = await fetch(url);
    if (!response.ok) throw new Error('Download failed');
    const blob = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = blob;
    link.download = name;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(blob), 60000);
}
export async function highlight(text, name) {
    const extension = name?.split('.').pop()?.toLowerCase();
    const languages = { cs: 'csharp', js: 'javascript', mjs: 'javascript', ts: 'typescript',
        py: 'python', ps1: 'powershell', fs: 'fsharp', sh: 'bash', md: 'markdown',
        json: 'json', html: 'xml', xml: 'xml', svg: 'xml', css: 'css', java: 'java',
        yml: 'yaml', yaml: 'yaml', sql: 'sql', cpp: 'cpp', h: 'cpp', c: 'c', rs: 'rust' };
    return languages[extension] ? highlightText(text, languages[extension]) : null;
}
