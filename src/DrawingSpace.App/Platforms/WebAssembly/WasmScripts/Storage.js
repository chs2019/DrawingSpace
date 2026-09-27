(() => {
  'use strict';
  const databaseName = 'DrawingSpace';
  let database;
  const openDatabase = () => database ??= new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, 1);
    request.onupgradeneeded = () => request.result.createObjectStore('documents');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Could not open local storage.'));
  });
  globalThis.drawingSpaceStorage = {
    async load() {
      const db = await openDatabase();
      return new Promise((resolve, reject) => {
        const transaction = db.transaction('documents', 'readonly');
        const request = transaction.objectStore('documents').get('recovery');
        request.onsuccess = () => resolve(request.result ?? '');
        request.onerror = () => reject(request.error);
      });
    },
    async save(json) {
      if (typeof json !== 'string' || json.length > 32 * 1024 * 1024) throw new Error('Recovery copy exceeds the supported size.');
      const db = await openDatabase();
      return new Promise((resolve, reject) => {
        const transaction = db.transaction('documents', 'readwrite');
        transaction.objectStore('documents').put(json, 'recovery');
        transaction.oncomplete = () => resolve('');
        transaction.onerror = () => reject(transaction.error ?? new Error('Local storage write failed.'));
        transaction.onabort = () => reject(transaction.error ?? new Error('Local storage write was aborted.'));
      });
    },
    async open() {
      return new Promise((resolve, reject) => {
        const input = document.createElement('input'); input.type = 'file'; input.accept = '.json,.drawingspace'; input.hidden = true;
        document.body.append(input);
        const done = result => { input.remove(); resolve(result); };
        input.addEventListener('cancel', () => done(''), { once: true });
        input.addEventListener('change', async () => {
          const file = input.files?.[0]; if (!file) return done('');
          try {
            if (file.size > 32 * 1024 * 1024) throw new Error('The drawing exceeds the 32 MB file limit.');
            const text = await file.text(); done(JSON.stringify({ name: file.name, text }));
          } catch (error) { input.remove(); reject(error); }
        }, { once: true });
        input.click();
      });
    },
    async download(name, base64, contentType) {
      const binary = atob(base64); const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
      const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
      const link = document.createElement('a'); link.href = url; link.download = name; link.hidden = true;
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 30000); return '';
    },
    async readClipboard() { return await navigator.clipboard.readText(); },
    async writeClipboard(text) { await navigator.clipboard.writeText(text); return ''; },
    isTestMode() { return new URLSearchParams(location.search).get('test') === '1'; },
    publishDiagnostics(json) { globalThis.drawingSpaceSnapshot = JSON.parse(json); }
  };
  // Browser defaults must not hijack commands routed to the actual Uno window.
  document.addEventListener('keydown', event => {
    const input = /^(INPUT|TEXTAREA|SELECT)$/.test(event.target?.tagName ?? '') || event.target?.isContentEditable;
    if (!input && (event.ctrlKey || event.metaKey) && ['s', 'o', 'n', 'd', 'g', 'f', '1', '2', '3'].includes(event.key.toLowerCase())) event.preventDefault();
    if (!input && event.key === ' ' && event.target !== document.body) event.preventDefault();
  });
})();
