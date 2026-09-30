import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { workbookFixture } from './xlsx-fixture.mjs';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const size = { width: 1600, height: 1200 };
const results = [], errors = [], messages = [];
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage(); page.setDefaultTimeout(30000);
page.on('pageerror', error => { errors.push(error.message); messages.push(error.stack); });
page.on('console', message => messages.push(`${message.type()} ${message.text()}`));
const snapshot = () => page.evaluate(() => globalThis.drawingSpaceSnapshot);
async function until(predicate, message, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const state = await snapshot(); if (state && predicate(state)) return state;
    await page.waitForTimeout(120);
  }
  throw new Error(`${message}\n${JSON.stringify(await snapshot())}`);
}
const visible = e => e.enabled && e.width > 0 && e.height > 0 && e.x >= 0 && e.y >= 0
  && e.x + e.width / 2 < size.width && e.y + e.height / 2 < size.height;
async function click(name) {
  const state = await until(s => s.elements.some(e => e.name === name && visible(e)), `Missing visible control: ${name}`);
  const e = state.elements.find(e => e.name === name && visible(e));
  await page.mouse.click(e.x + e.width / 2, e.y + e.height / 2); await page.waitForTimeout(250);
}
async function enter(name, value) { await click(name); await page.keyboard.press('Control+a'); await page.keyboard.type(value); }
async function check(name, action) {
  const start = Date.now();
  try { await action(); results.push({ name, passed: true, milliseconds: Date.now() - start }); console.log(`PASS ${name}`); }
  catch (error) { results.push({ name, passed: false, error: error.stack }); throw error; }
}
let id;
const asset = state => state.shapes.find(s => s.id === id);
const cell = (state, row, field, value) => state.elements.some(e => e.name === `Data cell ${row} ${field}: ${value}`);
async function label(value) {
  const state = await snapshot(), shape = asset(state);
  await page.mouse.dblclick(state.canvasX + state.panX + (shape.x + shape.width / 2) * state.zoom,
    state.canvasY + state.panY + (shape.y + shape.height / 2) * state.zoom);
  await until(s => s.editingText, 'Inline text editor did not open');
  await page.keyboard.press('Control+a'); await page.keyboard.type(value); await page.keyboard.press('Enter');
  await until(s => !s.editingText && asset(s).text === value, 'Edited label did not commit');
}
async function upload(progress, reversed = false) {
  const chooser = page.waitForEvent('filechooser'); await click('Open Excel');
  await (await chooser).setFiles({ name: 'assets.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: workbookFixture(progress, reversed) });
  await until(s => s.status.startsWith('Loaded workbook assets.xlsx'), 'Excel picker did not load workbook');
  await click('Excel worksheet'); await page.keyboard.press('Home'); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  await enter('Excel header row', '3'); await enter('Source identity', 'assets');
  await page.keyboard.press('Tab');
}
try {
  const url = new URL(base); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13, 'Uno application did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 }); await page.waitForTimeout(1200);
  await click('Insert page'); await click('Insert Process');
  const initial = await until(s => s.nodes === 1 && s.selection === 1, 'Fixture shape did not insert');
  id = initial.shapes[0].id; await page.waitForTimeout(650); await label('0001');
  await click('Data'); await click('Link Data');

  await check('Excel file picker selects a worksheet and explicit header row without editing the drawing', async () => {
    await upload(20); await click('Preview Refresh');
    const state = await until(s => s.status.startsWith('11 rows; 1 shapes'), 'Excel source preview was not produced');
    assert.equal(asset(state).dataSource, null); assert.equal(Object.keys(asset(state).data).length, 0);
    assert.ok(cell(state, 1, 'Id', '0001'));
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-excel-source.png' });
  });
  await check('Source filtering displays matching rows without changing the refresh plan', async () => {
    await enter('Data filter', 'alice'); await page.keyboard.press('Enter');
    const state = await until(s => cell(s, 1, 'Owner', 'Alice') && !s.elements.some(e => e.name.startsWith('Data cell 2 ')), 'Source filter did not restrict visible rows');
    assert.equal(asset(state).dataSource, null); await click('Clear data view');
    await until(s => cell(s, 2, 'Id', '10'), 'Source filter did not clear');
  });
  await check('Numeric header sorting is stable and does not reorder source identities', async () => {
    await click('Numeric data sort'); await click('Sort data column Progress');
    await until(s => cell(s, 1, 'Id', '10') && cell(s, 1, 'Progress', '2'), 'Ascending numeric sort failed');
    await click('Sort data column Progress');
    await until(s => cell(s, 1, 'Id', '11') && cell(s, 1, 'Progress', '100'), 'Descending numeric sort failed');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-source-sorting.png' });
  });
  await check('Applying the sorted preview links by the original leading-zero key and supports undo', async () => {
    await click('Apply Refresh');
    await until(s => asset(s).dataRowKey === '0001' && asset(s).data.Progress === '20', 'Sorted display changed the source link');
    await click('Undo'); await until(s => asset(s).dataSource === null, 'Excel data link did not undo');
    await click('Redo'); await until(s => asset(s).data.Progress === '20', 'Excel data link did not redo');
  });
  await check('Reordered Excel rows and a renamed shape refresh using the saved key and formula cache', async () => {
    await label('Renamed equipment'); await upload(65, true); await click('Preview Refresh'); await click('Apply Refresh');
    await until(s => s.nodes === 1 && asset(s).dataSource === 'assets' && asset(s).data.Progress === '65'
      && asset(s).dataRowKey === '0001' && asset(s).text === 'Renamed equipment', 'Stable-key Excel refresh failed');
  });
  await check('Repeated Excel preview and refresh are no-op operations', async () => {
    await click('Preview Refresh'); await until(s => s.status.startsWith('11 rows; 0 shapes'), 'Unchanged workbook was not a no-op');
    await click('Apply Refresh'); await until(s => s.status === 'No data changes required.', 'No-op Excel refresh changed data');
  });
  await check('Excel-linked values drive the existing vector data-graphic renderer and SVG export', async () => {
    await click('Data Bars'); await click('Apply Data Graphic');
    await until(s => asset(s).dataGraphics.includes('DataBar') && asset(s).data.Progress === '65', 'Data bar was not driven by Excel data');
    await click('File'); const pending = page.waitForEvent('download'); await click('SVG');
    const svg = await fs.readFile(await (await pending).path(), 'utf8');
    assert.match(svg, /data-graphics-for=/); assert.match(svg, /Renamed equipment/);
  });
  await check('Native saves preserve Excel row identity and accepted refresh baseline', async () => {
    const pending = page.waitForEvent('download'); await click('Save');
    const document = JSON.parse(await fs.readFile(await (await pending).path(), 'utf8'));
    const shape = document.pages.flatMap(p => p.shapes).find(s => s.id === id);
    assert.equal(shape.dataBinding.sourceId, 'assets'); assert.equal(shape.dataBinding.rowKey, '0001');
    assert.equal(shape.dataBinding.baseline.Progress, '65'); assert.equal(shape.data.Progress, '65');
    assert.equal(shape.dataGraphics[0].kind, 'DataBar');
  });
  assert.deepEqual(errors, []);
  console.log(`Validated ${results.length} Excel/source-view scenarios at ${base}`);
} catch (error) {
  console.error(error); process.exitCode = 1;
  await fs.writeFile('artifacts/excel-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-excel-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/excel-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/excel-console.log', messages.join('\n')); await browser.close();
}
