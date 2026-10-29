import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/theme.js', import.meta.url), 'utf8');
const storageKey = 'ai-client.settings';

// A page that records what the script did to <html> and when. The script runs in <head>, before the
// stylesheet, so anything it has not set here is a flash of the wrong theme for the person.
function setUp(saved, options = {}) {
    const properties = new Map();
    const attributes = new Map();
    const events = [];
    const messages = [];
    let storage = saved;
    const root = {
        dataset: {},
        style: {
            setProperty: (name, value) => properties.set(name, value),
            removeProperty: name => properties.delete(name)
        },
        getAttribute: name => attributes.get(name) ?? null,
        setAttribute: (name, value) => attributes.set(name, value)
    };
    const meta = { setAttribute: (name, value) => attributes.set(`meta:${name}`, value) };
    const document = {
        documentElement: root,
        querySelector: selector => selector === 'meta[name="theme-color"]' ? meta : null,
        addEventListener() {},
        dispatchEvent: event => events.push(event)
    };
    const context = createContext({
        document,
        CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init?.detail; } },
        matchMedia: () => ({ matches: options.systemLight === true, addEventListener() {} }),
        localStorage: {
            length: storage ? 1 : 0,
            key: () => storageKey,
            getItem: key => key === storageKey ? storage : null,
            setItem(key, value) { if (key === storageKey) storage = value; }
        },
        setTimeout,
        clearTimeout
    });
    if (options.desktop) context.invokeCSharpAction = json => messages.push(JSON.parse(json));
    runInContext(source, context);
    // --corner-scale and the like are written the same way and are not part of a theme's palette.
    const palette = () => new Map([...properties].filter(([name]) => name.startsWith('--color-')));
    return { context, root, properties, palette, attributes, events, messages };
}

const theme = () => ({
    id: 'theme-1',
    name: 'Warm dusk',
    family: 'dark',
    tokens: {
        '--color-bg': '#2b2320',
        '--color-text': '#fffdfc',
        '--color-surface': '#2f2723',
        '--color-accent-strong': '#ff0000',
        'not-a-property': '#123456'
    }
});

const settings = overrides => JSON.stringify({ theme: 'dark', accent: 'blue', ...overrides });

test('applies the saved custom theme and its palette before the first paint', () => {
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [theme()] });
    const { root, palette } = setUp(saved);

    assert.equal(root.dataset.theme, 'custom');
    assert.equal(root.dataset.themeFamily, 'dark');
    assert.deepEqual([...palette()], [
        ['--color-bg', '#2b2320'],
        ['--color-text', '#fffdfc'],
        ['--color-surface', '#2f2723']
    ]);
});

test('paints the active theme with its own accent before the first paint', () => {
    const saved = settings({ accent: 'blue', activeCustomThemeId: 'theme-1', customThemes: [{ ...theme(), accent: 'green' }] });
    const { root } = setUp(saved);

    assert.equal(root.dataset.accent, 'green');
});

test('keeps the presets accent for a preset and for a theme saved without one', () => {
    assert.equal(setUp(settings({ accent: 'pink', customThemes: [{ ...theme(), accent: 'green' }] })).root.dataset.accent, 'pink');
    assert.equal(setUp(settings({ accent: 'pink', activeCustomThemeId: 'theme-1', customThemes: [theme()] })).root.dataset.accent, 'pink');
});

test('never applies a role a theme may not repaint', () => {
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [theme()] });
    const { palette } = setUp(saved);

    assert.equal(palette().has('--color-accent-strong'), false);
    assert.equal(palette().has('not-a-property'), false);
});

test('carries the family of a light custom theme into the stylesheet attribute', () => {
    const light = { ...theme(), family: 'light', tokens: { '--color-bg': '#f7f2e8' } };
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [light] });
    const { root, attributes } = setUp(saved);

    assert.equal(root.dataset.theme, 'custom');
    assert.equal(root.dataset.themeFamily, 'light');
    assert.equal(attributes.get('meta:content'), '#f7f2e8');
});

test('falls back to the preset when the named theme is gone', () => {
    const saved = settings({ theme: 'lightgray', activeCustomThemeId: 'missing', customThemes: [theme()] });
    const { root, palette } = setUp(saved);

    assert.equal(root.dataset.theme, 'lightgray');
    assert.equal(root.dataset.themeFamily, 'light');
    assert.equal(palette().size, 0);
});

test('falls back to the preset when nothing names a custom theme', () => {
    const { root, palette } = setUp(settings({ theme: 'darkblue' }));

    assert.equal(root.dataset.theme, 'darkblue');
    assert.equal(root.dataset.themeFamily, 'dark');
    assert.equal(palette().size, 0);
});

test('switching to a preset removes the custom palette instead of layering over it', () => {
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [theme()] });
    const { context, root, palette } = setUp(saved);
    assert.equal(palette().size, 3);

    context.aiClientTheme.applyCustomTheme(null);

    assert.equal(root.dataset.theme, 'dark');
    assert.equal(palette().size, 0);
});

test('switching the active theme replaces the previous palette', () => {
    const other = { ...theme(), id: 'theme-2', tokens: { '--color-bg': '#112233' } };
    const { context, palette } = setUp(settings());

    context.aiClientTheme.applyCustomTheme(theme(), false);
    context.aiClientTheme.applyCustomTheme(other, false);

    assert.equal(palette().get('--color-bg'), '#112233');
    assert.equal(palette().has('--color-text'), false);
});

test('announces the family with the theme so other scripts can follow it', () => {
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [theme()] });
    const { events } = setUp(saved);

    const change = events.at(-1);
    assert.equal(change.type, 'ai-client-theme-change');
    assert.equal(change.detail.theme, 'custom');
    assert.equal(change.detail.family, 'dark');
});

test('tells the desktop host the preset preference a custom theme stands in for', () => {
    const { context, messages } = setUp(settings({ theme: 'light' }), { desktop: true });

    context.aiClientTheme.applyCustomTheme(theme());

    // The host paints its own window and titlebar from the preference, so a custom theme reports the
    // preset it stands in for rather than a value the host has no way to understand.
    assert.deepEqual(messages.filter(message => message.type === 'theme').at(-1),
        { type: 'theme', preference: 'light' });
});

test('an unknown preference still falls back to the operating system', () => {
    const { root } = setUp(settings({ theme: 'chartreuse' }), { systemLight: true });

    assert.equal(root.dataset.themePreference, 'system');
    assert.equal(root.dataset.theme, 'light');
});

test('an applied preset leaves no palette of a saved theme behind', () => {
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [theme()] });
    const { context, root, palette } = setUp(saved);
    assert.equal(palette().size, 3);

    // The service removes the palette and then applies the preset; either order must end with none.
    context.aiClientTheme.applyCustomTheme(null, false);
    context.aiClientTheme.apply('darkblue', false);

    assert.equal(root.dataset.theme, 'darkblue');
    assert.equal(palette().size, 0);
});

test('a preview replaces the saved palette without being saved', () => {
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [theme()] });
    const { context, root, palette, events } = setUp(saved);

    context.aiClientTheme.applyCustomTheme({ id: 'preview', family: 'light', tokens: { '--color-bg': '#f7f2e8' } }, false);

    assert.equal(root.dataset.theme, 'custom');
    assert.equal(root.dataset.themeFamily, 'light');
    assert.deepEqual([...palette()], [['--color-bg', '#f7f2e8']]);
    assert.equal(events.at(-1).detail.family, 'light');
});

test('a stored palette that is not a custom property is ignored', () => {
    const handEdited = { ...theme(), tokens: { '--color-bg': '#123456', '--corner-scale': '9', width: '1px' } };
    const saved = settings({ activeCustomThemeId: 'theme-1', customThemes: [handEdited] });
    const { palette } = setUp(saved);

    // A hand-edited entry cannot reach geometry or a property the stylesheet owns.
    assert.deepEqual([...palette()], [['--color-bg', '#123456']]);
});
