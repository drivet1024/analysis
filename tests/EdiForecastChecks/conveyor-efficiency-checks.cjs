const assert = require('node:assert/strict');
const fs = require('node:fs');

const seed = JSON.parse(fs.readFileSync('ConveyorDashboard/ConveyorEfficiencySeed.json', 'utf8'));
const html = fs.readFileSync('ConveyorDashboard/wwwroot/live-routes.html', 'utf8');
const script = fs.readFileSync('ConveyorDashboard/wwwroot/live-routes.js', 'utf8');
const service = fs.readFileSync('ConveyorDashboard/ConveyorEfficiencyService.cs', 'utf8');
const program = fs.readFileSync('ConveyorDashboard/Program.cs', 'utf8');

assert.equal(seed.months.length, 12, 'The initial trend contains twelve months');
assert.equal(seed.calculationVersion, 3, 'The bundled baseline is retained until the atomic version-4 rebuild completes');
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
assert(!html.includes('id="conveyor-depot-heading"'), 'The redundant conveyor section title is removed');
assert(html.indexOf('id="conveyor-efficiency-card"') < html.indexOf('id="conveyor-shift-kicker"'));
assert(html.indexOf('id="conveyor-shift-kicker"') < html.indexOf('id="conveyor-pill-grid"'), 'The shift label sits between the two pill rows');
assert(script.includes('function efficiencyCurve(points)'));
assert(script.includes("if (tabId === 'conveyor-tab') loadConveyorEfficiency()"), 'Efficiency is loaded lazily on the conveyor tab');
const liveRefreshBody = script.match(/async function loadConveyorData[\s\S]*?\n}\n\nasync function load\(\)/)?.[0] || '';
assert(!liveRefreshBody.includes('/api/conveyor-efficiency'), 'The monthly KPI is absent from the ten-second live refresh');
assert(service.includes('CONVEYOR_EFFICIENCY_PATH'));
assert(service.includes('CurrentCalculationVersion = 5'), 'The service requests the cross-depot measurement policy');
assert(service.includes('SaveSnapshotAsync(cached'), 'The daily result is persisted');
assert(service.includes('measurement_resolution'));
assert(service.includes('ph.SOURCE_TYPE IN (200,201)'), 'Manual and automated parcel-history measurements supplement scanner data');
assert(service.includes("pr.conveyor_key='sth-floor' OR COALESCE(pm.has_dimensions,0)"), 'Floor-conveyor parcels do not require dimensions');
assert(service.includes('COALESCE(pm.has_weight,0)'), 'Weight can be recovered from another automated pass');
assert(service.includes('history_measurement'), 'Parcel history supplements scanner measurements from other depots');
assert(service.includes('measurement_observation'), 'Measurements are combined before completeness is evaluated');
assert(service.includes('for (var offset = -11; offset <= 0; offset++)'), 'A calculation-version change rebuilds all twelve months');
assert(service.includes('now.Year, now.Month, now.Day, 11, 0, 0'), 'The daily calculation is scheduled for 11:00');

assert(html.includes('aria-controls="under2-clients-dialog"'), 'The under-two-pound card opens an accessible dialog');
assert(html.includes('id="under2-clients-body"'), 'The client detail table is present');
assert(script.includes('/api/conveyor-under-two-pounds/clients?'), 'Client details are loaded only when requested');
assert(script.includes("if (!DEPOTS[selectedDepotKey].supportsMeasurements) return;"), 'Depots without weight measurements cannot open the detail');
assert(program.includes('GetConveyorUnderTwoPoundsClientsAsync'), 'The client endpoint has a dedicated data query');
assert(program.includes('HAVING MAX(weight IS NOT NULL AND weight<2)=1'), 'The popup follows the same unique-parcel weight rule as the KPI');
assert(program.includes('GROUP BY r.customer_id,c.NAME'), 'Under-two-pound parcels are grouped by client');

console.log('Twelve-month conveyor efficiency, reconciliation, lazy UI loading and persistence verified.');
