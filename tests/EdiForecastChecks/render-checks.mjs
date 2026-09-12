// Rendering contract checks against an API response; not a substitute for visual browser QA.
import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const payload = JSON.parse(fs.readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
const script = fs.readFileSync('ConveyorDashboard/wwwroot/edi.js', 'utf8');
const html = fs.readFileSync('ConveyorDashboard/wwwroot/edi-previsions.html', 'utf8');
const elements = new Map([...html.matchAll(/id="([^"]+)"/g)].map((match) => [match[1], {
  textContent: '', innerHTML: '', value: '', hidden: false, children: [],
  replaceChildren() { this.children = []; this.innerHTML = ''; }, append(child) { this.children.push(child); }
}]));
const context = {
  $: (id) => { assert(elements.has(id), `Missing HTML element ${id}`); return elements.get(id); },
  load: (id) => { context.requestedVersion = id; },
  document: { createElement: () => ({ innerHTML: '' }) },
  number: new Intl.NumberFormat('fr-CA'), decimal: new Intl.NumberFormat('fr-CA', { maximumFractionDigits: 1 }),
  formatDate: (date) => date, escapeHtml: (value) => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;'),
};
vm.createContext(context);
vm.runInContext(script.slice(script.indexOf('function renderForecast('), script.indexOf('function render(data)')), context);
context.renderForecast(payload.forecast, payload.forecastArchive);
context.renderForecastVersions(payload.forecastArchive);
assert.equal(elements.get('forecast-body').children.length, 7);
assert.equal(elements.get('forecast-seasonality').hidden, false);
assert.match(elements.get('forecast-growth').textContent, /Facteur annuel/);
assert.match(elements.get('forecast-body').children[0].innerHTML, /50 % de tendance récente/);
assert.match(elements.get('forecast-body').children[0].innerHTML, /Références annuelles/);
assert.match(elements.get('forecast-model-comparison').textContent, /mêmes 27 journées/);
const selector = elements.get('forecast-archive-select');
selector.value = payload.forecastArchive.snapshot.id;
selector.onchange();
assert.equal(context.requestedVersion, payload.forecastArchive.snapshot.id);
assert(!html.includes('id="forecast-comparison-body"'));
assert.equal((elements.get('forecast-body').children[0].innerHTML.match(/<td/g)||[]).length, 7);
const firstDate = payload.forecast.days[0].date;
const matched = { snapshotId: payload.forecastArchive.snapshot.id, date: firstDate, actual: 0, difference: -10 };
context.renderForecast(payload.forecast, { ...payload.forecastArchive, comparisons: [matched, { ...matched, snapshotId: 'different-version', actual: 99999 }] });
assert(elements.get('forecast-body').children[0].innerHTML.includes('<td>0</td>'));
assert(elements.get('forecast-body').children[0].innerHTML.includes('<td>-10</td>'));
assert(!elements.get('forecast-body').children[0].innerHTML.includes('99999'));
assert(elements.get('forecast-foot').innerHTML.includes('Incomplet'));
context.renderForecast({ ...payload.forecast, seasonality: null }, payload.forecastArchive);
assert.equal(elements.get('forecast-seasonality').hidden, true);
console.log('Seasonality rendering and archive-filter checks passed.');
