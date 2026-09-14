(function () {
  const $ = id => document.getElementById(id);
  const button = $('forecast-chart-open');
  const dialog = $('forecast-chart-dialog');
  if (!dialog) return;
  const number = new Intl.NumberFormat('fr-CA');
  const dateLabel = new Intl.DateTimeFormat('fr-CA', { day: 'numeric', month: 'short' });
  const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
  const series = [
    { key: 'low', name: 'Minimum historique', color: '#ab8cff', width: 2, dash: '7 5' },
    { key: 'high', name: 'Maximum historique', color: '#ffbd59', width: 2, dash: '7 5' },
    { key: 'predicted', name: 'Statistique', color: '#51a7ff', width: 3, dash: '' },
    { key: 'mlPredicted', name: 'ML.NET', color: '#ff77c8', width: 3, dash: '' },
    { key: 'actual', name: 'Réel', color: '#38dc9a', width: 5, dash: '' }
  ];
  let rows = [], snapshot = null;
  let sectorPayload = null, selectedSectorId = null, sectorSubtitle = '';
  const valueLabel = value => value == null ? 'indisponible' : number.format(value);
  const describe = row => `${row.label} : statistique ${valueLabel(row.predicted)}${selectedSectorId == null ? ` · ML.NET ${valueLabel(row.mlPredicted)}` : ''} · réel ${valueLabel(row.actual)} · minimum ${valueLabel(row.low)} · maximum ${valueLabel(row.high)} colis.`;
  function render() {
    const width = 1000, height = 450, left = 85, right = 32, top = 35, bottom = 65;
    const plotWidth = width - left - right, plotHeight = height - top - bottom;
    const values = rows.flatMap(row => series.map(s => row[s.key])).filter(v => v != null);
    const largest = Math.max(1, ...values);
    const rough = largest / 5, power = Math.pow(10, Math.floor(Math.log10(rough)));
    const step = Math.max(1, [1, 2, 5, 10].map(x => x * power).find(x => x >= rough));
    const ceiling = Math.ceil(largest / step) * step;
    const x = index => left + index * plotWidth / Math.max(1, rows.length - 1);
    const y = value => top + plotHeight * (1 - value / ceiling);
    let svg = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-labelledby="forecast-chart-svg-title forecast-chart-svg-desc"><title id="forecast-chart-svg-title">Prévu, réel et minimum–maximum historiques</title><desc id="forecast-chart-svg-desc">Nombre de colis par jour pour la version sélectionnée. Les valeurs manquantes interrompent les lignes.</desc>`;
    svg += `<text x="${left}" y="18" class="chart-axis">Colis</text>`;
    for (let value = 0; value <= ceiling; value += step) {
      svg += `<line x1="${left}" x2="${width - right}" y1="${y(value)}" y2="${y(value)}" class="chart-grid"/><text x="${left - 12}" y="${y(value) + 4}" text-anchor="end" class="chart-axis">${number.format(value)}</text>`;
    }
    rows.forEach((row, index) => {
      svg += `<text x="${x(index)}" y="${height - 35}" text-anchor="middle" class="chart-axis">${escape(row.label)}</text>`;
    });
    series.forEach(s => {
      let path = '', connected = false;
      rows.forEach((row, index) => {
        if (row[s.key] == null) { connected = false; return; }
        path += `${connected ? 'L' : 'M'}${x(index)},${y(row[s.key])} `;
        connected = true;
      });
      if (path) svg += `<path data-series="${s.key}" d="${path.trim()}" fill="none" stroke="${s.color}" stroke-width="${s.width}" ${s.dash ? `stroke-dasharray="${s.dash}"` : ''} stroke-linejoin="round" stroke-linecap="round"/>`;
      rows.forEach((row, index) => {
        if (row[s.key] != null) svg += `<circle data-series-point="${s.key}" cx="${x(index)}" cy="${y(row[s.key])}" r="${s.key === 'actual' ? 5 : 3}" fill="${s.color}"/>`;
      });
    });
    rows.forEach((row, index) => {
      const band = plotWidth / Math.max(1, rows.length - 1);
      const start = Math.max(left - 12, x(index) - band / 2);
      const end = Math.min(width - right + 12, x(index) + band / 2);
      svg += `<g data-day="${index}" tabindex="0" role="group" aria-label="${escape(describe(row))}" class="chart-hit"><rect x="${start}" y="${top}" width="${end - start}" height="${plotHeight + 5}" fill="transparent"/><title>${escape(describe(row))}</title></g>`;
    });
    svg += '</svg>';
    $('forecast-chart-plot').innerHTML = svg;
    $('forecast-chart-title').textContent = selectedSectorId == null ? 'Prévisions et réel' : `Livraisons · secteur ${selectedSectorId}`;
    $('forecast-chart-version').textContent = selectedSectorId != null ? sectorSubtitle : snapshot ? `Version ${snapshot.id} · ${new Date(snapshot.savedAt).toLocaleString('fr-CA', { timeZone: 'America/Toronto' })}` : '';
    $('forecast-chart-readout').textContent = 'Survolez une journée ou sélectionnez-la au clavier pour afficher les valeurs.';
    $('forecast-chart-note').textContent = 'Min.–max. : volumes historiques des journées comparables, pas un intervalle de confiance. Les données manquantes ne sont pas remplacées par zéro.'
      + (selectedSectorId == null ? '' : ' Réel : colis uniques triés à Saint-Hubert affectés au jour de livraison.')
      + (rows.every(row => row.actual == null) ? ' Aucun réel évaluable pour cette version pour le moment.' : '');
  }
  globalThis.updateEdiForecastChart = function (forecast, archive) {
    snapshot = archive?.snapshot || null;
    const comparisons = new Map((archive?.comparisons || []).filter(row => row.snapshotId === snapshot?.id).map(row => [row.date, row]));
    const mlDays = new Map((snapshot?.mlForecast?.days || []).map(day => [day.date, day]));
    const days = [...(forecast?.days || [])];
    if (archive?.previousDay?.day && !days.some(day => day.date === archive.previousDay.day.date)) {
      days.unshift(archive.previousDay.day);
      comparisons.set(archive.previousDay.day.date, archive.previousDay.comparison);
      if (archive.previousDay.mlDay) mlDays.set(archive.previousDay.day.date, archive.previousDay.mlDay);
    }
    rows = days.map(day => ({ label: dateLabel.format(new Date(day.date + 'T12:00:00')),
      predicted: day.parcels, mlPredicted: mlDays.get(day.date)?.parcels ?? null,
      actual: comparisons.get(day.date)?.actual ?? null, low: day.historicalLow, high: day.historicalHigh }));
    if (button) button.disabled = !rows.length;
    if (dialog.open) render();
  };
  function selectSector(id) {
    const sector = sectorPayload?.sectors.find(row => row.sectorId === id);
    if (!sector) return false;
    selectedSectorId = id;
    const actuals = new Map((sector.actuals || []).map(day => [day.date, day.parcels]));
    const days = sector.forecast.days.filter(day => ![0, 6].includes(new Date(day.date + 'T12:00:00').getDay()));
    rows = days.map(day => ({ label: dateLabel.format(new Date(day.date + 'T12:00:00')),
      predicted: day.parcels, actual: actuals.get(day.date) ?? null, low: day.historicalLow, high: day.historicalHigh }));
    sectorSubtitle = days.length ? `Du ${rows[0].label} au ${rows[rows.length - 1].label} · ` : '';
    sectorSubtitle += sectorPayload.weekly?.forecastAvailable === false ? 'Aucune prévision sauvegardée pour cette semaine'
      : `Prévision figée le ${new Date(sectorPayload.savedAt).toLocaleString('fr-CA', { timeZone: 'America/Toronto' })}`;
    render();
    return true;
  }
  globalThis.updateSectorForecastCharts = function (data) {
    sectorPayload = data;
    if (dialog.open && selectedSectorId != null && !selectSector(selectedSectorId)) dialog.close();
  };
  $('sector-body')?.addEventListener('click', event => {
    const target = event.target.closest?.('[data-sector-chart]');
    if (target && selectSector(Number(target.dataset.sectorChart)) && !dialog.open) dialog.showModal();
  });
  button?.addEventListener('click', () => { render(); dialog.showModal(); });
  $('forecast-chart-close').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => {
    if (event.target !== dialog) return;
    const rect = dialog.getBoundingClientRect();
    if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) dialog.close();
  });
  const inspect = event => {
    const target = event.target.closest?.('[data-day]');
    if (target && rows[Number(target.dataset.day)]) $('forecast-chart-readout').textContent = describe(rows[Number(target.dataset.day)]);
  };
  $('forecast-chart-plot').addEventListener('pointerover', inspect);
  $('forecast-chart-plot').addEventListener('focusin', inspect);
  $('forecast-chart-plot').addEventListener('click', inspect);
})();
