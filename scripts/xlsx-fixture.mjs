// Original minimal OPC test fixture writer. No production edits or test hooks.
// ZIP entries are stored uncompressed, with real headers, central directory and CRC32.
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function zip(entries) {
  const local = [], central = []; let offset = 0, directorySize = 0;
  for (const [path, text] of entries) {
    const name = Buffer.from(path), bytes = Buffer.from(text), crc = crc32(bytes);
    const header = Buffer.alloc(30);
    header.writeUInt32LE(0x04034b50, 0); header.writeUInt16LE(20, 4); header.writeUInt16LE(0x800, 6);
    header.writeUInt16LE(33, 12); header.writeUInt32LE(crc, 14); header.writeUInt32LE(bytes.length, 18);
    header.writeUInt32LE(bytes.length, 22); header.writeUInt16LE(name.length, 26);
    local.push(header, name, bytes);
    const record = Buffer.alloc(46);
    record.writeUInt32LE(0x02014b50, 0); record.writeUInt16LE(20, 4); record.writeUInt16LE(20, 6);
    record.writeUInt16LE(0x800, 8); record.writeUInt16LE(33, 14); record.writeUInt32LE(crc, 16);
    record.writeUInt32LE(bytes.length, 20); record.writeUInt32LE(bytes.length, 24); record.writeUInt16LE(name.length, 28);
    record.writeUInt32LE(offset, 42); central.push(record, name);
    directorySize += record.length + name.length; offset += header.length + name.length + bytes.length;
  }
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(directorySize, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...local, ...central, end]);
}
const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const rel = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
const pkg = 'http://schemas.openxmlformats.org/package/2006/relationships';
const inline = (r, value) => `<c r="${r}" t="inlineStr"><is><t>${value}</t></is></c>`;
export function workbookFixture(progress = 20, reversed = false) {
  const entries = [['0001', progress, 'Alice'], ...Array.from({ length: 10 }, (_, i) => [String(i + 10), i === 0 ? 2 : i === 1 ? 100 : 90, 'Bob'])];
  if (reversed) entries.reverse();
  const rows = entries.map(([key, value, owner], index) => {
    const r = index + 4;
    const first = key === '0001' ? `<c r="A${r}" t="s"><v>0</v></c>` : inline(`A${r}`, key);
    const middle = `<c r="B${r}">${reversed && key === '0001' ? '<f>SUM(60,5)</f>' : ''}<v>${value}</v></c>`;
    return `<row r="${r}">${first}${middle}${inline(`C${r}`, owner)}</row>`;
  }).join('');
  const header = `<row r="3">${inline('A3', 'Id')}${inline('B3', 'Progress')}${inline('C3', 'Owner')}</row>`;
  return zip([
    ['[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/overview.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/assets.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>'],
    ['_rels/.rels', `<Relationships xmlns="${pkg}"><Relationship Id="workbook" Type="${rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>`],
    ['xl/workbook.xml', `<workbook xmlns="${ns}" xmlns:r="${rel}"><sheets><sheet name="Overview" sheetId="1" r:id="overview"/><sheet name="Assets" sheetId="2" r:id="assets"/></sheets></workbook>`],
    ['xl/_rels/workbook.xml.rels', `<Relationships xmlns="${pkg}"><Relationship Id="overview" Type="${rel}/worksheet" Target="worksheets/overview.xml"/><Relationship Id="assets" Type="${rel}/worksheet" Target="worksheets/assets.xml"/><Relationship Id="strings" Type="${rel}/sharedStrings" Target="sharedStrings.xml"/></Relationships>`],
    ['xl/sharedStrings.xml', `<sst xmlns="${ns}" count="1" uniqueCount="1"><si><t>0001</t></si></sst>`],
    ['xl/worksheets/overview.xml', `<worksheet xmlns="${ns}"><sheetData><row r="1">${inline('A1', 'Notes')}</row></sheetData></worksheet>`],
    ['xl/worksheets/assets.xml', `<worksheet xmlns="${ns}"><dimension ref="A1:XFD1048576"/><sheetData><row r="1">${inline('A1', 'Asset data')}</row>${header}${rows}</sheetData></worksheet>`]
  ]);
}
