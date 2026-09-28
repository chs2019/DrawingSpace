import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const results = [], errors = [], messages = [];
const size = { width: 1600, height: 1400 };
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage();
page.on('pageerror', error => { errors.push(error.message); messages.push(error.stack); });
page.on('console', message => messages.push(`${message.type()} ${message.text()}`));
page.setDefaultTimeout(30000);
const snapshot = () => page.evaluate(() => globalThis.drawingSpaceSnapshot);
async function until(predicate, message, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const state = await snapshot();
    if (state && predicate(state)) return state;
    await page.waitForTimeout(120);
  }
  throw new Error(`${message}\n${JSON.stringify(await snapshot())}`);
}
const visible = e => e.enabled && e.width > 0 && e.height > 0 && e.x >= 0
  && e.x + e.width / 2 < size.width && e.y >= 0 && e.y + e.height < size.height - 45;
async function click(name) {
  const state = await until(s => s.elements.some(e => e.name === name && visible(e)), `Missing command: ${name}`);
  const item = state.elements.find(e => e.name === name && visible(e));
  await page.mouse.click(item.x + item.width / 2, item.y + item.height / 2);
  await page.waitForTimeout(250);
}
async function scrollTo(name) {
  // Real wheel events, not ScrollViewer mutation through a diagnostic hook.
  for (let i = 0; i < 15; i++) {
    const state = await snapshot(), item = state.elements.find(e => e.name === name);
    if (item && visible(item) && item.y > 220) return;
    await page.mouse.move(size.width - 100, 800);
    await page.mouse.wheel(0, item && item.y < 220 ? -360 : 360);
    await page.waitForTimeout(250);
  }
  throw new Error(`Cannot scroll to ${name}`);
}
async function enter(name, value) { await click(name); await page.keyboard.press('Control+a'); await page.keyboard.type(value); }
const screen = (s, p) => ({ x: s.canvasX + s.panX + p.x * s.zoom, y: s.canvasY + s.panY + p.y * s.zoom });
async function check(name, action) {
  const start = Date.now();
  try { await action(); results.push({ name, passed: true, milliseconds: Date.now() - start }); console.log(`PASS ${name}`); }
  catch (error) { results.push({ name, passed: false, milliseconds: Date.now() - start, error: error.stack }); throw error; }
}
try {
  await page.goto(base + (base.includes('?') ? '&' : '?') + 'test=1', { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13, 'Fresh Uno workspace did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 });
  await page.waitForTimeout(1200);
  await check('Capture a connected multi-shape selection as one reusable master', async () => {
    // Insert page is at the bottom outside the pane-safe helper viewport.
    const initial = await snapshot(), insertPage = initial.elements.find(e => e.name === 'Insert page');
    assert.ok(insertPage); await page.mouse.click(insertPage.x + insertPage.width / 2, insertPage.y + insertPage.height / 2);
    await until(s => s.nodes === 0 && s.pages === 2, 'Blank page not inserted');
    await click('Insert Process');
    let state = await until(s => s.nodes === 1 && s.selection === 1, 'First process not inserted');
    const first = state.shapes[0], east = screen(state, { x: first.x + first.width, y: first.y + first.height / 2 });
    await page.mouse.click(east.x + 27, east.y);
    await until(s => s.nodes === 2 && s.edges === 1, 'AutoConnect not created');
    await click('Select All'); await click('Developer'); await click('Masters');
    await click('Create master from selection'); await enter('Master name', 'Connected master'); await page.keyboard.press('Enter');
    state = await until(s => s.masters === 1, 'Selection master not created');
    assert.equal(state.nodes, 2); assert.equal(state.edges, 1);
    assert.ok(state.elements.some(e => e.name === 'Edit master component Process'));
  });
  await check('Inserting a captured master preserves grouped components and internal glue', async () => {
    await click('Insert master Connected master');
    const state = await until(s => s.nodes === 5 && s.edges === 2 && s.groups === 1, 'Master bundle was not inserted');
    const members = state.shapes.filter(s => s.masterId);
    assert.equal(members.length, 3);
    const ids = new Set(members.map(s => s.id));
    assert.ok(state.connectors.some(e => ids.has(e.sourceId) && ids.has(e.targetId)));
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-master-authoring.png' });
  });
  await check('Master explorer edits an inherited component without altering source shapes', async () => {
    await click('Edit master component Process'); await enter('Master text', 'Inherited component'); await click('Apply master text');
    const state = await until(s => s.shapes.some(n => n.masterId && n.text === 'Inherited component'), 'Master component update did not propagate');
    assert.equal(state.shapes.filter(s => !s.masterId && s.text === 'Process').length, 2);
    await click('Undo'); await until(s => s.shapes.every(n => n.text !== 'Inherited component'), 'Component change did not undo');
  });
  await check('Detach selected master graph retains geometry and undo restores inheritance', async () => {
    const before = await snapshot();
    await scrollTo('Detach selected master instance'); await click('Detach selected master instance');
    const state = await until(s => s.shapes.every(n => !n.masterId), 'Instance links were not detached');
    for (const shape of state.shapes) {
      const old = before.shapes.find(n => n.id === shape.id);
      assert.ok(old); for (const key of ['x', 'y', 'width', 'height', 'rotation']) assert.ok(Math.abs(old[key] - shape[key]) < .001);
    }
    assert.equal(state.edges, before.edges); assert.equal(state.groups, before.groups);
    await click('Undo'); await until(s => s.shapes.filter(n => n.masterId).length === 3, 'Detach undo did not restore inheritance');
  });
  await check('Authored connected stencil exports and imports through real browser file pickers', async () => {
    await click('File');
    const downloading = page.waitForEvent('download'); await click('VSSX'); const download = await downloading;
    assert.ok(download.suggestedFilename().endsWith('.vssx'));
    const file = await download.path(), bytes = await fs.readFile(file); assert.equal(bytes.subarray(0, 2).toString(), 'PK');
    const before = await snapshot(), choosing = page.waitForEvent('filechooser'); await click('Open');
    const chooser = await choosing; await chooser.setFiles({ name: 'connected-library.vssx', mimeType: 'application/vnd.ms-visio.stencil', buffer: bytes });
    const state = await until(s => s.masters === 2, 'Authored stencil did not import');
    assert.equal(state.nodes, before.nodes); assert.equal(state.edges, before.edges);
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-connected-stencil.png' });
  });
  assert.deepEqual(errors, [], 'Browser reported JavaScript or WebAssembly errors');
  console.log(`Validated ${results.length} master authoring scenarios at ${base}`);
} catch (error) {
  if (!results.some(r => !r.passed)) results.push({ name: 'Setup failure', passed: false, error: error.stack });
  console.error(error); process.exitCode = 1;
  await fs.writeFile('artifacts/master-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-master-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/master-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/master-console.log', messages.join('\n')); await browser.close();
}
