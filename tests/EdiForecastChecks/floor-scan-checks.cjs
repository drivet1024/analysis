const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const data = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

assert.equal(data.depots.length, 25);
const expectedWorkdays = Array.from({ length: 30 }, (_, index) => {
  const date = new Date(`${data.monthStart}T12:00:00`);
  date.setDate(date.getDate() + index);
  return date;
}).filter(date => date.getDay() !== 0 && date.getDay() !== 6).length;
assert(data.depots.every(depot => depot.days.length === 7));
assert(data.depots.every(depot => depot.sevenDayTotal === depot.days.reduce((sum, day) => sum + day.parcels, 0)));
assert(data.depots.every(depot => depot.weekdaysObserved === expectedWorkdays));
assert(data.depots.every(depot => depot.weekdaysWithoutScans >= 0 && depot.weekdaysWithoutScans <= depot.weekdaysObserved));
assert(data.depots.some(depot => depot.sevenDayTotal > 0));
assert(data.depots.some(depot => depot.weekdaysWithoutScans === depot.weekdaysObserved));

const html = fs.readFileSync('ConveyorDashboard/wwwroot/floor-scans.html', 'utf8');
const nodes = new Map([...html.matchAll(/id="([^"]+)"/g)].map(match => [match[1], {
  textContent: '', innerHTML: '', hidden: false, disabled: false, value: '', max: '', className: '',
  events: {}, addEventListener(event, handler) { this.events[event] = handler; }
}]));
let calls = 0;
const context = {
  document: { getElementById: id => nodes.get(id), querySelectorAll: () => [] },
  location: { search: `?date=${data.date}`, href: `http://127.0.0.1/floor-scans.html?date=${data.date}`, origin: 'http://127.0.0.1' },
  history: { replaceState() {} }, URL, URLSearchParams, Intl, Date,
  fetch: async () => { calls++; return { ok: true, json: async () => data }; },
  setInterval: () => 1, clearInterval() {}, addEventListener() {}
};
vm.createContext(context);
vm.runInContext(fs.readFileSync('ConveyorDashboard/wwwroot/floor-scans.js', 'utf8'), context);
const tick = () => new Promise(resolve => setImmediate(resolve));
(async () => {
  await tick();
  assert.equal(calls, 1);
  const rendered = nodes.get('floor-scan-grid').innerHTML;
  assert.equal((rendered.match(/<article class="floor-scan-card/g) || []).length, data.depots.length);
  assert.equal((rendered.match(/<div class="floor-day(?: |")/g) || []).length, data.depots.length * 7);
  assert(rendered.includes('jour sans scan plancher'));
  assert(nodes.get('month-range').textContent.includes('fins de semaine exclues'));
  assert.equal(nodes.get('live-label').textContent, 'En ligne');
  console.log('All depots, seven-day bars, weekday denominator, missing-scan labels and live state verified.');
})();
