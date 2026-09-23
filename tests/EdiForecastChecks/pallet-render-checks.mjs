import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const nodes = new Map();
for (const id of ['pallet-unit','pallet-height','pallet-fill','pallet-assumptions','regions-body','regions-foot','parcels-today','pallets-today']) {
  nodes.set(id, { value: '', innerHTML: '', textContent: '', children: [],
    replaceChildren() { this.children = []; }, append(child) { this.children.push(child); } });
}
nodes.get('pallet-unit').value = 'in';
nodes.get('pallet-height').value = '78';
nodes.get('pallet-fill').value = '0.7';
const context = { $: id => nodes.get(id), document: { createElement: () => ({ innerHTML: '' }) },
  number: new Intl.NumberFormat('fr-CA'), decimal: new Intl.NumberFormat('fr-CA'), escapeHtml: String, regionRows: [] };
const script = fs.readFileSync('ConveyorDashboard/wwwroot/edi.js', 'utf8');
vm.createContext(context);
vm.runInContext(script.slice(script.indexOf('function totalsForRegions'), script.indexOf('function trendBadge')), context);
const capacity = 40 * 48 * 78 * 0.7;
const row = { region: 'Test', depots: '', parcelsToday: 10, palletsToday: 1, parcelsYesterday: 0, palletsYesterday: 0,
  parcelsLastWeek: 0, palletsLastWeek: 0, estimatedParcelVolume: capacity, clientProfileParcels: 10,
  fallbackProfileParcels: 0, missingProfileParcels: 0 };
const displayed = () => nodes.get('regions-body').children[0].innerHTML.match(/class="[^"]*pallet-estimate"[^>]*><strong>([^<]*)/)[1];
context.renderRegions([row]);
assert.equal(displayed(), '1', 'One effective pallet capacity');
nodes.get('pallet-fill').value = '0.6';
context.renderRegions([row]);
assert.equal(displayed(), '2', 'More void requires more pallets');
assert.equal(nodes.get('pallets-today').textContent, '2', 'Linehaul card follows packing settings');
nodes.get('pallet-fill').value = '0.7';
nodes.get('pallet-unit').value = 'cm';
context.renderRegions([{ ...row, estimatedParcelVolume: capacity * 2.54 ** 3 * 0.99 }]);
assert.equal(displayed(), '1', 'Cubic centimeters converted to cubic inches');
nodes.get('pallet-unit').value = 'in';
context.renderRegions([{ ...row, missingProfileParcels: 1 }]);
assert.equal(displayed(), '—', 'Missing dimensions must not produce a partial regional total');
assert.equal(nodes.get('pallets-today').textContent, 'Incomplet', 'Linehaul card must not report a partial estimate');
context.renderRegions([{ ...row, parcelsToday: 0, estimatedParcelVolume: null }]);
assert.equal(displayed(), '0', 'Zero parcels require zero pallets');
context.renderRegions([{ ...row, estimatedParcelVolume: capacity * 0.4 }, { ...row, estimatedParcelVolume: capacity * 0.4 }]);
assert(nodes.get('regions-foot').innerHTML.includes('<td class="period-today pallet-estimate">2</td>'), 'Round each region before summing');
assert.equal(nodes.get('pallets-today').textContent, '2', 'Linehaul card matches the sum of regional estimates');
const period = (parcels, volume, missing = 0) => ({ parcels, estimatedParcelVolume: volume,
  clientProfileParcels: parcels - missing, fallbackProfileParcels: 0, missingProfileParcels: missing });
const comparison = { ...row, yesterdaySameTime: period(20, capacity * 2),
  yesterdayFinal: period(50, capacity * 4), lastWeekSameTime: period(30, capacity * 3),
  lastWeekFinal: period(80, capacity * 5) };
const historicalPallets = () => [...nodes.get('regions-body').children[0].innerHTML.matchAll(/class="[^"]*pallet-estimate"[^>]*><strong>([^<]*)/g)].map(match => match[1]);
context.renderRegions([comparison]);
assert.deepEqual(historicalPallets(), ['1', '2', '4', '3', '5'], 'Five distinct period estimates');
nodes.get('pallet-fill').value = '0.6';
context.renderRegions([comparison]);
assert.deepEqual(historicalPallets(), ['2', '3', '5', '4', '6'], 'Settings update every period');
nodes.get('pallet-fill').value = '0.7';
context.renderRegions([{ ...comparison, yesterdayFinal: period(50, capacity, 1) }]);
assert.deepEqual(historicalPallets(), ['1', '2', '—', '3', '5'], 'Only the incomplete period is unavailable');
assert(nodes.get('regions-foot').innerHTML.includes('period-yesterday period-final pallet-estimate">Incomplet'));
context.renderRegions([{ ...comparison, lastWeekFinal: period(0, null) }]);
assert.equal(historicalPallets()[4], '0');
context.renderRegions([row]);
assert.deepEqual(historicalPallets(), ['1', '—', '—', '—', '—'], 'Missing API fields never masquerade as zero');
console.log('Pallet capacity, packing margin, units, missing data and regional rounding verified.');
