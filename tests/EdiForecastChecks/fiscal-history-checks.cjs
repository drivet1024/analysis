const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const data = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const main = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));

assert(data.weeks.length > 0);
assert.equal(data.weeks[0].start, data.fiscalStart);
assert.equal(data.weeks[0].previousStart, data.previousFiscalStart);
assert.equal(data.weeks[0].openingPartial, true);
assert(data.weeks.every((week, index) => week.week === index + 1 && week.parcels >= 0 && week.previousParcels >= 0));
assert.equal(data.weeks.at(-1).parcels, main.days.reduce((sum, day) => sum + day.parcels, 0));

const nodes = new Map();
function makeNode(initialId) {
  const node = {
    events: {}, open: false, _id: initialId || '', _innerHTML: '', textContent: '', dataset: {},
    setAttribute() {}, addEventListener(event, handler) { this.events[event] = handler; },
    showModal() { this.open = true; }, close() { this.open = false; this.events.close?.(); }, focus() {},
    replaceChildren() { this.innerHTML = ''; }, querySelectorAll() { return []; }
  };
  Object.defineProperty(node, 'id', { get() { return this._id; }, set(value) { this._id = value; nodes.set(value, this); } });
  Object.defineProperty(node, 'innerHTML', { get() { return this._innerHTML; }, set(value) {
    this._innerHTML = value;
    for (const match of value.matchAll(/id="([^"]+)"/g)) if (!nodes.has(match[1])) makeNode(match[1]);
  } });
  if (initialId) nodes.set(initialId, node);
  return node;
}
const weekly = makeNode('weekly-parcels-card');
let calls = 0;
const context = {
  document: {
    querySelector: () => null,
    getElementById: id => nodes.get(id),
    createElement: () => makeNode(),
    body: { append() {} }
  },
  Intl, Date, URLSearchParams, AbortController,
  fetch: async () => { calls++; return { ok: true, json: async () => data }; }
};
vm.createContext(context);
vm.runInContext(fs.readFileSync('ConveyorDashboard/wwwroot/edi-history.js', 'utf8'), context);
const tick = () => new Promise(resolve => setImmediate(resolve));
(async () => {
  context.updateEdiFiscalHistoryContext({ date: data.date, asOf: data.asOf });
  assert.equal(calls, 0, 'Fiscal history is lazy-loaded');
  weekly.events.click(); await tick();
  assert.equal(calls, 1);
  assert(nodes.get('edi-fiscal-history-dialog').open);
  const chart = nodes.get('edi-fiscal-history-plot').innerHTML;
  assert.equal((chart.match(/<polyline class="edi-history-line previous"/g) || []).length, 1, 'One prior-year line');
  assert.equal((chart.match(/<polyline class="edi-history-line current"/g) || []).length, 1, 'One current-year line');
  assert.equal((chart.match(/<circle class="edi-history-point previous"/g) || []).length, data.weeks.length, 'One prior-year point per week');
  assert.equal((chart.match(/<circle class="edi-history-point current/g) || []).length, data.weeks.length, 'One current-year point per week');
  const partialWeek = data.weeks.findIndex(week => week.partial);
  assert.equal(chart.includes('edi-history-line partial-segment'), partialWeek > 0, 'Current partial week segment highlighting matches the data');
  assert(chart.includes(`S${data.weeks.length}`));
  assert(chart.includes('Comparaison hebdomadaire'));
  nodes.get('edi-fiscal-history-close').events.click();
  weekly.events.keydown({ key: 'Enter', preventDefault() {} }); await tick();
  assert.equal(calls, 1, 'Fresh fiscal history is reused');
  console.log('Fiscal weeks, current-week reconciliation, prior-year comparison, lazy loading and cache verified.');
})();
