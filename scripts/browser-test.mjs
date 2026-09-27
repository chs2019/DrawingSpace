import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const output = 'artifacts';
await fs.mkdir(`${output}/screenshots`, { recursive: true });
const results = [];
const consoleMessages = [];
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage();
const errors = [];
page.on('pageerror', error => { errors.push(error.message); consoleMessages.push(`ERROR ${error.stack}`); });
page.on('console', message => consoleMessages.push(`${message.type()} ${message.text()}`));
page.setDefaultTimeout(30000);
const snapshot = () => page.evaluate(() => globalThis.drawingSpaceSnapshot);
async function until(predicate, message, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const state = await snapshot();
    if (state && predicate(state)) return state;
    await page.waitForTimeout(120);
  }
  throw new Error(`${message}\nSnapshot: ${JSON.stringify(await snapshot())}`);
}
async function click(name) {
  const state = await until(s => s.elements.some(e => e.name === name && e.enabled && e.width > 0 && e.x >= 0 && e.x + e.width / 2 < 1440 && e.y >= 0 && e.y < 1000), `Missing visible command: ${name}`);
  const element = state.elements.find(e => e.name === name && e.enabled && e.x >= 0 && e.x + e.width / 2 < 1440 && e.y >= 0 && e.y < 1000);
  await page.mouse.click(element.x + element.width / 2, element.y + element.height / 2);
  await page.waitForTimeout(200);
}
function center(state, shape) {
  return { x: state.canvasX + state.panX + (shape.x + shape.width / 2) * state.zoom, y: state.canvasY + state.panY + (shape.y + shape.height / 2) * state.zoom };
}
async function check(name, action) {
  const started = Date.now();
  await action(); results.push({ name, passed: true, milliseconds: Date.now() - started }); console.log(`PASS ${name}`);
}
try {
  await page.goto(base + (base.includes('?') ? '&' : '?') + 'test=1', { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13 && s.canvasWidth > 500, 'Uno application did not become ready', 120000);
  await page.waitForTimeout(3000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 });
  await check('Real Uno/Skia workspace starts with connected editable sample', async () => {
    const state = await snapshot(); assert.equal(state.edges, 10); assert.equal(state.pages, 1);
    assert.ok(state.fontFamily.includes('Open Sans'), `Unexpected font: ${state.fontFamily}`);
    assert.ok(state.fontWidthRatio > 2, 'The diagram renderer fell back to a monospaced font');
    assert.ok(await page.locator('canvas').count() > 0); assert.deepEqual(errors, []);
    await page.screenshot({ path: `${output}/screenshots/DrawingSpace-workspace.png` });
  });
  await check('Stencil click inserts an undoable shape', async () => {
    await click('Insert Process'); await until(s => s.nodes === 14 && s.selection === 1, 'Stencil did not insert a shape');
    await page.keyboard.press('Control+z'); await until(s => s.nodes === 13, 'Undo did not remove insertion');
    await page.keyboard.press('Control+y'); await until(s => s.nodes === 14, 'Redo did not restore insertion');
  });
  await check('Pointer drag moves geometry and Undo restores exact coordinates', async () => {
    const state = await snapshot(); const shape = state.shapes.find(s => s.selected); assert.ok(shape); const original = { x: shape.x, y: shape.y };
    const point = center(state, shape); await page.mouse.move(point.x, point.y); await page.mouse.down(); await page.mouse.move(point.x + 54, point.y + 31, { steps: 9 }); await page.mouse.up();
    await until(s => s.shapes.some(n => n.id === shape.id && (n.x !== original.x || n.y !== original.y)), 'Shape drag did not change geometry');
    await page.keyboard.press('Control+z'); await until(s => s.shapes.some(n => n.id === shape.id && n.x === original.x && n.y === original.y), 'Undo did not restore geometry');
  });
  await check('Double-click edits a shape label through the actual text input', async () => {
    const state = await snapshot(); const shape = state.shapes.find(s => s.selected); const point = center(state, shape);
    await page.mouse.dblclick(point.x, point.y); await until(s => s.editingText, 'Text editor did not open');
    await page.keyboard.press('Control+a'); await page.keyboard.type('Ready for review'); await page.keyboard.press('Enter');
    await until(s => !s.editingText && s.shapes.some(n => n.text === 'Ready for review'), 'Edited label did not reach the document');
  });
  await check('Duplicate creates a new shape and undo removes it', async () => {
    const state = await snapshot(); const shape = state.shapes.find(s => s.text === 'Ready for review'); const point = center(state, shape);
    await page.mouse.click(point.x, point.y); await page.keyboard.press('Control+d'); await until(s => s.nodes === 15, 'Duplicate did not create a shape');
    await page.keyboard.press('Control+z'); await until(s => s.nodes === 14, 'Undo did not remove duplicate');
  });
  await check('Page insertion and undo preserve the original drawing', async () => {
    await click('Insert page'); await until(s => s.pages === 2 && s.nodes === 0, 'Page insertion failed');
    await click('Undo'); await until(s => s.pages === 1 && s.nodes === 14, 'Page undo failed');
  });
  await check('Zoom controls modify the actual viewport', async () => {
    const before = (await snapshot()).zoom; await click('Zoom in'); await until(s => s.zoom > before, 'Zoom did not change');
    await click('Fit page');
  });
  await check('Custom format pane opens and layout remains usable', async () => {
    await click('Format Shape'); await until(s => s.elements.some(e => e.name === 'Position X'), 'Format pane not present');
    await page.screenshot({ path: `${output}/screenshots/DrawingSpace-format-pane.png` });
    await click('Close task pane');
  });
  await check('SVG export downloads real paths and edited labels', async () => {
    await click('File'); const downloadPromise = page.waitForEvent('download'); await click('SVG'); const download = await downloadPromise;
    assert.ok(download.suggestedFilename().endsWith('.svg')); const file = await download.path(); const text = await fs.readFile(file, 'utf8');
    assert.match(text, /<svg/); assert.match(text, /<path/);
    const labels = await page.evaluate(svg => {
      const document = new DOMParser().parseFromString(svg, 'image/svg+xml');
      if (document.querySelector('parsererror')) throw new Error('SVG is not well-formed XML');
      return [...document.querySelectorAll('text')].map(node => {
        const spans = [...node.querySelectorAll('tspan')];
        return spans.length ? spans.map(span => span.textContent).join(' ') : node.textContent;
      });
    }, text);
    assert.ok(labels.includes('Ready for review'), 'The exported SVG lost the edited label');
  });
  await check('Local recovery survives browser reload', async () => {
    await until(s => s.status.includes('Exported') || s.status.includes('saved'), 'No completed operation status');
    await page.waitForTimeout(1200); await page.reload({ waitUntil: 'domcontentloaded', timeout: 120000 });
    await until(s => s.ready && s.nodes === 14 && s.shapes.some(n => n.text === 'Ready for review'), 'Recovery did not restore the edited drawing', 120000);
  });
  assert.deepEqual(errors, [], 'The browser reported JavaScript or WebAssembly errors');
  console.log(`Validated ${results.length} browser scenarios at ${base}`);
} catch (error) {
  results.push({ name: 'Failure', passed: false, error: error.stack });
  await fs.writeFile(`${output}/browser-snapshot.json`, JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: `${output}/screenshots/DrawingSpace-failure.png` }).catch(() => {});
  console.error(error); process.exitCode = 1;
} finally {
  await fs.writeFile(`${output}/browser-results.json`, JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile(`${output}/browser-console.log`, consoleMessages.join('\n'));
  await browser.close();
}
