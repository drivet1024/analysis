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
payload.forecastArchive.snapshot.mlForecast = {
  modelId: 'edi-lightgbm-test-v1', featureVersion: 'edi-calendar-lags-v1', trainedAt: '2026-09-12T10:00:00Z',
  trainedThrough: '2026-09-11', trainingRows: 700, backtestMae: 125.4, backtestWape: 2.1,
  statisticalBacktestMae: 180.1, statisticalBacktestWape: 3.2, backtestDays: 56,
  days: payload.forecast.days.map((day, index) => ({ date: day.date, parcels: 1000 + index, status: 'Prévision LightGBM' })),
  total: 7021, status: 'Modèle LightGBM local chargé'
};
vm.createContext(context);
vm.runInContext(script.slice(script.indexOf('function renderForecast('), script.indexOf('function render(data)')), context);
context.renderForecast(payload.forecast, payload.forecastArchive);
context.renderForecastVersions(payload.forecastArchive);
assert.equal(elements.get('forecast-body').children.length, 7);
assert.equal(elements.get('forecast-seasonality').hidden, false);
assert.match(elements.get('forecast-growth').textContent, /Facteur annuel/);
assert(!html.includes('Explication du chiffre'));
assert(!elements.get('forecast-body').children[0].innerHTML.includes('Voir les volumes et le calcul'));
assert.match(elements.get('forecast-model-comparison').textContent, /mêmes 27 journées/);
assert.match(elements.get('forecast-ml-summary').textContent, /edi-lightgbm-test-v1/);
assert.match(elements.get('forecast-ml-summary').textContent, /WAPE 2,1 %/);
assert.match(elements.get('forecast-ml-summary').textContent, /statistique 180,1 colis, WAPE 3,2 %/);
const selector = elements.get('forecast-archive-select');
selector.value = payload.forecastArchive.snapshot.id;
selector.onchange();
assert.equal(context.requestedVersion, payload.forecastArchive.snapshot.id);
assert(!html.includes('id="forecast-comparison-body"'));
assert.equal((elements.get('forecast-body').children[0].innerHTML.match(/<td/g)||[]).length, 9);
assert.match(elements.get('forecast-body').children[0].innerHTML, /1[\s ]?000/);
const firstDate = payload.forecast.days[0].date;
const matched = { snapshotId: payload.forecastArchive.snapshot.id, date: firstDate, actual: 0, difference: -10, mlDifference: -1000 };
context.renderForecast(payload.forecast, { ...payload.forecastArchive, comparisons: [matched, { ...matched, snapshotId: 'different-version', actual: 99999 }] });
assert(elements.get('forecast-body').children[0].innerHTML.includes('<td class="forecast-actual-value">0</td>'));
assert(elements.get('forecast-body').children[0].innerHTML.includes('<td>-10</td>'));
assert.match(elements.get('forecast-body').children[0].innerHTML, /<td>-1[\s ]?000<\/td>/);
assert(!elements.get('forecast-body').children[0].innerHTML.includes('99999'));
assert(elements.get('forecast-foot').innerHTML.includes('Incomplet'));
const matchedPercent = { ...matched, actual: 1000, mlDifference: -100, mlErrorPercent: 10 };
context.renderForecast(payload.forecast, { ...payload.forecastArchive, comparisons: [matchedPercent] });
assert(elements.get('forecast-body').children[0].innerHTML.includes('<td>10 %</td>'));
context.renderForecast({ ...payload.forecast, seasonality: null }, payload.forecastArchive);
assert.equal(elements.get('forecast-seasonality').hidden, true);
assert.equal(elements.get('forecast-average-ml').textContent, new Intl.NumberFormat('fr-CA').format(1003));
const averageDays = payload.forecast.days.map((day, index) => ({ ...day, parcels: index === 0 ? 0 : index === 1 ? 100 : null }));
context.renderForecast({ ...payload.forecast, days: averageDays }, payload.forecastArchive);
assert.equal(elements.get('forecast-average-statistical').textContent, '50');
assert.match(elements.get('forecast-average-note').textContent, /statistique : 2 jour/);
context.renderForecast(null, null);
assert.equal(elements.get('forecast-average-statistical').textContent, '—');
assert.equal(elements.get('forecast-average-ml').textContent, '—');
console.log('Seasonality rendering and archive-filter checks passed.');
