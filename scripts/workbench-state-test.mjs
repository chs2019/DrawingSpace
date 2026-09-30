import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const size = { width: 1440, height: 1000 };
const results = [], errors = [], messages = [];
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, acceptDownloads: true });
const page = await context.newPage();
page.setDefaultTimeout(30000);
page.on('pageerror', e => errors.push(e.message));
page.on('console', e => messages.push(`${e.type()} ${e.text()}`));
const snapshot = () => page.evaluate(() => globalThis.drawingSpaceSnapshot);
async function until(predicate, message, timeout = 15000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const state = await snapshot();
    if (state && predicate(state)) return state;
    await page.waitForTimeout(100);
  }
  throw new Error(`${message}\n${JSON.stringify(await snapshot())}`);
}
async function click(name, scope = () => true) {
  let previous, observation, stable = 0, target;
  await until(s => {
    if (s.observation === observation) return false;
    observation = s.observation;
    const matches = s.elements.filter(e => e.name === name && e.enabled && e.width > 0 && e.height > 0
      && e.x >= 0 && e.y >= 0 && e.x + e.width / 2 < size.width && e.y + e.height / 2 < size.height && scope(e));
    if (matches.length !== 1) { previous = undefined; stable = 0; return false; }
    target = matches[0];
    const geometry = JSON.stringify([target.x, target.y, target.width, target.height]);
    stable = geometry === previous ? stable + 1 : 0; previous = geometry;
    return stable >= 2;
  }, `Missing, ambiguous or unsettled control: ${name}`);
  await page.mouse.click(target.x + target.width / 2, target.y + target.height / 2);
  await until(s => s.observation > observation, `No observation after clicking ${name}`);
}
async function ribbon(name) {
  await click(name);
  // A new timer publication is not proof that pointer release has rebuilt and
  // arranged the requested ribbon. Observe that tab's actual command geometry
  // and binding count across three different publications before assertions.
  const marker = { File: 'SVG', Home: 'Pointer Tool', Data: 'Color by Value', View: 'Ruler', Developer: 'ShapeSheet' }[name];
  assert.ok(marker, `No ribbon marker for ${name}`);
  let previous, observation, stable = 0;
  return until(s => {
    if (s.observation === observation) return false;
    observation = s.observation;
    const matches = s.elements.filter(e => e.name === marker && e.width > 0 && e.height > 0 && e.y >= 69 && e.y < s.canvasY);
    if (matches.length !== 1) { previous = undefined; stable = 0; return false; }
    const e = matches[0], state = JSON.stringify([e.x, e.y, e.width, e.height, s.commandBindings]);
    stable = state === previous ? stable + 1 : 1; previous = state;
    return stable >= 3;
  }, `Requested ${name} ribbon did not settle`);
}
async function enter(name, value) {
  await click(name); await page.keyboard.press('Control+a'); await page.keyboard.type(value); await page.keyboard.press('Enter');
}
function center(s, shape) {
  return { x: s.canvasX + s.panX + (shape.x + shape.width / 2) * s.zoom,
    y: s.canvasY + s.panY + (shape.y + shape.height / 2) * s.zoom };
}
async function select(id) {
  const s = await snapshot(), point = center(s, s.shapes.find(n => n.id === id));
  await page.mouse.click(point.x, point.y);
  return until(n => n.observation > s.observation && n.gesture === 'None' && n.selection === 1 && n.shapes.some(v => v.id === id && v.selected), 'Selection did not settle');
}
async function check(name, run) {
  const start = Date.now();
  try { await run(); results.push({ name, passed: true, milliseconds: Date.now() - start }); console.log(`PASS ${name}`); }
  catch (e) { results.push({ name, passed: false, error: e.stack }); throw e; }
}
let firstId, secondId;
try {
  const url = new URL(base); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13, 'Workspace did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden' });
  await page.waitForTimeout(1200);
  await click('Insert page'); await click('Insert Process');
  firstId = (await until(s => s.nodes === 1 && s.selection === 1, 'First fixture shape missing')).shapes[0].id;
  await click('Format Shape'); await enter('Position X', '120'); await enter('Position Y', '200');
  await click('Insert Process');
  secondId = (await until(s => s.nodes === 2 && s.shapes.some(n => n.selected && n.id !== firstId), 'Second fixture shape missing')).shapes.find(n => n.selected).id;
  await enter('Position X', '420'); await enter('Position Y', '200');
  await until(s => s.shapes.some(n => n.id === secondId && n.x === 420 && n.y === 200), 'Fixture geometry did not commit');

  await check('Same-sized selection changes rebuild the correct editors and preserve exact undo', async () => {
    const before = await snapshot();
    await select(firstId); await enter('Width', '180');
    const after = await until(s => s.shapes.find(n => n.id === firstId).width === 180, 'First shape width did not change');
    assert.equal(after.shapes.find(n => n.id === secondId).width, before.shapes.find(n => n.id === secondId).width);
    assert.ok(after.propertyRebuilds > before.propertyRebuilds);
    await click('Undo'); await until(s => s.shapes.find(n => n.id === firstId).width === before.shapes.find(n => n.id === firstId).width, 'Width undo failed');
  });
  await check('Zoom and tool changes do not recreate property panes or page tabs', async () => {
    const before = await snapshot();
    for (let i = 0; i < 4; i++) { await click('Zoom in'); await click('Zoom out'); }
    await click('Text'); await until(s => s.tool === 'Text', 'Text tool did not activate');
    await click('Pointer Tool');
    const after = await until(s => s.tool === 'Pointer', 'Pointer tool did not activate');
    assert.equal(after.propertyRebuilds, before.propertyRebuilds);
    assert.equal(after.pageStripRebuilds, before.pageStripRebuilds);
    assert.equal(after.revision, before.revision);
  });
  await check('Quick Access Save is uniquely targeted with File ribbon open and retains editors', async () => {
    const before = await snapshot(); const fileRibbon = await ribbon('File');
    assert.equal(fileRibbon.elements.filter(e => e.name === 'Save' && e.enabled).length, 2);
    const pending = page.waitForEvent('download');
    await click('Save', e => e.automationId === 'QuickAccess.Save'); await pending;
    const after = await until(s => !s.dirty, 'Save did not mark the current document');
    assert.equal(after.propertyRebuilds, before.propertyRebuilds);
    assert.equal(after.pageStripRebuilds, before.pageStripRebuilds);
  });
  await check('Repeated ribbon reconstruction keeps command registrations bounded', async () => {
    const before = await snapshot(), counts = {};
    const tabs = ['Home', 'Data', 'View', 'Developer', 'File'];
    for (let cycle = 0; cycle < 6; cycle++) {
      for (const tab of tabs) {
        const s = await ribbon(tab);
        if (cycle === 0) counts[tab] = s.commandBindings;
        else assert.equal(s.commandBindings, counts[tab], `Retired ${tab} bindings are still registered`);
      }
    }
    const after = await snapshot();
    assert.equal(after.propertyRebuilds, before.propertyRebuilds);
    assert.equal(after.pageStripRebuilds, before.pageStripRebuilds);
    await fs.writeFile('artifacts/workbench-binding-results.json', JSON.stringify(counts, null, 2));
  });
  await check('Recovery saves preserve foreground validation messages', async () => {
    await ribbon('Home'); await enter('Width', 'not-a-number');
    const before = await until(s => s.status.startsWith('Width must be between'), 'Invalid dimension was not reported');
    await ribbon('File'); await click('Save recovery copy');
    const after = await until(s => s.recoveryWrites > before.recoveryWrites && s.recoveryCurrent, 'Explicit recovery save did not complete');
    assert.equal(after.status, before.status);
    assert.equal(after.recoveryStatus, 'Recovery saved');
    assert.equal(after.recoveryRevision, after.revision);
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-recovery-status.png' });
  });
  await check('Page insertion and undo still update the retained page strip', async () => {
    const before = await snapshot(); await click('Insert page');
    const after = await until(s => s.pages === before.pages + 1 && s.nodes === 0, 'New page was not activated');
    assert.ok(after.pageStripRebuilds > before.pageStripRebuilds);
    await click('Undo'); await until(s => s.pages === before.pages && s.activePageId === before.activePageId && s.nodes === 2, 'Page undo did not restore active content');
  });
  await check('Recovery defers live gestures and persists only the cancelled gesture baseline', async () => {
    await ribbon('Home'); await click('Close task pane'); await select(secondId);
    await until(s => s.recoveryCurrent, 'Fixture recovery did not settle');
    const before = await snapshot();
    await page.keyboard.press('ArrowRight');
    const nudged = await until(s => s.shapes.find(n => n.id === secondId).x === before.shapes.find(n => n.id === secondId).x + 1, 'Nudge did not commit');
    const expected = nudged.shapes.find(n => n.id === secondId), point = center(nudged, expected);
    await page.mouse.move(point.x, point.y); await page.mouse.down(); await page.mouse.move(point.x + 50, point.y + 20, { steps: 4 });
    const active = await until(s => s.gesture !== 'None', 'Move gesture did not start');
    await page.waitForTimeout(1200);
    assert.equal((await snapshot()).recoveryWrites, active.recoveryWrites, 'A live preview was written to recovery');
    await page.keyboard.press('Escape'); await page.mouse.up();
    await until(s => s.gesture === 'None' && s.recoveryCurrent && s.shapes.find(n => n.id === secondId).x === expected.x, 'Cancelled gesture did not restore and save its baseline');
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 120000 });
    await until(s => s.ready && s.pages === 2, 'Recovery document did not load', 120000);
    await page.locator('.uno-loader').waitFor({ state: 'hidden' });
    await click('Page-2');
    await until(s => s.shapes.some(n => n.id === secondId && n.x === expected.x && n.y === expected.y), 'Recovery restored uncommitted preview geometry');
  });
  assert.deepEqual(errors, []);
  console.log(`Validated ${results.length} workbench-state scenarios at ${base}`);
} catch (e) {
  console.error(e); process.exitCode = 1;
  await fs.writeFile('artifacts/workbench-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-workbench-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/workbench-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/workbench-console.log', messages.join('\n')); await browser.close();
}
