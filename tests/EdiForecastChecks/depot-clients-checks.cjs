const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const data = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
assert.equal(data.clients.reduce((sum, row) => sum + row.parcelsToday, 0), data.total);
assert(data.clients.every((row, i) => !i || row.parcelsToday <= data.clients[i - 1].parcelsToday));
const html = fs.readFileSync('ConveyorDashboard/wwwroot/edi.html', 'utf8');
const nodes = new Map([...html.matchAll(/id="([^"]+)"/g)].map(match => [match[1], {
  innerHTML: '', textContent: '', checked: false, open: false, events: {},
  addEventListener(event, handler) { this.events[event] = handler; },
  replaceChildren() { this.innerHTML = ''; }, showModal() { this.open = true; },
  close() { this.open = false; this.events.close?.(); }
}]));
let requests = 0;
const context = {
  document: { getElementById: id => nodes.get(id) }, URLSearchParams,
  fetch: async () => { requests++; return { ok: true, json: async () => data }; }
};
vm.createContext(context);
vm.runInContext(fs.readFileSync('ConveyorDashboard/wwwroot/edi-depot-clients.js', 'utf8'), context);
const click = () => nodes.get('depot-body').events.click({ target: { closest: () => ({ dataset: { depotClients: String(data.depotId) } }) } });
const tick = () => new Promise(resolve => setImmediate(resolve));
(async () => {
  context.updateDepotClientContext({ date: data.date, asOf: data.asOf, depots: [{ depotId: data.depotId, depotName: data.depotName }] });
  click(); await tick();
  assert.equal(nodes.get('depot-clients-dialog').open, true);
  const rows = () => (nodes.get('depot-clients-body').innerHTML.match(/<tr>/g) || []).length;
  assert.equal(rows(), Math.min(10, data.clients.length));
  nodes.get('depot-clients-all').checked = true;
  nodes.get('depot-clients-all').events.change();
  assert.equal(rows(), data.clients.length);
  assert.equal(requests, 1, 'All clients use the same cached response');
  nodes.get('depot-clients-close').events.click();
  click(); await tick();
  assert.equal(nodes.get('depot-clients-all').checked, false);
  assert.equal(rows(), Math.min(10, data.clients.length));
  context.resetDepotClientContext('2000-01-01');
  assert.equal(nodes.get('depot-clients-dialog').open, false, 'Date changes close stale details');
  for (const page of fs.readdirSync('ConveyorDashboard/wwwroot').filter(name => name.endsWith('.html'))) {
    const content = fs.readFileSync('ConveyorDashboard/wwwroot/' + page, 'utf8');
    assert(content.includes('/deployment-footer.js'), page);
    assert(content.includes('/deployment-footer.css'), page);
  }
  console.log('Client reconciliation, top 10, all clients without refetch, reopen, date change and footer coverage verified.');
})();
