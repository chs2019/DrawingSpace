import { spawn } from 'node:child_process';

// Run all suites even when one fails; any failure keeps the workflow failed.
let failed = false;
for (const script of ['scripts/browser-test.mjs', 'scripts/advanced-editing-test.mjs', 'scripts/master-authoring-test.mjs', 'scripts/boolean-test.mjs', 'scripts/data-features-test.mjs', 'scripts/excel-data-test.mjs']) {
  const code = await new Promise(resolve => {
    const child = spawn(process.execPath, [script], { stdio: 'inherit', env: process.env });
    child.once('error', error => { console.error(error); resolve(1); });
    child.once('exit', code => resolve(code ?? 1));
  });
  if (code !== 0) failed = true;
}
process.exitCode = failed ? 1 : 0;
