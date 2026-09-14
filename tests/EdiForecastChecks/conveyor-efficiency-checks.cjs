const assert = require('node:assert/strict');
const fs = require('node:fs');

const seed = JSON.parse(fs.readFileSync('ConveyorDashboard/ConveyorEfficiencySeed.json', 'utf8'));
const html = fs.readFileSync('ConveyorDashboard/wwwroot/live-routes.html', 'utf8');
const script = fs.readFileSync('ConveyorDashboard/wwwroot/live-routes.js', 'utf8');
const service = fs.readFileSync('ConveyorDashboard/ConveyorEfficiencyService.cs', 'utf8');

assert.equal(seed.months.length, 12, 'The initial trend contains twelve months');
assert.equal(seed.calculationVersion, 3, 'The persisted snapshot combines measurements across automated passes');
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
assert.equal(august.problemOutcomes, 95867);
assert.equal(august.successfulOutcomes, 695955);
assert.equal(august.revenueRiskParcels, 31976);
assert.equal(august.efficiencyPercent, 87.893);

assert(html.includes('id="conveyor-efficiency-card"'));
assert(html.includes('id="conveyor-efficiency-dialog"'));
assert(script.includes('function efficiencyCurve(points)'));
assert(script.includes("if (tabId === 'conveyor-tab') loadConveyorEfficiency()"), 'Efficiency is loaded lazily on the conveyor tab');
const liveRefreshBody = script.match(/async function loadConveyorData[\s\S]*?\n}\n\nasync function load\(\)/)?.[0] || '';
assert(!liveRefreshBody.includes('/api/conveyor-efficiency'), 'The monthly KPI is absent from the ten-second live refresh');
assert(service.includes('CONVEYOR_EFFICIENCY_PATH'));
assert(service.includes('SaveSnapshotAsync(cached'), 'The daily result is persisted');
assert(service.includes('measurement_resolution'));
assert(service.includes('NOT COALESCE(pm.has_complete_measurement,0)'), 'A later complete automated measurement clears the measurement issue');
assert(service.includes('(MAX(weight>0) AND MAX(l>0) AND MAX(w>0) AND MAX(h>0))'), 'Weight and dimensions can come from separate passes');
assert(service.includes('now.Year, now.Month, now.Day, 11, 0, 0'), 'The daily calculation is scheduled for 11:00');

console.log('Twelve-month conveyor efficiency, reconciliation, lazy UI loading and persistence verified.');
