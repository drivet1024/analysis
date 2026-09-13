const assert = require('node:assert/strict');
const fs = require('node:fs');

const seed = JSON.parse(fs.readFileSync('ConveyorDashboard/ConveyorEfficiencySeed.json', 'utf8'));
const html = fs.readFileSync('ConveyorDashboard/wwwroot/live-routes.html', 'utf8');
const script = fs.readFileSync('ConveyorDashboard/wwwroot/live-routes.js', 'utf8');
const service = fs.readFileSync('ConveyorDashboard/ConveyorEfficiencyService.cs', 'utf8');

assert.equal(seed.months.length, 12, 'The initial trend contains twelve months');
assert.equal(seed.months.at(-1).month, seed.currentMonth, 'The current month is the final point');
for (let index = 0; index < seed.months.length; index += 1) {
  const month = seed.months[index];
  assert.equal(month.assessedOutcomes, month.problemOutcomes + month.successfulOutcomes, `Month ${month.month} reconciles`);
  assert(Math.abs(month.efficiencyPercent - (100 * month.successfulOutcomes / month.assessedOutcomes)) < 0.001, `Month ${month.month} rate is reproducible`);
  if (index > 0) assert(month.month > seed.months[index - 1].month, 'Months are chronological');
}

const august = seed.months.find((month) => month.month === '2026-08-01');
assert(august, 'Validated August control month exists');
assert.equal(august.assessedOutcomes, 791822);
assert.equal(august.problemOutcomes, 115197);
assert.equal(august.successfulOutcomes, 676625);
assert.equal(august.efficiencyPercent, 85.452);

assert(html.includes('id="conveyor-efficiency-card"'));
assert(html.includes('id="conveyor-efficiency-dialog"'));
assert(script.includes('function efficiencyCurve(points)'));
assert(script.includes("if (tabId === 'conveyor-tab') loadConveyorEfficiency()"), 'Efficiency is loaded lazily on the conveyor tab');
const liveRefreshBody = script.match(/async function loadConveyorData[\s\S]*?\n}\n\nasync function load\(\)/)?.[0] || '';
assert(!liveRefreshBody.includes('/api/conveyor-efficiency'), 'The monthly KPI is absent from the ten-second live refresh');
assert(service.includes('CONVEYOR_EFFICIENCY_PATH'));
assert(service.includes('SaveSnapshotAsync(cached'), 'The daily result is persisted');

console.log('Twelve-month conveyor efficiency, reconciliation, lazy UI loading and persistence verified.');
