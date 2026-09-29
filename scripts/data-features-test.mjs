import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const results = [], errors = [], messages = [];
const size = { width: 1600, height: 1050 };
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage();
page.on('pageerror', e => { errors.push(e.message); messages.push(e.stack); });
page.on('console', m => messages.push(`${m.type()} ${m.text()}`));
page.setDefaultTimeout(30000);
const snapshot = () => page.evaluate(() => globalThis.drawingSpaceSnapshot);
async function until(predicate, message, timeout = 15000) {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const s = await snapshot();
    if (s && predicate(s)) return s;
    await page.waitForTimeout(120);
  }
  throw new Error(`${message}\n${JSON.stringify(await snapshot())}`);
}
const visible = e => e.enabled && e.width > 0 && e.height > 0 && e.x >= 0 && e.y >= 0
  && e.x + e.width / 2 < size.width && e.y + e.height / 2 < size.height;
async function click(name) {
  const s = await until(s => s.elements.some(e => e.name === name && visible(e)), `Missing visible control: ${name}`);
  const e = s.elements.find(e => e.name === name && visible(e));
  await page.mouse.click(e.x + e.width / 2, e.y + e.height / 2);
  await page.waitForTimeout(250);
}
async function enter(name, value) {
  await click(name); await page.keyboard.press('Control+a'); await page.keyboard.type(value);
}
async function upload(progress) {
  const csv = `Id,Progress,Owner,Status\n0001,${progress},Alice,On\n`
    + Array.from({ length: 10 }, (_, i) => `${i + 10},90,Bob,On`).join('\n');
  const chooser = page.waitForEvent('filechooser'); await click('Open CSV');
  await (await chooser).setFiles({ name: 'assets.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
  await until(s => s.status.startsWith('Loaded data source assets.csv'), 'CSV file did not load');
}
async function check(name, action) {
  const start = Date.now();
  try { await action(); results.push({ name, passed: true, milliseconds: Date.now() - start }); console.log(`PASS ${name}`); }
  catch (error) { results.push({ name, passed: false, error: error.stack }); throw error; }
}
let id;
const asset = s => s.shapes.find(n => n.id === id);
try {
  const url = new URL(base); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13, 'Uno application did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 });
  await page.waitForTimeout(1200);
  await click('Insert page'); await click('Insert Process');
  let s = await until(s => s.nodes === 1 && s.selection === 1, 'Fixture shape not inserted');
  id = s.shapes[0].id;
  const a = asset(s);
  await page.waitForTimeout(650);
  await page.mouse.dblclick(s.canvasX + s.panX + (a.x + a.width / 2) * s.zoom,
    s.canvasY + s.panY + (a.y + a.height / 2) * s.zoom);
  await until(s => s.editingText, 'Inline label editor not opened');
  await page.keyboard.press('Control+a'); await page.keyboard.type('0001'); await page.keyboard.press('Enter');
  await until(s => asset(s).text === '0001' && !s.editingText, 'Stable key label not saved');
  await click('Data');

  await check('Actual CSV picker and bounded table preview do not mutate the drawing', async () => {
    await upload('20'); await click('Preview Refresh');
    const s = await until(s => s.status.startsWith('11 rows; 1 shapes'), 'Refresh preview not calculated');
    assert.equal(asset(s).dataSource, null); assert.equal(Object.keys(asset(s).data).length, 0);
    await click('Next data rows'); await click('Previous data rows');
    await click('Next data columns'); await click('Previous data columns');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-external-data.png' });
  });
  await check('Data refresh links by a leading-zero key and supports undo and redo', async () => {
    await click('Apply Refresh');
    await until(s => asset(s).data.Progress === '20' && asset(s).dataRowKey === '0001', 'Data link was not applied');
    await click('Undo'); await until(s => asset(s).dataSource === null, 'Data refresh did not undo');
    await click('Redo'); await until(s => asset(s).data.Progress === '20', 'Data refresh did not redo');
  });
  await check('Refresh updates an unchanged imported value without duplicating shapes', async () => {
    await upload('60'); await click('Preview Refresh'); await click('Apply Refresh');
    await until(s => s.nodes === 1 && asset(s).data.Progress === '60', 'Refresh did not update keyed row');
  });
  await check('A local data edit is preserved until overwrite is explicitly selected', async () => {
    await click('Shape Data'); await enter('Data Progress', '42'); await page.keyboard.press('Tab');
    await until(s => asset(s).data.Progress === '42', 'Local property editor did not commit');
    await click('Link Data'); await upload('90'); await click('Preview Refresh');
    await until(s => s.status.includes('1 conflicts'), 'Local/source conflict was not reported');
    await click('Apply Refresh'); assert.equal(asset(await snapshot()).data.Progress, '42');
    await click('Overwrite local conflicts'); await click('Preview Refresh'); await click('Apply Refresh');
    await until(s => asset(s).data.Progress === '90', 'Explicit overwrite did not apply');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-data-refresh.png' });
  });
  await check('All four data-graphic families are applied through the Data ribbon', async () => {
    for (const [button, kind] of [['Color by Value', 'ColorByValue'], ['Data Bars', 'DataBar'],
      ['Icon Sets', 'IconSet'], ['Text Callouts', 'TextCallout']]) {
      await click(button);
      if (kind === 'TextCallout') await enter('Graphic field', 'Owner');
      await click('Apply Data Graphic');
      await until(s => asset(s).dataGraphics.includes(kind), `Missing ${kind} graphic`);
    }
    const a = asset(await snapshot());
    assert.equal(a.dataGraphics.length, 4); assert.equal(a.effectiveFill, '#107C10'); assert.equal(a.baseFill, '#FFFFFF');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-data-graphics.png' });
  });
  await check('SVG export includes evaluated graphics and shaped text labels', async () => {
    await click('File'); const pending = page.waitForEvent('download'); await click('SVG');
    const text = await fs.readFile(await (await pending).path(), 'utf8');
    assert.match(text, /data-graphics-for=/); assert.match(text, /#107C10/); assert.match(text, /Owner: Alice/);
  });
  await check('Native save and file-picker reopen retain links, baseline and all graphics', async () => {
    const pending = page.waitForEvent('download'); await click('Save');
    const bytes = await fs.readFile(await (await pending).path());
    await until(s => !s.dirty, 'Save did not mark the current revision');
    const document = JSON.parse(bytes.toString('utf8'));
    const shape = document.pages.flatMap(p => p.shapes).find(s => s.id === id);
    assert.equal(shape.dataBinding.baseline.Progress, '90'); assert.equal(shape.dataGraphics.length, 4);
    const chooser = page.waitForEvent('filechooser'); await click('Open');
    await (await chooser).setFiles({ name: 'data.drawingspace.json', mimeType: 'application/json', buffer: bytes });
    // Native open selects the first foreground page. Navigate to the authored second page.
    await until(s => s.status.startsWith('Opened data.drawingspace.json'), 'Native data drawing did not reopen');
    await click('Page-2');
    await until(s => asset(s)?.dataGraphics.length === 4 && asset(s)?.dataRowKey === '0001', 'Data features lost on reopen');
  });
  await check('Removing graphics and unlinking preserve data and are undoable', async () => {
    const s = await snapshot(), a = asset(s);
    await page.mouse.click(s.canvasX + s.panX + (a.x + a.width / 2) * s.zoom,
      s.canvasY + s.panY + (a.y + a.height / 2) * s.zoom);
    await until(s => asset(s).selected, 'Reopened shape could not be selected');
    await click('Data'); await click('Color by Value'); await click('Remove Data Graphics');
    await until(s => asset(s).dataGraphics.length === 0 && asset(s).effectiveFill === asset(s).baseFill, 'Base fill not restored');
    await click('Undo'); await until(s => asset(s).dataGraphics.length === 4, 'Graphic removal did not undo');
    await click('Link Data'); await click('Unlink Data');
    await until(s => asset(s).dataSource === null && asset(s).data.Progress === '90', 'Unlink removed data values');
    await click('Undo'); await until(s => asset(s).dataSource === 'Assets', 'Unlink did not undo');
  });
  assert.deepEqual(errors, []);
  console.log(`Validated ${results.length} data-feature scenarios at ${base}`);
} catch (error) {
  console.error(error); process.exitCode = 1;
  await fs.writeFile('artifacts/data-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-data-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/data-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/data-console.log', messages.join('\n'));
  await browser.close();
}
