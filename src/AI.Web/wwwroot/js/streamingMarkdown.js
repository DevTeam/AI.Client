// Blazor owns the wrapper, but not its children after initialization. Reconcile the HTML Markdig
// produces as a stream grows instead of replacing completed links with every new text fragment.
// Other feed observers may decorate those children, so comparisons use the previous source HTML
// rather than the DOM after file-link and search annotations were added.
const sourceHtml = new WeakMap();
const sourceAttributes = new WeakMap();

const attributesOf = element => new Map([...element.attributes].map(attribute => [attribute.name, attribute.value]));

const remember = (element, source) => {
    if (element.nodeType !== Node.ELEMENT_NODE || source.nodeType !== Node.ELEMENT_NODE) return;
    sourceHtml.set(element, source.outerHTML);
    sourceAttributes.set(element, attributesOf(source));
    const currentChildren = element.childNodes;
    const sourceChildren = source.childNodes;
    for (let index = 0; index < Math.min(currentChildren.length, sourceChildren.length); index++)
        remember(currentChildren[index], sourceChildren[index]);
};

const clone = source => {
    const element = source.cloneNode(true);
    remember(element, source);
    return element;
};

const compatible = (current, source) => current.nodeType === source.nodeType
    && (current.nodeType !== Node.ELEMENT_NODE || current.tagName === source.tagName);

const updateAttributes = (current, source) => {
    const previous = sourceAttributes.get(current) ?? attributesOf(current);
    const next = attributesOf(source);
    for (const name of previous.keys()) if (!next.has(name)) current.removeAttribute(name);
    for (const [name, value] of next) if (previous.get(name) !== value) current.setAttribute(name, value);
    sourceAttributes.set(current, next);
};

const updateNode = (current, source) => {
    if (current.nodeType === Node.TEXT_NODE) {
        if (current.data !== source.data) current.data = source.data;
        return current;
    }
    if (current.nodeType !== Node.ELEMENT_NODE) {
        if (current.textContent !== source.textContent) current.textContent = source.textContent;
        return current;
    }
    const html = source.outerHTML;
    if (sourceHtml.get(current) === html) return current;
    // A changed link or code span may carry decorations whose source is no longer current.
    // Replace that one element; unchanged ones are retained by the check above.
    if (current.tagName === 'A' || current.tagName === 'CODE') {
        const replacement = clone(source);
        current.replaceWith(replacement);
        return replacement;
    }
    updateAttributes(current, source);
    updateChildren(current, source);
    sourceHtml.set(current, html);
    return current;
};

function updateChildren(parent, source) {
    let current = parent.firstChild;
    for (const wanted of source.childNodes) {
        if (!current) {
            parent.append(clone(wanted));
            continue;
        }
        const next = current.nextSibling;
        if (compatible(current, wanted)) updateNode(current, wanted);
        else current.replaceWith(clone(wanted));
        current = next;
    }
    while (current) {
        const next = current.nextSibling;
        current.remove();
        current = next;
    }
}

const parse = html => {
    const template = document.createElement('template');
    template.innerHTML = html;
    return template.content;
};

export function initialize(element, html) {
    const source = parse(html);
    const current = element.childNodes;
    const wanted = source.childNodes;
    for (let index = 0; index < Math.min(current.length, wanted.length); index++)
        remember(current[index], wanted[index]);
}

export function update(element, html) {
    updateChildren(element, parse(html));
}
