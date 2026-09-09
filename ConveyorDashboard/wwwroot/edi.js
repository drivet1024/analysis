const REFRESH_SECONDS = 60;
const $ = (id) => document.getElementById(id);
const number = new Intl.NumberFormat('fr-CA');
const time = new Intl.DateTimeFormat('fr-CA', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const shortDate = new Intl.DateTimeFormat('fr-CA', { day: '2-digit', month: 'short', year: 'numeric' });
let countdown = REFRESH_SECONDS;
let loading = false;

function parseDate(value) {
  return new Date(`${value}T12:00:00`);
}

function isoLocalDate(date = new Date()) {
  const pad = (part) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function formatDate(value) {
  return value ? shortDate.format(parseDate(value)) : '—';
}

function formatTime(value) {
  return value ? time.format(new Date(value)) : '—';
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
  })[character]);
}

function setConnection(state, label) {
  $('live-dot').className = state;
  $('live-label').textContent = label;
}

function totalsForRegions(regions) {
  return regions.reduce((totals, row) => ({
    parcelsToday: totals.parcelsToday + Number(row.parcelsToday || 0),
    palletsToday: totals.palletsToday + Number(row.palletsToday || 0),
    parcelsYesterday: totals.parcelsYesterday + Number(row.parcelsYesterday || 0),
    palletsYesterday: totals.palletsYesterday + Number(row.palletsYesterday || 0),
    parcelsLastWeek: totals.parcelsLastWeek + Number(row.parcelsLastWeek || 0),
    palletsLastWeek: totals.palletsLastWeek + Number(row.palletsLastWeek || 0)
  }), { parcelsToday: 0, palletsToday: 0, parcelsYesterday: 0, palletsYesterday: 0, parcelsLastWeek: 0, palletsLastWeek: 0 });
}

function renderRegions(regions) {
  const body = $('regions-body');
  body.replaceChildren();
  if (!regions.length) {
    body.innerHTML = '<tr><td colspan="8" class="empty-cell">Aucun volume EDI trouvé pour la période.</td></tr>';
  } else {
    regions.forEach((region) => {
      const row = document.createElement('tr');
      row.innerHTML = `
        <td class="region-name">${escapeHtml(region.region)}</td>
        <td class="depots-cell">${escapeHtml(region.depots)}</td>
        <td><strong>${number.format(region.parcelsToday)}</strong></td>
        <td>${number.format(region.palletsToday)}</td>
        <td><strong>${number.format(region.parcelsYesterday)}</strong></td>
        <td>${number.format(region.palletsYesterday)}</td>
        <td><strong>${number.format(region.parcelsLastWeek)}</strong></td>
        <td>${number.format(region.palletsLastWeek)}</td>`;
      body.append(row);
    });
  }

  const totals = totalsForRegions(regions);
  $('regions-foot').innerHTML = `
    <tr><td colspan="2">Total</td>
      <td>${number.format(totals.parcelsToday)}</td><td>${number.format(totals.palletsToday)}</td>
      <td>${number.format(totals.parcelsYesterday)}</td><td>${number.format(totals.palletsYesterday)}</td>
      <td>${number.format(totals.parcelsLastWeek)}</td><td>${number.format(totals.palletsLastWeek)}</td></tr>`;
  $('parcels-today').textContent = number.format(totals.parcelsToday);
  $('pallets-today').textContent = number.format(totals.palletsToday);
}

function renderWeek(days, weeklyBudget) {
  const today = isoLocalDate();
  const body = $('week-body');
  body.replaceChildren();
  days.forEach((day) => {
    const budget = Number(day.budget || 0);
    const parcels = Number(day.parcels || 0);
    const isFuture = day.date > today;
    const difference = parcels - budget;
    const attainment = budget ? 100 * parcels / budget : 0;
    const row = document.createElement('tr');
    if (day.date === today) row.classList.add('today-row');
    if (isFuture) row.classList.add('future-row');
    row.innerHTML = `
      <td class="day-name">${escapeHtml(day.dayName)}</td>
      <td>${formatDate(day.date)}</td>
      <td><strong>${number.format(parcels)}</strong></td>
      <td>${number.format(budget)}</td>
      <td class="${isFuture ? '' : difference >= 0 ? 'delta-positive' : 'delta-negative'}">${isFuture ? 'À venir' : `${difference >= 0 ? '+' : '−'}${number.format(Math.abs(difference))}`}</td>
      <td>${isFuture ? '—' : `<span class="attainment"><span class="attainment-track"><i style="width:${Math.min(100, attainment)}%"></i></span>${attainment.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} %</span>`}</td>`;
    body.append(row);
  });

  const weeklyParcels = days.reduce((sum, day) => sum + Number(day.parcels || 0), 0);
  const weeklyDifference = weeklyParcels - weeklyBudget;
  const weeklyAttainment = weeklyBudget ? 100 * weeklyParcels / weeklyBudget : 0;
  $('week-foot').innerHTML = `<tr><td colspan="2">Total semaine</td><td>${number.format(weeklyParcels)}</td><td>${number.format(weeklyBudget)}</td><td class="${weeklyDifference >= 0 ? 'delta-positive' : 'delta-negative'}">${weeklyDifference >= 0 ? '+' : '−'}${number.format(Math.abs(weeklyDifference))}</td><td>${weeklyAttainment.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} %</td></tr>`;
  $('weekly-parcels').textContent = number.format(weeklyParcels);
  $('weekly-budget').textContent = number.format(weeklyBudget);
  $('weekly-progress').textContent = `${weeklyAttainment.toLocaleString('fr-CA', { maximumFractionDigits: 1 })} % du budget reçu`;

  const maximum = Math.max(1, ...days.flatMap((day) => [Number(day.parcels || 0), Number(day.budget || 0)]));
  const chart = $('week-chart');
  chart.replaceChildren();
  days.forEach((day) => {
    const parcels = Number(day.parcels || 0);
    const budget = Number(day.budget || 0);
    const column = document.createElement('div');
    column.className = `day-column${day.date > today ? ' future' : ''}`;
    column.innerHTML = `
      <span class="day-value">${number.format(parcels)}</span>
      <div class="day-track" title="${number.format(parcels)} colis · budget ${number.format(budget)}">
        <i class="day-bar" style="height:${100 * parcels / maximum}%"></i>
        <i class="budget-marker" style="bottom:${100 * budget / maximum}%"></i>
      </div>
      <small>${escapeHtml(day.dayName.slice(0, 3))}</small>`;
    chart.append(column);
  });
}

function render(data) {
  const regions = data.regions || [];
  const days = data.days || [];
  renderRegions(regions);
  renderWeek(days, Number(data.weeklyBudget || 0));
  $('snapshot-parcels-today').textContent = number.format(data.parcelsTodaySnapshot || 0);
  $('snapshot-parcels-d7').textContent = number.format(data.parcelsLastWeekSameTime || 0);
  $('week-range').textContent = `${formatDate(data.weekStart)} au ${formatDate(data.weekEnd)}`;
  $('database-time').textContent = formatTime(data.databaseNow);
  $('last-refresh').textContent = `Actualisé à ${formatTime(data.databaseNow)}`;
}

async function load() {
  if (loading) return;
  loading = true;
  $('refresh-button').disabled = true;
  $('error-banner').hidden = true;
  setConnection('waiting', 'Actualisation…');
  try {
    const response = await fetch(`/api/edi?t=${Date.now()}`, { cache: 'no-store' });
    if (!response.ok) throw new Error(`Réponse ${response.status}`);
    render(await response.json());
    setConnection('ok', 'Données en direct');
    countdown = REFRESH_SECONDS;
  } catch (error) {
    setConnection('error', 'Connexion interrompue');
    $('error-banner').textContent = 'Impossible de charger les données EDI. Une nouvelle tentative sera faite automatiquement.';
    $('error-banner').hidden = false;
  } finally {
    loading = false;
    $('refresh-button').disabled = false;
  }
}

$('refresh-button').addEventListener('click', () => { countdown = REFRESH_SECONDS; load(); });
setInterval(() => {
  countdown -= 1;
  if (countdown <= 0) {
    countdown = REFRESH_SECONDS;
    load();
  }
  $('countdown').textContent = countdown;
}, 1000);

load();
