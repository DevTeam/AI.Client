import { readFileSync } from "node:fs";
import { test } from "node:test";
import assert from "node:assert/strict";

const home = readFileSync(new URL("../../../src/AI.Web/Pages/Home.razor", import.meta.url), "utf8");

// A row's menu is drawn inside its row, in the sidebar's scrolling list, so nothing about the row
// closes it when the person presses somewhere else. Each menu therefore comes with the page-level
// backdrop underneath it, and that backdrop is what catches the press and closes the menus.
const gated = new Map();
for (const match of home.matchAll(/@if \((_\w+) is not null\)\s*\{([^}]*)\}/g))
    gated.set(match[1], match[2]);

for (const [name, field] of [["chat", "_chatMenuId"], ["directory", "_directoryMenuId"], ["branch", "_branchMenuId"]]) {
    test(`the ${name} menu renders a backdrop that closes it on a press outside`, () => {
        const block = gated.get(field);
        assert.ok(block, `the ${name} menu (${field}) has no page-level menu block`);
        assert.match(block, /class="context-menu-backdrop"[^>]*@onclick="CloseContextMenus"/,
            `the ${name} menu has no backdrop closing the menus, so a press outside it leaves it open`);
    });
}
