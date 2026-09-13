// Compare an original full API response with the three isolated endpoints,
// captured for the same completed operational date against the same source.
const fs = require('node:fs');
const assert = require('node:assert/strict');
assert.equal(process.argv.length, 6, 'Supply baseline, main, clients and transport JSON files');
const [baseline, main, clients, transport] = process.argv.slice(2)
  .map(path => JSON.parse(fs.readFileSync(path, 'utf8').replace(/^\uFEFF/, '')));
for (const response of [main, clients, transport]) {
  for (const key of ['executionDate', 'weekStart', 'weekEnd'])
    assert.deepEqual(response[key], baseline[key], key);
}
for (const key of ['parcelsTodaySnapshot', 'parcelsLastWeekSameTime', 'weeklyBudget', 'days', 'nowcast'])
  assert.deepEqual(main[key], baseline[key], key);
assert.deepEqual(main.clients, []);
assert.deepEqual(main.regions, []);
assert.deepEqual(clients.regions, []);
assert.deepEqual(clients.days, []);
assert.deepEqual(transport.clients, []);
assert.deepEqual(transport.days, []);
assert.deepEqual(clients.clients, baseline.clients);
assert.equal(transport.regions.length, baseline.regions.length);
for (const original of baseline.regions) {
  const region = transport.regions.find(row => row.region === original.region);
  assert(region, original.region);
  for (const key of Object.keys(original).filter(key => key !== 'estimatedParcelVolume'))
    assert.deepEqual(region[key], original[key], `${original.region}: ${key}`);
  if (original.estimatedParcelVolume == null) assert.equal(region.estimatedParcelVolume, null);
  else {
    // SQL summation order can introduce sub-micro cubic-inch floating point differences.
    assert(Math.abs(region.estimatedParcelVolume - original.estimatedParcelVolume)
      <= Math.max(1e-6, Math.abs(original.estimatedParcelVolume) * 1e-12));
    for (const height of [76, 78, 80]) for (const fill of [0.6, 0.65, 0.7, 0.75, 0.8]) {
      const capacity = 40 * 48 * height * fill;
      assert.equal(Math.ceil(region.estimatedParcelVolume / capacity),
        Math.ceil(original.estimatedParcelVolume / capacity), 'Displayed pallet estimate');
    }
  }
}
console.log('Page isolation, counts, budgets, client trends and pallet estimates verified against baseline.');
