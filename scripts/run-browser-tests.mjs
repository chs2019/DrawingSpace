import { spawn } from 'node:child_process';

// Both suites run even when one fails; CI remains failed if any suite fails.
let failed = false;
for (const script of ['scripts/browser-test.mjs', 'scripts/advanced-editing-test.mjs']) {
  const code = await new Promise(resolve => {
    const child = spawn(process.execPath, [script], { stdio: 'inherit', env: process.env });
    child.once('error', error => { console.error(error); resolve(1); });
    child.once('exit', code => resolve(code ?? 1));
  });
  if (code !== 0) failed = true;
}
process.exitCode = failed ? 1 : 0;
