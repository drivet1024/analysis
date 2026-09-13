const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const data = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const main = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
assert.equal(data.total, main.parcelsTodaySnapshot);
assert.equal(data.mapped + data.unmapped, data.total);
assert.equal(data.points.reduce((n, p) => n + p.parcels, 0), data.mapped);
assert.equal(data.points.reduce((n, p) => n + p.postalParcels, 0), data.postalParcels);
assert.equal(new Set(data.points.map(p => `${p.latitude},${p.longitude}`)).size, data.points.length);
assert(data.points.every(p => p.latitude >= -90 && p.latitude <= 90 && p.longitude >= -180 && p.longitude <= 180 && (p.latitude !== 0 || p.longitude !== 0) && p.parcels > 0));
const html = fs.readFileSync('ConveyorDashboard/wwwroot/edi.html', 'utf8');
const nodes = new Map([...html.matchAll(/id="([^"]+)"/g)].map(match => [match[1], {
  textContent: '', open: false, events: {},
  addEventListener(event, handler) { this.events[event] = handler; },
  showModal() { this.open = true; }, close() { this.open = false; this.events.close?.(); }
}]));
const groups = [], assets = [], markers = [];
let calls = 0;
const map = { setView() { return this; }, invalidateSize() {}, removeLayer() {}, fitBounds(bounds) { this.bounds = bounds; } };
const context = {
  document: { getElementById: id => nodes.get(id), createElement: () => ({ remove() {} }),
    head: { append(element) { assets.push(element); queueMicrotask(() => element.onload()); } } },
  URLSearchParams, AbortController,
  fetch: async () => { calls++; return { ok: true, json: async () => data }; },
  L: {
    map: () => map, tileLayer: () => ({ on() { return this; }, addTo() {} }), latLngBounds: points => points,
    divIcon: options => options,
    marker: (position, options) => { const marker = { position, options, bindTooltip(text) { this.tooltip = text; return this; }, bindPopup(text) { this.popup = text; return this; } }; markers.push(marker); return marker; },
    markerClusterGroup: options => { const group = { options, events: {}, on(event, handler) { this.events[event] = handler; }, addTo() {}, addLayers(rows) { this.rows = rows; } }; groups.push(group); return group; }
  }
};
vm.createContext(context);
vm.runInContext(fs.readFileSync('ConveyorDashboard/wwwroot/edi-map.js', 'utf8'), context);
const tick = () => new Promise(resolve => setImmediate(resolve));
(async () => {
  context.updateEdiMapContext({ date: data.date, asOf: data.asOf });
  assert.equal(calls, 0, 'No map data requested before opening');
  assert.equal(assets.length, 0, 'Map libraries are lazy-loaded');
  nodes.get('edi-map-open').events.click(); await tick();
  assert.equal(calls, 1);
  assert.equal(markers.length, data.points.length);
  assert.equal(map.bounds.length, data.points.length);
  const group = groups[0];
  const cluster = { getAllChildMarkers: () => group.rows, getChildCount: () => group.rows.length,
    bindTooltip(text) { this.tooltip = text; return this; }, openTooltip() { this.open = true; }, closeTooltip() { this.open = false; } };
  assert(group.options.iconCreateFunction(cluster).html.includes(new Intl.NumberFormat('fr-CA').format(data.mapped)));
  group.events.clustermouseover({ layer: cluster });
  assert(cluster.open && cluster.tooltip.includes('colis'));
  group.events.clustermouseout({ layer: cluster }); assert.equal(cluster.open, false);
  assert(markers.every((marker, i) => marker.options.parcelCount === data.points[i].parcels && marker.tooltip.includes('colis')));
  for (let i = 0; i < markers.length; i++) if (data.points[i].postalParcels) assert(markers[i].tooltip.includes('approximative'));
  context.resetEdiMapContext('2000-01-01');
  assert.equal(nodes.get('edi-map-dialog').open, false);
  context.fetch = async () => ({ ok: false });
  context.updateEdiMapContext({ date: data.date, asOf: data.asOf });
  nodes.get('edi-map-open').events.click(); await tick();
  assert(nodes.get('edi-map-status').textContent.includes('indisponible'));
  console.log('Map reconciliation, valid positions, lazy loading, weighted clusters, hover, approximation and failure state verified.');
})();
