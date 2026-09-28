import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const results = [], errors = [], messages = [];
const size = { width: 1600, height: 1000 };
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage();
page.on('pageerror', error => { errors.push(error.message); messages.push(error.stack); });
page.on('console', message => messages.push(`${message.type()} ${message.text()}`));
page.setDefaultTimeout(30000);
// Diagnostics are observations only. All changes use pointer/keyboard/file-picker input.
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
function visible(e) {
  return e.enabled && e.width > 0 && e.height > 0 && e.x >= 0 && e.y >= 0
    && e.x + e.width / 2 < size.width && e.y + e.height / 2 < size.height;
}
async function click(name) {
  const state = await until(s => s.elements.some(e => e.name === name && visible(e)), `Missing command: ${name}`);
  const item = state.elements.find(e => e.name === name && visible(e));
  await page.mouse.click(item.x + item.width / 2, item.y + item.height / 2);
  await page.waitForTimeout(250);
}
const screen = (s, p) => ({ x: s.canvasX + s.panX + p.x * s.zoom, y: s.canvasY + s.panY + p.y * s.zoom });
const center = s => ({ x: s.x + s.width / 2, y: s.y + s.height / 2 });
async function clickWorld(p) {
  const q = screen(await snapshot(), p); await page.mouse.click(q.x, q.y); await page.waitForTimeout(250);
}
async function dragWorld(a, b) {
  const state = await snapshot(), from = screen(state, a), to = screen(state, b);
  // A deliberate new drag, not an accidental second click of the label-edit gesture.
  await page.waitForTimeout(650);
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 10 }); await page.mouse.up();
  await until(s => s.gesture === 'None' && !s.editingText, 'Drag did not finish');
}
async function allOperands() {
  await click('Home'); await click('Select All'); await until(s => s.selection === 2, 'The two operands were not selected');
  await click('Developer');
}
function sameGeometry(state, expected) {
  return state.nodes === expected.length && expected.every(a => {
    const b = state.shapes.find(s => s.id === a.id);
    return b && ['x', 'y', 'width', 'height', 'rotation'].every(k => Math.abs(a[k] - b[k]) < .005);
  });
}
async function check(name, action) {
  const start = Date.now();
  try { await action(); results.push({ name, passed: true, milliseconds: Date.now() - start }); console.log(`PASS ${name}`); }
  catch (error) { results.push({ name, passed: false, milliseconds: Date.now() - start, error: error.stack }); throw error; }
}
try {
  const url = new URL(base); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13, 'Fresh Uno workspace did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 });
  await page.waitForTimeout(1200);
  await click('Insert page'); await until(s => s.nodes === 0 && s.pages === 2, 'Blank page not inserted');
  await click('Insert Process');
  let state = await until(s => s.nodes === 1 && s.selection === 1, 'First operand not inserted');
  const firstId = state.shapes[0].id;
  await dragWorld(center(state.shapes[0]), { x: 240, y: 240 });
  await click('Insert Process');
  state = await until(s => s.nodes === 2 && s.selection === 1, 'Second operand not inserted');
  const second = state.shapes.find(s => s.id !== firstId);
  await dragWorld(center(second), { x: 312, y: 240 });
  state = await snapshot();
  const original = state.shapes.map(s => ({ ...s }));
  const a = original.find(s => s.id === firstId), b = original.find(s => s.id !== firstId);
  const left = Math.min(a.x, b.x), right = Math.max(a.x + a.width, b.x + b.width);
  const overlapLeft = Math.max(a.x, b.x), overlapRight = Math.min(a.x + a.width, b.x + b.width);
  assert.ok(overlapRight - overlapLeft > 20, 'Fixture operands must overlap');
  const undoOperands = async () => { await click('Undo'); return until(s => sameGeometry(s, original), 'Undo did not restore the original operands'); };

  await check('Union creates a single editable outline and supports exact undo/redo', async () => {
    await allOperands(); await click('Union');
    const united = await until(s => s.nodes === 1, 'Union did not replace the operands');
    assert.equal(united.shapes[0].id, firstId);
    assert.ok(Math.abs(united.shapes[0].width - (right - left)) < .1);
    const unionGeometry = united.shapes.map(s => ({ ...s }));
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-boolean-union.png' });
    await undoOperands(); await click('Redo');
    await until(s => sameGeometry(s, unionGeometry), 'Redo changed the union geometry');
    await undoOperands();
  });
  await check('Intersect keeps only the overlapping filled region', async () => {
    await allOperands(); await click('Intersect');
    const intersected = await until(s => s.nodes === 1, 'Intersection not produced');
    assert.ok(Math.abs(intersected.shapes[0].x - overlapLeft) < .1);
    assert.ok(Math.abs(intersected.shapes[0].width - (overlapRight - overlapLeft)) < .1);
    await undoOperands();
  });
  await check('Subtract keeps the default primary side and restores both operands on undo', async () => {
    await allOperands(); await click('Subtract');
    const subtracted = await until(s => s.nodes === 1, 'Subtraction not produced');
    assert.equal(subtracted.shapes[0].id, firstId);
    assert.ok(Math.abs(subtracted.shapes[0].x - a.x) < .1);
    assert.ok(Math.abs(subtracted.shapes[0].width - (overlapLeft - a.x)) < .1);
    await undoOperands();
  });
  await check('Combine leaves the overlap unfilled for actual canvas hit-testing', async () => {
    await allOperands(); await click('Combine'); await until(s => s.nodes === 1, 'Combine not produced');
    await clickWorld({ x: 60, y: 60 }); await until(s => s.selection === 0, 'Blank click did not clear selection');
    await clickWorld({ x: (overlapLeft + overlapRight) / 2, y: center(a).y });
    assert.equal((await snapshot()).selection, 0, 'The XOR hole was incorrectly selectable as filled area');
    await clickWorld({ x: a.x + 12, y: center(a).y });
    await until(s => s.selection === 1, 'The retained filled region was not selectable');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-boolean-combine.png' });
    await undoOperands();
  });
  await check('Create Path Copy keeps the original and creates a separately undoable outline', async () => {
    await clickWorld({ x: 60, y: 60 }); await clickWorld({ x: a.x + 12, y: center(a).y });
    await until(s => s.selection === 1 && s.shapes.some(n => n.id === firstId && n.selected), 'Primary not selected');
    await click('Create Path Copy');
    const copied = await until(s => s.nodes === 3, 'Path copy not inserted');
    const copy = copied.shapes.find(n => !original.some(o => o.id === n.id));
    assert.ok(copy && copy.name.endsWith(' path'));
    for (const key of ['x', 'y', 'width', 'height']) assert.ok(Math.abs(copy[key] - a[key]) < .1);
    await undoOperands();
  });
  await check('Empty intersection preserves the document and does not insert a spurious history entry', async () => {
    await clickWorld({ x: 60, y: 60 });
    const p = { x: b.x + b.width - 12, y: center(b).y };
    await clickWorld(p); await until(s => s.selection === 1 && s.shapes.some(n => n.id === b.id && n.selected), 'Second not selected');
    await dragWorld(p, { x: p.x + 300, y: p.y });
    const separated = (await snapshot()).shapes.map(s => ({ ...s }));
    await allOperands(); await click('Intersect');
    state = await until(s => s.status.includes('no filled area'), 'Empty-result notification missing');
    assert.ok(sameGeometry(state, separated));
    await undoOperands();
  });
  await check('Boolean geometry exports and imports through the real VSDX file picker', async () => {
    await allOperands(); await click('Union'); await until(s => s.nodes === 1, 'Final union not ready');
    await click('File'); const downloading = page.waitForEvent('download'); await click('VSDX');
    const download = await downloading;
    assert.ok(download.suggestedFilename().endsWith('.vsdx'));
    const bytes = await fs.readFile(await download.path()); assert.equal(bytes.subarray(0, 2).toString(), 'PK');
    // Save the native drawing before replacing it. Do not bypass the real unsaved-changes dialog.
    const saving = page.waitForEvent('download'); await click('Save'); await saving;
    await until(s => !s.dirty, 'Save did not mark the document revision');
    const choosing = page.waitForEvent('filechooser'); await click('Open');
    await (await choosing).setFiles({ name: 'boolean-roundtrip.vsdx', mimeType: 'application/vnd.ms-visio.drawing', buffer: bytes });
    await until(s => s.status.startsWith('Opened boolean-roundtrip.vsdx'), 'Boolean VSDX was not opened');
    // Loading starts on the first page; the authored Boolean result is on the second.
    await click('Page-2'); state = await until(s => s.nodes === 1, 'Authored page was not restored');
    assert.ok(Math.abs(state.shapes[0].width - (right - left)) < .1);
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-boolean-roundtrip.png' });
  });
  assert.deepEqual(errors, [], 'Browser reported JavaScript or WebAssembly errors');
  console.log(`Validated ${results.length} Boolean editing scenarios at ${base}`);
} catch (error) {
  if (!results.some(r => !r.passed)) results.push({ name: 'Setup or runtime failure', passed: false, error: error.stack });
  console.error(error); process.exitCode = 1;
  await fs.writeFile('artifacts/boolean-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-boolean-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/boolean-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/boolean-console.log', messages.join('\n')); await browser.close();
}
