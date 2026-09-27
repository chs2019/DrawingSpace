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
  try {
    await action();
    results.push({ name, passed: true, milliseconds: Date.now() - started });
    console.log(`PASS ${name}`);
  } catch (error) {
    results.push({ name, passed: false, milliseconds: Date.now() - started, error: error.stack });
    console.error(`FAIL ${name}: ${error.message}`);
    await fs.writeFile(`${output}/failure-${results.length}.json`, JSON.stringify(await snapshot().catch(() => null), null, 2));
    await page.screenshot({ path: `${output}/screenshots/failure-${results.length}.png` }).catch(() => {});
    // Keep later independent scenarios observable; the aggregate assertion still fails the run.
    await page.keyboard.up('Alt'); await page.keyboard.up('Shift'); await page.mouse.up();
  }
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
      return [...document.querySelectorAll('[aria-label]')].map(node => node.getAttribute('aria-label')).concat([...document.querySelectorAll('text')].map(node => {
        const spans = [...node.querySelectorAll('tspan')];
        return spans.length ? spans.map(span => span.textContent).join(' ') : node.textContent;
      }));
    }, text);
    assert.ok(labels.includes('Ready for review'), 'The exported SVG lost the edited label');
  });
  await check('Local recovery survives browser reload', async () => {
    await until(s => s.status.includes('Exported') || s.status.includes('saved'), 'No completed operation status');
    await page.waitForTimeout(1200); await page.reload({ waitUntil: 'domcontentloaded', timeout: 120000 });
    await until(s => s.ready && s.nodes === 14 && s.shapes.some(n => n.text === 'Ready for review'), 'Recovery did not restore the edited drawing', 120000);
    await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 30000 });
    await page.waitForTimeout(300); // Allow the read-only geometry snapshot to observe the final fitted viewport.
  });

  async function enter(name, value) { await click(name); await page.keyboard.press('Control+a'); await page.keyboard.type(value); }
  async function selectLabel(label) {
    const state = await snapshot(); const shape = state.shapes.find(s => s.text === label); assert.ok(shape, `Missing shape ${label}`);
    const point = center(state, shape); await page.mouse.click(point.x, point.y);
    return await until(s => s.shapes.some(n => n.id === shape.id && n.selected), 'Could not select shape');
  }
  function screen(state, point) { return { x: state.canvasX + state.panX + point.x * state.zoom, y: state.canvasY + state.panY + point.y * state.zoom }; }
  async function drag(a, b) { await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, {steps: 10}); await page.mouse.up(); }
  await check('ShapeSheet pane recalculates geometry and supports exact undo', async () => {
    await selectLabel('Ready for review'); const original = (await snapshot()).shapes.find(s => s.selected);
    await click('Developer'); await click('ShapeSheet'); await enter('Cell formula', '3 in'); await click('Apply formula');
    await until(s => s.shapes.some(n => n.id === original.id && Math.abs(n.width - 288) < .001), 'Formula did not resize the selected shape');
    await page.screenshot({path: `${output}/screenshots/DrawingSpace-shapesheet.png`});
    await click('Undo'); await until(s => s.shapes.some(n => n.id === original.id && Math.abs(n.width - original.width) < .001), 'Formula undo lost original size');
    await click('Close task pane');
  });
  await check('Rich text pane applies a selected range rather than whole-shape formatting', async () => {
    await selectLabel('Ready for review'); await click('Rich Text'); await click('Rich text content'); await page.keyboard.press('Control+a');
    await click('Range bold'); await until(s => s.shapes.some(n => n.text === 'Ready for review' && n.rangeBold), 'Selected range was not formatted');
    await page.screenshot({path: `${output}/screenshots/DrawingSpace-rich-text.png`}); await click('Close task pane');
  });
  await check('A real master can be created, inserted and undone through the UI', async () => {
    await selectLabel('Ready for review'); await click('Masters'); await click('Create master from shape');
    await enter('Master name', 'Approval master'); await page.keyboard.press('Enter');
    await until(s => s.masters === 1, 'Master creation did not commit');
    const count = (await snapshot()).nodes; await click('Insert master Approval master');
    await until(s => s.nodes === count + 1 && s.shapes.some(n => n.selected && n.masterId), 'Master did not insert');
    await click('Undo'); await until(s => s.nodes === count && s.masters === 1, 'Master insertion did not undo independently');
    await click('Close task pane');
  });
  await check('Semantic container movement preserves its members and undo', async () => {
    const initial = await selectLabel('Ready for review'); const child = initial.shapes.find(s => s.selected);
    await click('Containers'); await click('Container around selection');
    const state = await until(s => s.shapes.some(n => n.id === child.id && n.containerId), 'Container membership was not assigned');
    const container = state.shapes.find(s => s.selected); assert.ok(container);
    const a = screen(state, {x:container.x + container.width / 2, y:container.y + 12});
    await drag(a, {x:a.x + 45, y:a.y + 30});
    await until(s => s.shapes.some(n => n.id === child.id && n.x !== child.x), 'Container drag did not move its member');
    await click('Undo'); await until(s => s.shapes.some(n => n.id === child.id && Math.abs(n.x - child.x) < .001), 'Container movement undo failed');
    await click('Undo'); await until(s => !s.shapes.find(n => n.id === child.id)?.containerId, 'Container insertion undo failed');
    await click('Close task pane');
  });
  await check('Connector endpoint can be detached with pointer drag and restored by undo', async () => {
    const state = await snapshot(); const edge = state.connectors[0]; assert.ok(edge?.route?.length >= 2);
    const p = {x:(edge.route[0].x + edge.route[1].x)/2,y:(edge.route[0].y + edge.route[1].y)/2}; const hit = screen(state,p);
    await page.mouse.click(hit.x,hit.y); const selected = await until(s => s.connectors.some(c => c.id === edge.id && c.selected), 'Connector was not selected');
    const last = edge.route.at(-1); const a = screen(selected,last); const b = screen(selected,{x:900,y:650}); await drag(a,b);
    await until(s => s.connectors.some(c => c.id === edge.id && c.targetId === null), 'Endpoint stayed attached after drag');
    await click('Undo'); await until(s => s.connectors.some(c => c.id === edge.id && c.targetId === edge.targetId), 'Endpoint undo did not restore attachment');
  });
  await check('Visible segment grips insert and move waypoints without a keyboard modifier', async () => {
    const state = await snapshot(); const edge = state.connectors.find(c => c.selected); assert.ok(edge);
    const p = {x:(edge.route[0].x + edge.route[1].x)/2,y:(edge.route[0].y + edge.route[1].y)/2};
    const a = screen(state,p); await drag(a,{x:a.x+45,y:a.y+12});
    await until(s => s.connectors.some(c => c.id === edge.id && c.waypointCount > 0), 'Segment grip did not insert a waypoint');
    await click('Undo'); await until(s => s.connectors.some(c => c.id === edge.id && c.waypointCount === 0), 'Waypoint insertion did not undo');
  });
  await check('Alt-drag inserts a route waypoint and it can be removed with Shift-click', async () => {
    const state = await snapshot(); const edge = state.connectors.find(c => c.selected); assert.ok(edge);
    const p = {x:(edge.route[0].x + edge.route[1].x)/2,y:(edge.route[0].y + edge.route[1].y)/2}; const a = screen(state,p);
    await page.keyboard.down('Alt');
    try { await drag(a,{x:a.x+45,y:a.y+12}); }
    finally { await page.keyboard.up('Alt'); }
    const after = await until(s => s.connectors.some(c => c.id === edge.id && c.waypointCount > 0), 'Waypoint was not inserted');
    const point = screen(after,after.connectors.find(c => c.id === edge.id).waypoints[0]);
    await page.keyboard.down('Shift'); await page.mouse.click(point.x,point.y); await page.keyboard.up('Shift');
    await until(s => s.connectors.some(c => c.id === edge.id && c.waypointCount === 0), 'Waypoint was not removed');
  });
  await check('VSDX export and binary file-picker import round-trip the live drawing', async () => {
    await click('File'); const downloadPromise = page.waitForEvent('download'); await click('VSDX'); const download = await downloadPromise;
    const bytes = await fs.readFile(await download.path()); assert.equal(bytes[0],0x50); assert.equal(bytes[1],0x4b);
    const saved = page.waitForEvent('download'); await click('Save'); await saved; await until(s => !s.dirty, 'Save did not mark the current revision');
    const count = (await snapshot()).nodes; const chooserPromise = page.waitForEvent('filechooser'); await click('Open');
    const chooser = await chooserPromise; await chooser.setFiles({name:'roundtrip.vsdx',mimeType:'application/vnd.ms-visio.drawing',buffer:bytes});
    await until(s => s.nodes === count && s.status.startsWith('Opened roundtrip.vsdx'), 'Binary Visio file did not open');
    assert.ok((await snapshot()).shapes.some(s => s.text === 'Ready for review'));
    await page.screenshot({path: `${output}/screenshots/DrawingSpace-visio-import.png`});
  });
  await check('VSSX import adds masters without replacing the current drawing', async () => {
    const state = await snapshot(); const pending = page.waitForEvent('download'); await click('VSSX'); const download = await pending; const bytes = await fs.readFile(await download.path());
    const chooserPromise = page.waitForEvent('filechooser'); await click('Open'); const chooser = await chooserPromise;
    await chooser.setFiles({name:'library.vssx',mimeType:'application/vnd.ms-visio.stencil',buffer:bytes});
    await until(s => s.masters > state.masters && s.nodes === state.nodes, 'Stencil library did not merge into the drawing');
    await page.screenshot({path: `${output}/screenshots/DrawingSpace-master-library.png`});
  });
  assert.ok(results.every(result => result.passed), 'One or more browser scenarios failed');
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
