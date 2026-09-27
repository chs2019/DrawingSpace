import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const base = process.env.DRAWINGSPACE_URL ?? 'http://127.0.0.1:4173/DrawingSpace/';
const results = [], errors = [], messages = [];
await fs.mkdir('artifacts/screenshots', { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1 });
const page = await context.newPage();
page.on('pageerror', error => { errors.push(error.message); messages.push(error.stack); });
page.on('console', message => messages.push(`${message.type()} ${message.text()}`));
page.setDefaultTimeout(30000);
// Read-only observations of actual Uno state. Every mutation below is pointer or keyboard input.
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
async function click(name) {
  const visible = e => e.name === name && e.enabled && e.width > 0 && e.height > 0
    && e.x >= 0 && e.x + e.width / 2 < 1440 && e.y >= 0 && e.y + e.height / 2 < 1000;
  const state = await until(s => s.elements.some(visible), `Missing command ${name}`);
  const item = state.elements.find(visible);
  await page.mouse.click(item.x + item.width / 2, item.y + item.height / 2);
  await page.waitForTimeout(220);
}
async function selectAllObjects() {
  // Escape intentionally clears the selection after cancelling a gesture. Establish
  // this precondition through the actual command instead of relying on previous tests.
  await click('Select All');
  return until(s => s.nodes === 2 && s.edges === 1 && s.selection === 3
    && s.shapes.every(n => n.selected) && s.connectors[0].selected,
  'Select All did not establish the complete selection');
}
const screen = (s, p) => ({ x: s.canvasX + s.panX + p.x * s.zoom, y: s.canvasY + s.panY + p.y * s.zoom });
const center = shape => ({ x: shape.x + shape.width / 2, y: shape.y + shape.height / 2 });
function bounds(shapes) {
  const left = Math.min(...shapes.map(s => s.x)), right = Math.max(...shapes.map(s => s.x + s.width));
  const top = Math.min(...shapes.map(s => s.y)), bottom = Math.max(...shapes.map(s => s.y + s.height));
  return { left, right, top, bottom, width: right - left, height: bottom - top, x: (left + right) / 2, y: (top + bottom) / 2 };
}
function equalGeometry(state, original) {
  return original.every(a => {
    const b = state.shapes.find(s => s.id === a.id);
    return b && ['x', 'y', 'width', 'height', 'rotation'].every(k => Math.abs(a[k] - b[k]) < .005);
  });
}
async function drag(a, b, expectedGesture) {
  await page.mouse.move(a.x, a.y); await page.mouse.down();
  if (expectedGesture) await until(s => s.gesture === expectedGesture, `Expected ${expectedGesture} on pointer press`);
  await page.mouse.move(b.x, b.y, { steps: 10 }); await page.mouse.up();
  await until(s => s.gesture === 'None', 'Pointer release did not finish the gesture');
}
async function check(name, action) {
  const start = Date.now();
  try {
    await action(); results.push({ name, passed: true, milliseconds: Date.now() - start });
    console.log(`PASS ${name}`);
  } catch (error) {
    results.push({ name, passed: false, milliseconds: Date.now() - start, error: error.stack }); throw error;
  }
}
try {
  await page.goto(base + (base.includes('?') ? '&' : '?') + 'test=1', { waitUntil: 'domcontentloaded', timeout: 120000 });
  await until(s => s.ready && s.nodes === 13 && s.canvasWidth > 500, 'Fresh Uno application did not initialize', 120000);
  await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 });
  await page.waitForTimeout(1200);
  await check('Create two connected shapes through stencil and AutoConnect controls', async () => {
    await click('Insert page'); await until(s => s.nodes === 0 && s.pages === 2, 'Blank page was not inserted');
    await click('Insert Process');
    let state = await until(s => s.nodes === 1 && s.selection === 1, 'Stencil insertion failed');
    await drag(screen(state, center(state.shapes[0])), screen(state, { x: 220, y: 220 }));
    state = await snapshot(); const a = state.shapes[0];
    const east = screen(state, { x: a.x + a.width, y: a.y + a.height / 2 });
    await page.mouse.click(east.x + 27, east.y);
    state = await until(s => s.nodes === 2 && s.edges === 1, 'AutoConnect did not create a shape and connector');
    // The previous operation returns focus to the canvas; Ctrl+A selects real objects.
    await page.keyboard.press('Control+a');
    await until(s => s.shapes.every(n => n.selected) && s.connectors[0].selected, 'Select-all did not include both shapes and their edge');
  });

  const original = (await snapshot()).shapes.map(s => ({ ...s }));
  await check('Shared side grip resizes both shapes and retains glued endpoints', async () => {
    const state = await selectAllObjects(), box = bounds(state.shapes);
    const a = screen(state, { x: box.right, y: box.y });
    await drag(a, { x: a.x + 70, y: a.y }, 'SelectionResize');
    const changed = await until(s => s.shapes.every(n => n.width > original.find(o => o.id === n.id).width + 5), 'Shared resize did not affect every selected shape');
    const scale = (box.width + 70 / state.zoom) / box.width;
    for (const before of original) {
      const after = changed.shapes.find(n => n.id === before.id);
      assert.ok(Math.abs(after.width - before.width * scale) < .2);
      assert.ok(Math.abs(after.height - before.height) < .2);
    }
    assert.equal(changed.connectors[0].sourceId, state.connectors[0].sourceId);
    assert.equal(changed.connectors[0].targetId, state.connectors[0].targetId);
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-selection-resize.png' });
    await click('Undo'); await until(s => equalGeometry(s, original), 'Resize undo did not restore all geometry');
  });
  await check('Shared rotation grip rotates the selection as a unit and supports undo', async () => {
    const state = await selectAllObjects(), box = bounds(state.shapes), pivot = screen(state, box);
    const top = screen(state, { x: box.x, y: box.top }), a = { x: top.x, y: top.y - 25 };
    const radians = Math.PI / 6, dx = a.x - pivot.x, dy = a.y - pivot.y;
    const b = { x: pivot.x + dx * Math.cos(radians) - dy * Math.sin(radians), y: pivot.y + dx * Math.sin(radians) + dy * Math.cos(radians) };
    await drag(a, b, 'SelectionRotate');
    await until(s => s.shapes.every(n => Math.abs(n.rotation - 30) < .5), 'The selection did not rotate by the pointer angle');
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-selection-rotation.png' });
    await click('Undo'); await until(s => equalGeometry(s, original), 'Rotation undo did not restore all shapes');
  });
  await check('Escape rolls back an in-progress selection resize and clears selection', async () => {
    const state = await selectAllObjects(), box = bounds(state.shapes);
    const a = screen(state, { x: box.right, y: box.y });
    await page.mouse.move(a.x, a.y); await page.mouse.down();
    await until(s => s.gesture === 'SelectionResize', 'Selection resize did not begin');
    await page.mouse.move(a.x + 40, a.y, { steps: 5 });
    await until(s => !equalGeometry(s, original), 'Interactive geometry preview was not applied');
    await page.keyboard.press('Escape'); await page.mouse.up();
    await until(s => s.gesture === 'None' && s.selection === 0 && equalGeometry(s, original), 'Escape failed to restore the document and clear selection');
  });
  await check('Corner grip preserves the whole-selection aspect ratio by default', async () => {
    const state = await selectAllObjects(), box = bounds(state.shapes);
    const a = screen(state, { x: box.right, y: box.bottom });
    await drag(a, { x: a.x + 44, y: a.y + 9 }, 'SelectionResize');
    const changed = await until(s => s.shapes[0].width > original[0].width + 1, 'Corner resize did not change geometry');
    for (const after of changed.shapes) {
      const before = original.find(s => s.id === after.id);
      assert.ok(Math.abs(after.width / after.height - before.width / before.height) < .001, 'Corner resize changed an aspect ratio');
    }
    await click('Undo'); await until(s => equalGeometry(s, original), 'Corner resize did not undo');
  });
  await check('Orthogonal segment drag creates doglegs without detaching glued endpoints', async () => {
    let state = await snapshot(); const edge = state.connectors[0];
    const midpoint = { x: (edge.route[0].x + edge.route[1].x) / 2, y: (edge.route[0].y + edge.route[1].y) / 2 };
    const hit = screen(state, midpoint); await page.mouse.click(hit.x, hit.y);
    state = await until(s => s.selection === 1 && s.connectors[0].selected, 'Connector was not selected on its own');
    const a = screen(state, midpoint);
    await drag(a, { x: a.x, y: a.y + 55 }, 'ConnectorSegment');
    const changed = await until(s => s.connectors[0].waypointCount >= 2, 'Segment drag did not create two controlled bend points');
    const moved = changed.connectors[0];
    assert.equal(moved.sourceId, edge.sourceId); assert.equal(moved.targetId, edge.targetId);
    assert.ok(Math.hypot(moved.route[0].x - edge.route[0].x, moved.route[0].y - edge.route[0].y) < .01);
    assert.ok(Math.hypot(moved.route.at(-1).x - edge.route.at(-1).x, moved.route.at(-1).y - edge.route.at(-1).y) < .01);
    await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-segment-drag.png' });
    await click('Undo'); await until(s => s.connectors[0].waypointCount === 0, 'Segment drag was not one undoable operation');
  });
  await check('Alt-drag retains explicit single-waypoint insertion', async () => {
    const state = await snapshot(), edge = state.connectors[0];
    const midpoint = { x: (edge.route[0].x + edge.route[1].x) / 2, y: (edge.route[0].y + edge.route[1].y) / 2 };
    const a = screen(state, midpoint);
    await page.keyboard.down('Alt');
    try { await drag(a, { x: a.x + 24, y: a.y + 38 }, 'ConnectorWaypoint'); }
    finally { await page.keyboard.up('Alt'); }
    await until(s => s.connectors[0].waypointCount === 1, 'Alt-drag did not preserve single-waypoint semantics');
    await click('Undo'); await until(s => s.connectors[0].waypointCount === 0, 'Waypoint insertion undo failed');
  });
  assert.deepEqual(errors, [], 'JavaScript or WebAssembly errors were reported');
  console.log(`Validated ${results.length} advanced editing scenarios at ${base}`);
} catch (error) {
  if (!results.some(r => !r.passed)) results.push({ name: 'Setup or runtime failure', passed: false, error: error.stack });
  console.error(error); process.exitCode = 1;
  await fs.writeFile('artifacts/advanced-snapshot.json', JSON.stringify(await snapshot().catch(() => null), null, 2));
  await page.screenshot({ path: 'artifacts/screenshots/DrawingSpace-advanced-failure.png' }).catch(() => {});
} finally {
  await fs.writeFile('artifacts/advanced-results.json', JSON.stringify({ url: base, results, errors }, null, 2));
  await fs.writeFile('artifacts/advanced-console.log', messages.join('\n'));
  await browser.close();
}
