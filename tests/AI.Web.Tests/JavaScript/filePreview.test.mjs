import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/filePreview.js', import.meta.url), 'utf8')
    .replace(/^import .*$/m, '').replace(/export /g, '');

test('line navigation scrolls only the preview and accounts for its padding and line height', () => {
    const body = { scrollTop: 100, clientHeight: 300, getBoundingClientRect: () => ({ top: 50 }) };
    const pre = {};
    const code = { querySelector: () => pre, getBoundingClientRect: () => ({ top: -50 }) };
    const panel = { querySelector: selector => selector === '.file-preview-body' ? body : code };
    const context = createContext({ document: { getElementById: () => panel },
        getComputedStyle: () => ({ paddingTop: '12.8px', lineHeight: '19.2px' }) });
    runInContext(source, context);
    context.scrollToLine('preview', 20);
    assert.equal(body.scrollTop, 277.6);
    body.scrollTop = 100;
    context.scrollToLine('preview', 1);
    assert.equal(body.scrollTop, 0);
    context.scrollToLine('preview', null);
    assert.equal(body.scrollTop, 0);
});
