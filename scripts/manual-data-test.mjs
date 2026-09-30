import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const size = { width: 1600, height: 1400 };
const results = [], errors = [], messages = [];
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage();
page.setDefaultTimeout(30000);
page.on('pageerror', e => { errors.push(e.message); messages.push(e.stack); });
page.on('console', m => messages.push(`${m.type()} ${m.text()}`));
const snapshot = () => page.evaluate(() => globalThis.drawingSpaceSnapshot);
const visible = e => e.enabled && e.width > 0 && e.height > 0 && e.x >= 0 && e.y >= 0
  && e.x + e.width / 2 < size.width && e.y + e.height / 2 < size.height;
async function until(predicate, message, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const state = await snapshot();
    if (state && predicate(state)) return state;
    await page.waitForTimeout(100);
  }
  throw new Error(`${message}\n${JSON.stringify(await snapshot())}`);
}
async function click(name) {
  let previous = '', observation = -1, stable = 0, target;
  await until(state => {
    if (state.observation === observation) return false;
    observation = state.observation;
    const matches = state.elements.filter(e => e.name === name && visible(e));
    if (matches.length !== 1) { previous = ''; stable = 0; return false; }
    target = matches[0]; const geometry = JSON.stringify([target.x, target.y, target.width, target.height]);
    stable = previous === geometry ? stable + 1 : 1; previous = geometry;
    return stable >= 3;
  }, `Missing unique settled control: ${name}`);
  await page.mouse.click(target.x + target.width / 2, target.y + target.height / 2);
  await page.waitForTimeout(180);
}
async function enter(name, text) { await click(name); await page.keyboard.press('Control+a'); await page.keyboard.type(text); }
async function action(name) { await click('Source Row Actions'); await click(name); }
async function selectSingleCanvasShape(id) {
  // Pressing an already-selected shape preserves the selection for group dragging.
  // Clear it with a real blank-canvas click before selecting the topmost fixture.
  let previous = '', observation = -1, stable = 0, target;
  async function settledPoint(pointFor, description) {
    previous = ''; observation = -1; stable = 0;
    await until(state => {
      if (state.observation === observation || state.gesture !== 'None') return false;
      observation = state.observation;
      target = pointFor(state);
      if (!target) { stable = 0; previous = ''; return false; }
      const geometry = JSON.stringify([state.activePageId, state.revision,
        state.canvasX, state.canvasY, state.canvasWidth, state.canvasHeight,
        state.panX, state.panY, state.zoom, target.x, target.y]);
      stable = previous === geometry ? stable + 1 : 1; previous = geometry;
      return stable >= 3;
    }, description);
    await page.mouse.click(target.x, target.y);
  }
  await settledPoint(state => {
    const x = 40, y = 40;
    const worldX = (x - state.panX) / state.zoom;
    const worldY = (y - state.panY) / state.zoom;
    if (state.shapes.some(s => worldX >= s.x - 8 && worldX <= s.x + s.width + 8
      && worldY >= s.y - 8 && worldY <= s.y + s.height + 8)) return null;
    return { x: state.canvasX + x, y: state.canvasY + y };
  }, 'Fixture has no settled blank-canvas selection target');
  await until(s => s.selection === 0 && s.gesture === 'None', 'Blank canvas did not clear selection');
  await settledPoint(state => {
    const shape = state.shapes.find(s => s.id === id);
    if (!shape) return null;
    const x = state.panX + (shape.x + shape.width / 2) * state.zoom;
    const y = state.panY + (shape.y + shape.height / 2) * state.zoom;
    if (x <= 22 || y <= 22 || x >= state.canvasWidth || y >= state.canvasHeight) return null;
    return { x: state.canvasX + x, y: state.canvasY + y };
  }, 'Linked shape did not reach a settled visible canvas position');
  return until(s => s.gesture === 'None' && s.selection === 1
    && s.shapes.some(shape => shape.id === id && shape.selected), 'Single linked shape selection failed');
}
async function check(name, task) {
  const started = Date.now();
  try { await task(); results.push({ name, passed: true, milliseconds: Date.now() - started }); console.log(`PASS ${name}`); }
  catch (e) { results.push({ name, passed: false, error: e.stack }); throw e; }
}
const linked = (state, key, value) => state.nodes === 2 && state.shapes.every(s => s.dataRowKey === key && s.data.Progress === value);
try {
  const url = new URL(base); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13, 'Application did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden' });
  await click('Insert page'); await click('Insert Process'); await click('Insert Process');
  await page.keyboard.press('Control+a');
  await until(s => s.nodes === 2 && s.selection === 2, 'Two fixture shapes were not selected');
  await click('Data');

  await check('Manual row selection, numeric sorting and filtering do not edit unmatched shapes', async () => {
    const chooser = page.waitForEvent('filechooser'); await click('Open CSV');
    await (await chooser).setFiles({ name: 'manual.csv', mimeType: 'text/csv',
      buffer: Buffer.from('Id,Progress,Owner\n0001,10,Alice\n0002,90,Bob\n' + Array.from({length:10}, (_, i) => `${i+10},50,Eve`).join('\n')) });
    await until(s => s.status.startsWith('Loaded data source manual.csv'), 'CSV source did not load');
    await click('Preview Refresh');
    const before = await until(s => s.status.startsWith('12 rows; 0 shapes') && s.elements.some(e => e.name === 'Select data row 0002'), 'Unmatched preview or row selector missing');
    await click('Select data row 0002'); await click('Numeric data sort'); await click('Sort data column Progress');
    await enter('Data filter', 'Bob'); await click('Filter data rows');
    const after = await until(s => s.elements.some(e => e.name === 'Data cell 1 Id: 0002') && !s.elements.some(e => e.name.startsWith('Data cell 2 Id:')), 'Source filtering did not retain exact selected key');
    assert.equal(after.revision, before.revision); assert.deepEqual(after.shapes, before.shapes);
  });
  await check('Link to Selected Shapes previews one exact row then applies one undoable transaction', async () => {
    const before = await snapshot(); await action('Link to Selected Shapes');
    const preview = await until(s => s.status.startsWith('Row 0002: 2 shapes to link'), 'Manual linking preview missing');
    assert.equal(preview.revision, before.revision); assert.ok(preview.shapes.every(s => s.dataRowKey === null));
    await click('Apply Refresh'); await until(s => linked(s, '0002', '90'), 'Selected row not linked to both shapes');
    await click('Undo'); await until(s => s.shapes.every(n => n.dataRowKey === null), 'Manual link undo failed');
    await click('Redo'); await until(s => linked(s, '0002', '90'), 'Manual link redo failed');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-manual-row-link.png' });
  });
  await check('Relinking the unchanged selected row is a revision and history no-op', async () => {
    await action('Link to Selected Shapes');
    const before = await until(s => s.status.startsWith('Row 0002: 0 shapes'), 'No-op manual plan missing');
    await click('Apply Refresh'); const after = await until(s => s.status === 'No data changes required.', 'No-op command did not complete');
    assert.equal(after.revision, before.revision); assert.deepEqual(after.shapes, before.shapes);
    assert.equal(after.undoName, before.undoName); assert.equal(after.redoName, before.redoName);
    assert.ok(after.elements.some(e => e.name === 'Data cell 1 Id: 0002'), 'Filter was lost during a pane rebuild');
  });
  await check('A different row requires explicit link replacement and field overwrite choices', async () => {
    await click('Clear data view'); await click('Select data row 0001');
    await action('Link to Selected Shapes');
    await until(s => s.status.startsWith('Row 0001: 0 shapes to link; 2 warnings'), 'Implicit relinking was not blocked');
    await click('Apply Refresh'); assert.ok(linked(await snapshot(), '0002', '90'));
    await click('Overwrite local conflicts');
    await click('Source Row Actions'); await click('Replace existing links'); await click('Link to Selected Shapes');
    await until(s => s.status.startsWith('Row 0001: 2 shapes to link'), 'Explicit relink plan missing');
    await click('Apply Refresh'); await until(s => linked(s, '0001', '10'), 'Explicit relinking failed');
  });
  await check('Show Linked Row clears a hiding filter and Linked Shapes selects without editing', async () => {
    let state = await snapshot(); const before = state; const shape = state.shapes.at(-1);
    const selected = await selectSingleCanvasShape(shape.id);
    assert.equal(selected.revision, before.revision);
    assert.deepEqual(selected.shapes.map(({selected, ...s}) => s), before.shapes.map(({selected, ...s}) => s));
    await enter('Data filter', 'Bob'); await click('Filter data rows');
    state = await until(s => s.elements.some(e => e.name === 'Data cell 1 Id: 0002'), 'Hiding filter missing');
    await action('Show Linked Row');
    await until(s => s.elements.some(e => e.name === 'Data cell 1 Id: 0001'), 'Linked row was not revealed');
    await action('Linked Shapes');
    const after = await until(s => s.selection === 2, 'Linked shapes navigation failed'); assert.equal(after.revision, state.revision);
  });
  await check('Unlink Row retains data graphics and values and is independently undoable', async () => {
    await click('Data Bars'); await click('Apply Data Graphic');
    await until(s => s.shapes.every(n => n.dataGraphics.includes('DataBar')), 'Data bars were not applied');
    await click('Link Data'); await action('Unlink Row');
    await until(s => s.shapes.every(n => n.dataRowKey === null && n.data.Progress === '10' && n.dataGraphics.includes('DataBar')), 'Unlink damaged data or graphics');
    await click('Undo'); await until(s => linked(s, '0001', '10'), 'Row unlink undo failed');
  });
  await check('Native save persists manually selected row keys and accepted baselines', async () => {
    const pending = page.waitForEvent('download'); await click('Save');
    const bytes = await fs.readFile(await (await pending).path());
    const document = JSON.parse(bytes.toString('utf8'));
    const ids = new Set((await snapshot()).shapes.map(s => s.id));
    const shapes = document.pages.flatMap(p => p.shapes).filter(s => ids.has(s.id));
    assert.equal(shapes.length, 2);
    for (const shape of shapes) { assert.equal(shape.dataBinding.rowKey, '0001'); assert.equal(shape.dataBinding.baseline.Progress, '10'); }
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-linked-source-navigation.png' });
  });
  await check('Arrow navigation retains focus across row pages without changing the drawing', async () => {
    const before = await snapshot();
    await click('Clear data view'); await click('Select data row 12');
    await page.keyboard.press('ArrowDown');
    await until(s => s.elements.some(e => e.name === 'Data cell 1 Id: 13'), 'ArrowDown did not enter the next row page');
    await page.keyboard.press('ArrowUp');
    await until(s => s.elements.some(e => e.name === 'Data cell 1 Id: 0001'), 'ArrowUp lost keyboard focus across pages');
    await page.keyboard.press('ArrowDown');
    await until(s => s.elements.some(e => e.name === 'Data cell 1 Id: 13'), 'Second page traversal lost focus');
    await page.keyboard.press('ArrowDown');
    await action('Link to Selected Shapes');
    const after = await until(s => s.status.startsWith('Row 14:'), 'Keyboard selection did not retain the exact source key');
    assert.equal(after.revision, before.revision); assert.deepEqual(after.shapes, before.shapes);
    assert.equal(after.undoName, before.undoName); assert.equal(after.redoName, before.redoName);
  });
  assert.deepEqual(errors, []); console.log(`Validated ${results.length} manual data scenarios at ${base}`);
} catch (error) {
  console.error(error); process.exitCode = 1;
  await fs.writeFile('artifacts/manual-data-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-manual-data-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/manual-data-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/manual-data-console.log', messages.join('\n'));
  await browser.close();
}
