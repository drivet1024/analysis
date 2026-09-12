import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const data = JSON.parse(fs.readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
const html = fs.readFileSync('ConveyorDashboard/wwwroot/livraison-previsions.html', 'utf8');
const ediForecast = fs.readFileSync('ConveyorDashboard/wwwroot/edi-previsions.html', 'utf8');
const edi = fs.readFileSync('ConveyorDashboard/wwwroot/edi.html', 'utf8');
assert(!edi.includes('id="forecast-body"')); 
assert(ediForecast.includes('id="forecast-body"') && !ediForecast.includes('id="forecast-comparison-body"')); 
assert(!ediForecast.includes('id="sector-body"') && !html.includes('id="forecast-body"'));
const nodes = new Map([...html.matchAll(/id="([^"]+)"/g)].map(match => [match[1], { value: '', innerHTML: '', textContent: '', checked: false }]));
const script = fs.readFileSync('ConveyorDashboard/wwwroot/edi.js', 'utf8');
const context = { $: id => { assert(nodes.has(id), `Missing ${id}`); return nodes.get(id); },
  sectorData: null, normalizedClientName: value => String(value).toLowerCase(), escapeHtml: value => String(value).replaceAll('<', '&lt;'),
  number: new Intl.NumberFormat('fr-CA'), decimal: new Intl.NumberFormat('fr-CA'), formatDate: value => value };
vm.createContext(context);
vm.runInContext(script.slice(script.indexOf('function sectorActualLine('), script.indexOf('function renderNowcast(')), context);
context.renderSectors(data);
assert.equal((nodes.get('sector-body').innerHTML.match(/<tr>/g) || []).length, data.sectors.filter(s => s.historicalParcels > 0).length);
assert.equal((nodes.get('sector-head').innerHTML.match(/<th>/g) || []).length, 8);
nodes.get('sector-filter').value = '530';
context.renderSectors(data);
assert.equal((nodes.get('sector-body').innerHTML.match(/<tr>/g) || []).length, 1);
const sector = data.sectors.find(s => s.sectorId === 530);
for (const day of sector.forecast.days.filter(day => ![0, 6].includes(new Date(day.date + 'T12:00:00').getDay()))) {
  if (day.parcels != null) assert(nodes.get('sector-body').innerHTML.includes(new Intl.NumberFormat('fr-CA').format(day.parcels)));
  if ([0, 6].includes(new Date(day.date + 'T12:00:00').getDay())) assert.equal(day.parcels, 0);
}
assert.equal(data.modelVersion, 'sorted-sector-delivery-v2');
assert(nodes.get('sector-quality').textContent.includes('colis uniques triés'));
assert(!nodes.get('sector-head').innerHTML.includes('samedi') && !nodes.get('sector-head').innerHTML.includes('dimanche'));
assert(!nodes.get('sector-body').innerHTML.includes('Aucune livraison le week-end'));
nodes.get('sector-filter').value = '';
nodes.get('sector-show-empty').checked = true;
context.renderSectors(data);
assert.equal((nodes.get('sector-body').innerHTML.match(/<tr>/g) || []).length, data.sectors.length);
assert.equal(data.sectors.reduce((sum, s) => sum + s.historicalParcels, 0) + data.outsideDepotParcels + data.unmappedParcels + data.ambiguousPostalParcels, data.networkParcels);
assert(data.sectors.every(s => s.postalParcels + s.routeFallbackParcels === s.historicalParcels));
if (data.weekly) {
  assert(nodes.get('sector-body').innerHTML.includes('Réel :'));
  if (!data.weekly.forecastAvailable) assert(nodes.get('sector-status').textContent.includes('Aucune prévision sauvegardée'));
}
console.log('Moved forecasts, five weekday columns, filtering, empty-state toggle and source reconciliation verified.');
