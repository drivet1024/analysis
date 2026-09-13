const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const read = path => JSON.parse(fs.readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
const data = read(process.argv[2]);
const main = read(process.argv[3]);
assert.equal(data.asOf, main.nowcast.asOf);
assert.equal(data.depots.reduce((n, row) => n + row.parcelsToday, 0), main.parcelsTodaySnapshot);
assert.equal(data.depots.reduce((n, row) => n + row.parcelsD7, 0), main.parcelsLastWeekSameTime);
assert.equal(new Set(data.depots.map(row => row.depotId)).size, data.depots.length);
const html = fs.readFileSync('ConveyorDashboard/wwwroot/edi.html', 'utf8');
const nodes = new Map([...html.matchAll(/id="([^"]+)"/g)].map(match => [match[1], {
  innerHTML: '', textContent: '', replaceChildren() { this.innerHTML = ''; }
}]));
const context = {
  $: id => { assert(nodes.has(id), id); return nodes.get(id); },
  number: new Intl.NumberFormat('fr-CA'), decimal: new Intl.NumberFormat('fr-CA', { maximumFractionDigits: 1 }),
  formatDate: value => value, formatTime: value => value, currentEdiDate: () => data.date,
  escapeHtml: value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;'),
  requestVersion: 1, URLSearchParams
};
vm.createContext(context);
const script = fs.readFileSync('ConveyorDashboard/wwwroot/edi.js', 'utf8');
vm.runInContext(script.slice(script.indexOf('function renderDepots('), script.indexOf('function renderParcelSnapshot(')), context);
context.renderDepots(data);
assert.equal((nodes.get('depot-body').innerHTML.match(/<tr>/g) || []).length, data.depots.length);
assert(nodes.get('depot-foot').innerHTML.includes(context.number.format(main.parcelsTodaySnapshot)));
context.renderDepots({ ...data, depots: [{ depotId: -1, depotName: '<inconnu>', parcelsToday: 0, parcelsD7: 3 }] });
assert(nodes.get('depot-body').innerHTML.includes('&lt;inconnu>'));
assert(nodes.get('depot-body').innerHTML.includes('<td>0</td>'));
assert(nodes.get('depot-body').innerHTML.includes('<td>-3</td>'));
assert(!nodes.get('depot-body').innerHTML.includes('NaN'));
(async () => {
  const previous = nodes.get('depot-body').innerHTML;
  context.fetch = async () => ({ ok: true, json: async () => data });
  await context.loadDepots(data.date, 0);
  assert.equal(nodes.get('depot-body').innerHTML, previous, 'Ignore response from an old date request');
  context.fetch = async () => ({ ok: false, status: 503 });
  await context.loadDepots(data.date, 1);
  assert(nodes.get('depot-body').innerHTML.includes('indisponibles'));
  assert.equal(nodes.get('depot-foot').innerHTML, '');
  console.log('Depot totals, shared cutoff, zero values, escaping, stale responses and failure state verified.');
})();
