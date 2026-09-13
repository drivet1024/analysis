(() => {
  const REFRESH_SECONDS = 60;
  const $ = id => document.getElementById(id);
  const number = new Intl.NumberFormat('fr-CA');
  const shortDate = new Intl.DateTimeFormat('fr-CA', { day: 'numeric', month: 'short' });
  const dayName = new Intl.DateTimeFormat('fr-CA', { weekday: 'short' });
  const monthName = new Intl.DateTimeFormat('fr-CA', { month: 'short' });
  const time = new Intl.DateTimeFormat('fr-CA', { timeZone: 'America/Toronto', hour: '2-digit', minute: '2-digit', second: '2-digit' });
  let selectedDate = new URLSearchParams(location.search).get('date') || localDate();
  let requestVersion = 0, countdown = REFRESH_SECONDS, timer = null;

  function localDate(date = new Date()) {
    const pad = value => String(value).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }
  function parseDate(value) { return new Date(`${value}T12:00:00`); }
  function formatDate(value) { return shortDate.format(parseDate(value)); }
  function escape(value) { return String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;'); }
  function setDate(value) {
    const today = localDate();
    selectedDate = value > today ? today : value;
    $('analysis-date').value = selectedDate;
    $('analysis-date').max = today;
    $('next-date').disabled = selectedDate >= today;
    const url = new URL(location.href);
    url.searchParams.set('date', selectedDate);
    history.replaceState(null, '', url);
    document.querySelectorAll('[data-analysis-navigation]').forEach(link => {
      const target = new URL(link.href, location.origin);
      target.searchParams.set('date', selectedDate);
      link.href = target.pathname + target.search;
    });
  }
  function moveDate(offset) {
    const date = parseDate(selectedDate);
    date.setDate(date.getDate() + offset);
    setDate(localDate(date));
    load();
  }
  function rate(value) { return value == null ? '—' : `${number.format(value)} %`; }
  function signedPoints(value) { return `${value > 0 ? '+' : ''}${number.format(value)} pts`; }
  function trendSparkline(depot) {
    const valid = depot.months.map((month, index) => ({ ...month, index })).filter(month => month.dailyScanRate != null);
    const points = valid.map(month => `${7 + month.index * 34},${41 - month.dailyScanRate * .36}`).join(' ');
    const labels = depot.months.map(month => `${monthName.format(parseDate(month.month))} : ${rate(month.dailyScanRate)} (${number.format(month.daysWithScans)}/${number.format(month.weekdaysObserved)} jours)`).join(' · ');
    const circles = valid.map(month => `<circle cx="${7 + month.index * 34}" cy="${41 - month.dailyScanRate * .36}" r="3"><title>${escape(monthName.format(parseDate(month.month)))} : ${escape(rate(month.dailyScanRate))}</title></circle>`).join('');
    return `<svg class="floor-trend-sparkline ${depot.trendDirection}" viewBox="0 0 184 46" role="img" aria-label="${escape(labels)}"><line x1="7" y1="23" x2="177" y2="23"></line>${valid.length > 1 ? `<polyline points="${points}"></polyline>` : ''}${circles}</svg>`;
  }
  function trendRow(depot) {
    const direction = depot.trendDirection === 'positive' ? 'Hausse' : 'Baisse';
    const symbol = depot.trendDirection === 'positive' ? '↗' : '↘';
    return `<div class="floor-trend-row ${depot.trendDirection}">
      <div class="floor-trend-identity"><strong>${escape(depot.depotName)}</strong><small>Dépôt ${depot.depotId} · assiduité 6 mois ${rate(depot.sixMonthDailyScanRate)}</small></div>
      ${trendSparkline(depot)}
      <div class="floor-trend-change"><strong>${symbol} ${escape(signedPoints(depot.trendChangePoints))}</strong><small>${rate(depot.previousThreeMonthRate)} → ${rate(depot.recentThreeMonthRate)}</small></div>
    </div>`;
  }
  function render(data) {
    $('chart-range').textContent = `${formatDate(data.sevenDayStart)} au ${formatDate(data.date)}`;
    $('month-range').textContent = `${formatDate(data.monthStart)} au ${formatDate(data.monthEnd)} · fins de semaine exclues`;
    $('database-time').textContent = time.format(new Date(data.databaseNow));
    $('trend-range').textContent = `${formatDate(data.trendStart)} au ${formatDate(data.trendEnd)} · 3 derniers mois comparés aux 3 précédents`;
    const positive = data.depots.filter(depot => depot.trendDirection === 'positive').sort((a, b) => b.trendChangePoints - a.trendChangePoints);
    const negative = data.depots.filter(depot => depot.trendDirection === 'negative').sort((a, b) => a.trendChangePoints - b.trendChangePoints);
    const stable = data.depots.filter(depot => depot.trendDirection === 'stable').length;
    const unavailable = data.depots.filter(depot => depot.trendDirection === 'unavailable').length;
    $('floor-trend-summary').innerHTML = `<div><span>En amélioration</span><strong>${number.format(positive.length)}</strong></div><div><span>En diminution</span><strong>${number.format(negative.length)}</strong></div><div><span>Stables</span><strong>${number.format(stable)}</strong></div>${unavailable ? `<div><span>Données insuffisantes</span><strong>${number.format(unavailable)}</strong></div>` : ''}`;
    $('positive-trend-count').textContent = number.format(positive.length);
    $('negative-trend-count').textContent = number.format(negative.length);
    $('positive-trend-list').innerHTML = positive.length ? positive.map(trendRow).join('') : '<p class="floor-trend-empty">Aucun dépôt en hausse marquée.</p>';
    $('negative-trend-list').innerHTML = negative.length ? negative.map(trendRow).join('') : '<p class="floor-trend-empty">Aucun dépôt en baisse marquée.</p>';
    $('floor-scan-grid').innerHTML = data.depots.length ? data.depots.map(depot => {
      const maximum = Math.max(1, ...depot.days.map(day => day.parcels));
      const gapClass = depot.weekdaysWithoutScans >= 5 ? ' large-gap' : depot.weekdaysWithoutScans > 0 ? ' has-gap' : '';
      const gapLabel = depot.weekdaysWithoutScans === 0 ? 'Aucun jour sans scan plancher' : `${number.format(depot.weekdaysWithoutScans)} jour${depot.weekdaysWithoutScans > 1 ? 's' : ''} sans scan plancher`;
      const latest = depot.latestScan ? `Dernier scan : ${new Date(depot.latestScan).toLocaleString('fr-CA', { timeZone: 'America/Toronto', dateStyle: 'medium', timeStyle: 'short' })}` : 'Aucun scan durant la période';
      const bars = depot.days.map(day => {
        const height = day.parcels ? Math.max(3, day.parcels / maximum * 100) : 0;
        const label = `${dayName.format(parseDate(day.date))} ${formatDate(day.date)} : ${number.format(day.parcels)} colis${day.partial ? ' · journée en cours' : ''}`;
        return `<div class="floor-day${day.partial ? ' partial' : ''}${day.parcels === 0 ? ' zero' : ''}" title="${escape(label)}" aria-label="${escape(label)}">
          <span class="floor-day-value">${number.format(day.parcels)}</span><span class="floor-bar-track"><i class="floor-bar" style="height:${height}%"></i></span>
          <small>${escape(dayName.format(parseDate(day.date)))}<br>${day.date.slice(8, 10)}</small></div>`;
      }).join('');
      return `<article class="floor-scan-card${gapClass}">
        <div class="floor-scan-head"><div><h3>${escape(depot.depotName)}</h3><small>Dépôt ${depot.depotId} · ${escape(depot.depotShortLabel)}</small></div><div class="floor-scan-total"><strong>${number.format(depot.sevenDayTotal)}</strong>7 jours</div></div>
        <div class="floor-bars" role="img" aria-label="Scans plancher des sept derniers jours pour ${escape(depot.depotName)}">${bars}</div>
        <div class="floor-scan-gap"><strong>${gapLabel}</strong><small>sur ${number.format(depot.weekdaysObserved)} jours ouvrables · ${escape(latest)}</small></div>
      </article>`;
    }).join('') : '<div class="floor-scan-loading">Aucun dépôt actif disponible.</div>';
  }
  async function load() {
    const version = ++requestVersion;
    $('refresh-button').disabled = true;
    $('live-dot').className = 'live-dot waiting';
    $('live-label').textContent = 'Chargement…';
    try {
      const response = await fetch('/api/floor-scans?' + new URLSearchParams({ date: selectedDate }), { cache: 'no-store' });
      if (!response.ok) throw new Error('Données indisponibles');
      const data = await response.json();
      if (version !== requestVersion || data.date !== selectedDate) return;
      render(data);
      $('error-banner').hidden = true;
      $('live-dot').className = 'live-dot ok';
      $('live-label').textContent = 'En ligne';
      $('last-refresh').textContent = `Actualisé à ${time.format(new Date())}`;
    } catch {
      if (version !== requestVersion) return;
      $('error-banner').textContent = 'Les scans plancher sont indisponibles. Nouvelle tentative dans 60 secondes.';
      $('error-banner').hidden = false;
      $('live-dot').className = 'live-dot error';
      $('live-label').textContent = 'Erreur';
    } finally {
      if (version === requestVersion) $('refresh-button').disabled = false;
      countdown = REFRESH_SECONDS;
      $('countdown').textContent = countdown;
    }
  }
  setDate(selectedDate);
  $('previous-date').addEventListener('click', () => moveDate(-1));
  $('next-date').addEventListener('click', () => moveDate(1));
  $('analysis-date').addEventListener('change', event => { if (event.target.value) { setDate(event.target.value); load(); } });
  $('refresh-button').addEventListener('click', load);
  timer = setInterval(() => {
    countdown--;
    if (countdown <= 0) load();
    $('countdown').textContent = Math.max(0, countdown);
  }, 1000);
  addEventListener('beforeunload', () => clearInterval(timer), { once: true });
  load();
})();
