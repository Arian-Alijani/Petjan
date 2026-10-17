/* =========================================================
   VetPlus — Personalized dashboard router
   Renders role-specific HTML from dashboard-views.js into
   #dash-root, rebinds shared micro-interactions, and drives
   the temporary role-switcher (demo button).
   ========================================================= */
(function () {
  'use strict';

  var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var fa = window.toFa || function (v) { return String(v); };

  function group(n) {
    return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, '٬');
  }
  function faNum(n, dec) {
    var s = dec ? Number(n).toFixed(dec).replace('.', '٫') : group(Math.round(n));
    return fa(s);
  }
  function easeOut(t) { return 1 - Math.pow(1 - t, 3); }
  function animate(dur, step, done) {
    if (reduceMotion) { step(1); if (done) done(); return; }
    var t0 = performance.now();
    (function frame(now) {
      var p = Math.min(1, (now - t0) / dur);
      step(easeOut(p));
      if (p < 1) requestAnimationFrame(frame); else if (done) done();
    })(t0);
  }

  var root = document.getElementById('dash-root');
  if (!root || !window.DASH_VIEWS) return;

  /* ---------- role state ---------- */
  var role = 'manager';
  try { role = sessionStorage.getItem('vp_dash_role') || 'manager'; } catch (e) {}
  var TITLES = {
    manager: { title: 'صبح بخیر، دکتر آریا', crumbs: ['مرکز فرمان', 'داشبورد'] },
    cashier: { title: 'صندوق و مالی', crumbs: ['مدیریت کلینیک', 'صندوق'] },
    doctor:  { title: 'روز کاری شما', crumbs: ['ویزیت', 'داشبورد پزشک'] },
  };

  /* ---------- render ---------- */
  function render() {
    root.innerHTML = window.DASH_VIEWS[role]();
    mountLayout({ active: 'dashboard', title: TITLES[role].title, crumbs: TITLES[role].crumbs, role: role });
    bindAll();
    updateSwitcher();
  }

  window.DASH = {
    setRole: function (r) {
      if (!window.DASH_VIEWS[r]) return;
      role = r;
      try { sessionStorage.setItem('vp_dash_role', r); } catch (e) {}
      render();
      window.scrollTo({ top: 0, behavior: reduceMotion ? 'auto' : 'smooth' });
    }
  };

  /* ---------- role switcher (temporary demo control) ---------- */
  var switcher;
  function ensureSwitcher() {
    if (switcher) return;
    switcher = document.createElement('div');
    switcher.className = 'db-switcher';
    switcher.innerHTML =
      '<span class="db-sw-label">پیش‌نمایش نقش‌ها</span>' +
      '<div class="db-sw-btns" role="tablist">' +
        '<button data-role="manager" class="active">مدیر</button>' +
        '<button data-role="cashier">صندوق</button>' +
        '<button data-role="doctor">پزشک</button>' +
      '</div>';
    document.body.appendChild(switcher);
    switcher.querySelectorAll('button').forEach(function (b) {
      b.addEventListener('click', function () { window.DASH.setRole(b.dataset.role); });
    });
  }
  function updateSwitcher() {
    if (!switcher) return;
    switcher.querySelectorAll('button').forEach(function (b) {
      b.classList.toggle('active', b.dataset.role === role);
    });
  }

  /* ---------- shared binding (re-run per render) ---------- */
  function bindAll() {
    /* 1. reveal on scroll */
    var revealables = root.querySelectorAll('.reveal');
    if ('IntersectionObserver' in window && !reduceMotion) {
      var io = new IntersectionObserver(function (entries) {
        entries.forEach(function (e, i) {
          if (!e.isIntersecting) return;
          var el = e.target;
          setTimeout(function () { el.classList.add('in'); }, i * 70);
          io.unobserve(el);
        });
      }, { threshold: 0.12 });
      revealables.forEach(function (el) { io.observe(el); });
    } else {
      revealables.forEach(function (el) { el.classList.add('in'); });
    }

    /* 2. counters */
    var counters = root.querySelectorAll('[data-count]');
    if ('IntersectionObserver' in window && !reduceMotion) {
      var io2 = new IntersectionObserver(function (entries) {
        entries.forEach(function (e) {
          if (!e.isIntersecting) return;
          runCounter(e.target);
          io2.unobserve(e.target);
        });
      }, { threshold: 0.4 });
      counters.forEach(function (el) { io2.observe(el); });
    } else {
      counters.forEach(runCounter);
    }

    /* 3. stat sparklines */
    root.querySelectorAll('.stat[data-spark]').forEach(function (card) {
      var vals = card.dataset.spark.split(',').map(Number);
      var color = card.dataset.sparkColor || '#14b8a6';
      var w = 200, h = 40, pad = 4;
      var max = Math.max.apply(null, vals), min = Math.min.apply(null, vals);
      var span = (max - min) || 1;
      var pts = vals.map(function (v, i) {
        return [w - pad - i * ((w - pad * 2) / (vals.length - 1)), h - pad - ((v - min) / span) * (h - pad * 2 - 4)];
      });
      var d = smooth(pts);
      var id = 'sp' + Math.random().toString(36).slice(2, 8);
      var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('class', 'spark');
      svg.setAttribute('viewBox', '0 0 ' + w + ' ' + h);
      svg.setAttribute('preserveAspectRatio', 'none');
      svg.innerHTML =
        '<defs><linearGradient id="' + id + '" x1="0" x2="0" y1="0" y2="1">' +
        '<stop offset="0%" stop-color="' + color + '" stop-opacity=".22"/>' +
        '<stop offset="100%" stop-color="' + color + '" stop-opacity="0"/></linearGradient></defs>' +
        '<path d="' + d + ' L' + pts[pts.length - 1][0] + ',' + h + ' L' + pts[0][0] + ',' + h + ' Z" fill="url(#' + id + ')"/>' +
        '<path class="line" d="' + d + '" fill="none" stroke="' + color + '" stroke-width="2" stroke-linecap="round" opacity=".75"/>';
      card.appendChild(svg);
    });

    /* 4. quick-tile ripple */
    root.querySelectorAll('.quick-tile').forEach(function (el) {
      el.addEventListener('pointerdown', function (e) {
        var r = el.getBoundingClientRect();
        var ink = document.createElement('span');
        var size = Math.max(r.width, r.height);
        ink.className = 'ripple-ink';
        ink.style.width = ink.style.height = size + 'px';
        ink.style.left = (e.clientX - r.left - size / 2) + 'px';
        ink.style.top = (e.clientY - r.top - size / 2) + 'px';
        ink.style.background = 'rgba(13,148,136,.18)';
        el.appendChild(ink);
        setTimeout(function () { ink.remove(); }, 620);
      });
    });

    /* 5. species donut (manager only — elements exist or not) */
    bindDonut();

    /* 6. quick tiles (manager only) */
    bindTiles();

    /* 7. revenue chart (manager only) */
    bindChart();

    /* 8. queue rows keyboard */
    root.querySelectorAll('.db-queue-row[tabindex], .db-live-row[tabindex], .db-vax-row[tabindex], .db-draft-row[tabindex]').forEach(function (row) {
      row.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); row.click(); } });
    });

    /* 9. doctor live clock */
    bindClock();
  }

  /* ---------- live clock (doctor hero) ---------- */
  var clockTimer = null;
  function bindClock() {
    if (clockTimer) { clearInterval(clockTimer); clockTimer = null; }
    var el = root.querySelector('[data-clock]');
    if (!el) return;
    function tick() {
      var d = new Date();
      var hh = String(d.getHours()).padStart(2, '0');
      var mm = String(d.getMinutes()).padStart(2, '0');
      el.textContent = fa(hh + ':' + mm);
    }
    tick();
    if (!reduceMotion) clockTimer = setInterval(tick, 10000);
  }

  function runCounter(el) {
    var to = parseFloat(el.dataset.count);
    var dec = parseInt(el.dataset.dec || '0', 10);
    var suffix = el.dataset.suffix || '';
    animate(1100, function (p) {
      el.textContent = faNum(to * p, dec) + suffix;
    });
  }

  /* Catmull-Rom → cubic bezier smoothing */
  function smooth(p) {
    if (p.length < 2) return '';
    var d = 'M' + p[0][0].toFixed(1) + ',' + p[0][1].toFixed(1);
    for (var i = 0; i < p.length - 1; i++) {
      var p0 = p[i - 1] || p[i], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2] || p2;
      var c1x = p1[0] + (p2[0] - p0[0]) / 6, c1y = p1[1] + (p2[1] - p0[1]) / 6;
      var c2x = p2[0] - (p3[0] - p1[0]) / 6, c2y = p2[1] - (p3[1] - p1[1]) / 6;
      d += ' C' + c1x.toFixed(1) + ',' + c1y.toFixed(1) + ' ' + c2x.toFixed(1) + ',' + c2y.toFixed(1) +
           ' ' + p2[0].toFixed(1) + ',' + p2[1].toFixed(1);
    }
    return d;
  }

  /* ---------- species donut (from old dashboard, scoped) ---------- */
  var SPECIES = [
    { key: 'dog',    name: 'سگ',            count: 578, color: '#0d9488', icon: 'dog' },
    { key: 'cat',    name: 'گربه',           count: 398, color: '#b8a2e6', icon: 'cat' },
    { key: 'bird',   name: 'پرنده',          count: 154, color: '#f0b593', icon: 'bird' },
    { key: 'rodent', name: 'خرگوش و جوندگان', count: 103, color: '#8fb5f0', icon: 'rabbit' },
    { key: 'other',  name: 'سایر گونه‌ها',    count: 51,  color: '#cfd9d6', icon: 'paw' }
  ];

  function bindDonut() {
    var segsG = document.getElementById('donutSegs');
    var legendEl = document.getElementById('speciesLegend');
    if (!segsG || !legendEl) return;
    var total = SPECIES.reduce(function (s, x) { return s + x.count; }, 0);
    var R = 48, SW = 13, C = 2 * Math.PI * R, GAP = 3.4, offset = 0;

    SPECIES.forEach(function (s, i) {
      var len = (s.count / total) * C;
      var c = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      c.setAttribute('class', 'seg');
      c.setAttribute('cx', 60); c.setAttribute('cy', 60); c.setAttribute('r', R);
      c.setAttribute('stroke', s.color);
      c.setAttribute('stroke-width', SW);
      c.setAttribute('stroke-dasharray', '0 ' + C);
      c.setAttribute('stroke-dashoffset', -(offset + GAP / 2));
      c.dataset.i = i;
      segsG.appendChild(c);
      var target = Math.max(1.2, len - GAP);
      setTimeout(function () {
        animate(900, function (p) { c.setAttribute('stroke-dasharray', (target * p) + ' ' + C); });
      }, 120 + i * 110);
      offset += len;

      var row = document.createElement('div');
      row.className = 'legend-row';
      row.dataset.i = i;
      row.innerHTML =
        '<span class="legend-fill" style="--c:' + s.color + '"></span>' +
        '<span class="swatch" style="background:' + s.color + '"></span>' +
        '<span class="lg-name">' + ((window.Icons && Icons[s.icon]) || '') + s.name + '</span>' +
        '<span class="lg-count num-fa">' + fa(group(s.count)) + '</span>' +
        '<span class="lg-pct num-fa">' + fa(Math.round(s.count / total * 100)) + '٪</span>';
      legendEl.appendChild(row);
      setTimeout(function () {
        row.querySelector('.legend-fill').style.width = Math.max(9, s.count / total * 100) + '%';
      }, 200 + i * 110);
    });

    var wrap = document.getElementById('donutWrap');
    var dcVal = document.getElementById('dcVal'), dcLbl = document.getElementById('dcLbl'), dcSub = document.getElementById('dcSub');
    var segs = segsG.querySelectorAll('.seg');
    var rows = legendEl.querySelectorAll('.legend-row');

    function focus(i) {
      wrap.classList.add('dim');
      segs.forEach(function (s, k) {
        var on = k === i;
        s.classList.toggle('on', on);
        s.setAttribute('stroke-width', on ? SW + 4 : SW);
      });
      rows.forEach(function (r, k) { r.classList.toggle('on', k === i); });
      var s = SPECIES[i];
      dcVal.textContent = fa(group(s.count));
      dcVal.style.color = s.color;
      dcLbl.textContent = s.name;
      dcSub.textContent = fa(Math.round(s.count / total * 100)) + '٪ از کل بیماران';
    }
    function blur() {
      wrap.classList.remove('dim');
      segs.forEach(function (s) { s.classList.remove('on'); s.setAttribute('stroke-width', SW); });
      rows.forEach(function (r) { r.classList.remove('on'); });
      dcVal.textContent = fa(group(total));
      dcVal.style.color = '';
      dcLbl.textContent = 'کل بیماران فعال';
      dcSub.textContent = fa(SPECIES.length) + ' گونه ثبت‌شده';
    }
    blur();
    [].concat([].slice.call(segs), [].slice.call(rows)).forEach(function (el) {
      el.addEventListener('mouseenter', function () { focus(+el.dataset.i); });
      el.addEventListener('mouseleave', blur);
      el.addEventListener('click', function () { navigateTo('patients.html'); });
    });
  }

  /* ---------- quick tiles (manager only) ---------- */
  var TILES = [
    { t: 'نوبت جدید',    s: 'ثبت در تقویم',   href: 'appointments.html', icon: 'calendar',  c: '#0d9488', bg: '#f0fdfa' },
    { t: 'پرونده بیمار',  s: 'جستجو و ثبت',    href: 'patients.html',     icon: 'users',     c: '#7b5cc4', bg: '#ece5fb' },
    { t: 'شروع ویزیت',   s: 'ثبت معاینه',      href: 'visits.html',       icon: 'clipboard', c: '#1570cd', bg: '#eff8ff' },
    { t: 'واکسیناسیون',  s: '۶ یادآور امروز',  href: 'vaccination.html',  icon: 'syringe',   c: '#027a48', bg: '#ecfdf3' },
    { t: 'فروش و صندوق', s: 'صدور فاکتور',    href: 'sale.html',         icon: 'cart',      c: '#d9803f', bg: '#fbe7d6' },
    { t: 'انبار و کالا',  s: 'کنترل موجودی',   href: 'inventory.html',    icon: 'box',       c: '#b54708', bg: '#fffaeb', badge: '۳' },
    { t: 'خرید و تأمین', s: 'سفارش تأمین',     href: 'purchase.html',     icon: 'truck',     c: '#14b8a6', bg: '#e5f4f0' },
    { t: 'گزارش‌ها',     s: 'تحلیل عملکرد',   href: 'reports.html',      icon: 'chart',     c: '#2f6f8f', bg: '#e2eff5' }
  ];

  function bindTiles() {
    var grid = document.getElementById('quickGrid');
    if (!grid) return;
    TILES.forEach(function (x, i) {
      var a = document.createElement('a');
      a.className = 'quick-tile';
      a.href = x.href;
      a.style.setProperty('--tile-color', x.c);
      a.style.setProperty('--tile-soft', x.bg);
      a.innerHTML =
        '<kbd class="qt-key">Alt+' + fa(i + 1) + '</kbd>' +
        '<span class="qt-icon">' + ((window.Icons && Icons[x.icon]) || '') +
          (x.badge ? '<span class="qt-badge num-fa">' + x.badge + '</span>' : '') +
        '</span>' +
        '<span class="qt-title">' + x.t + '</span>' +
        '<span class="qt-sub">' + x.s +
          '<span class="qt-arrow"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><line x1="19" y1="12" x2="5" y2="12"/><polyline points="12 19 5 12 12 5"/></svg></span>' +
        '</span>';
      grid.appendChild(a);
    });
  }
  /* Alt+N quick-tile navigation — bound once per page, not per render */
  if (!window.__dashAltBound) {
    window.__dashAltBound = true;
    document.addEventListener('keydown', function (e) {
      if (!e.altKey) return;
      if (!document.getElementById('quickGrid')) return;
      var n = parseInt(e.key, 10);
      if (n >= 1 && n <= TILES.length) { e.preventDefault(); navigateTo(TILES[n - 1].href); }
    });
  }

  /* ---------- revenue pulse chart (manager only) ---------- */
  var RANGES = {
    day: {
      labels: ['۹', '۱۰', '۱۱', '۱۲', '۱۴', '۱۵', '۱۶', '۱۷'],
      values: [1.2, 2.4, 1.8, 3.1, 2.2, 3.4, 2.1, 2.2],
      unit: 'ساعت', delta: '۶٫۲٪ نسبت به دیروز'
    },
    week: {
      labels: ['شنبه', 'یک‌شنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنج‌شنبه', 'جمعه'],
      values: [10.2, 9.4, 13.1, 11.8, 14.6, 12.9, 14.45],
      unit: 'روز', delta: '۱۲٫۴٪ نسبت به هفته پیش'
    },
    month: {
      labels: ['هفته ۱', 'هفته ۲', 'هفته ۳', 'هفته ۴'],
      values: [78.2, 84.6, 91.3, 86.45],
      unit: 'هفته', delta: '۸٫۹٪ نسبت به ماه پیش'
    },
    year: {
      labels: ['فرو', 'ارد', 'خرد', 'تیر', 'مرد', 'شهر', 'مهر', 'آبا', 'آذر', 'دی', 'بهم', 'اسف'],
      values: [280, 310, 295, 330, 352, 341, 368, 392, 375, 410, 398, 341],
      unit: 'ماه', delta: '۲۱٫۷٪ نسبت به سال پیش'
    }
  };

  function bindChart() {
    var holder = document.getElementById('chartHolder');
    if (!holder) return;
    var W = 500, H = 180, PAD_X = 8, TOP = 16, BOTTOM = 18, M = 56;
    var lineEl = document.getElementById('chartLine');
    var areaEl = document.getElementById('chartArea');
    var gridEl = document.getElementById('chartGrid');
    var yAxis = document.getElementById('chartY');
    var dotsEl = document.getElementById('chartDots');
    var xAxis = document.getElementById('chartX');
    var tip = document.getElementById('chartTip');
    var hLine = document.getElementById('hoverLine');
    var hDot = document.getElementById('hoverDot');
    var valEl = document.getElementById('pulseValue');
    var deltaEl = document.getElementById('pulseDelta');
    var current = null;
    var GRID = 5; // ۵ فاصله → ۶ خط افقی (مثل محور ۰…۱۰۰ نمونه)

    // محدودهٔ عمودیِ داده را حساب می‌کند تا خطوط شبکه و نقاط هم‌تراز شوند.
    function spanFor(vals) {
      var max = Math.max.apply(null, vals), min = Math.min.apply(null, vals);
      var pad = (max - min) * 0.28 + 0.001;
      var hi = max + pad, lo = min - (max - min) * 0.5;
      return { hi: hi, lo: lo };
    }
    function yOf(v, sp) {
      return TOP + (1 - (v - sp.lo) / (sp.hi - sp.lo)) * (H - TOP - BOTTOM);
    }
    function pointsFor(vals) {
      var n = vals.length, sp = spanFor(vals);
      var step = (W - PAD_X * 2) / (n - 1);
      return vals.map(function (v, i) {
        return [W - PAD_X - i * step, yOf(v, sp)];
      });
    }
    function resample(pts) {
      var out = [];
      for (var k = 0; k < M; k++) {
        var t = k / (M - 1) * (pts.length - 1);
        var i = Math.floor(t), f = t - i;
        var a = pts[i], b = pts[Math.min(pts.length - 1, i + 1)];
        out.push(a[1] + (b[1] - a[1]) * f);
      }
      return out;
    }
    function drawFromYs(ys) {
      var pts = ys.map(function (y, k) { return [W - PAD_X - k * ((W - PAD_X * 2) / (M - 1)), y]; });
      var d = smooth(pts);
      lineEl.setAttribute('d', d);
      areaEl.setAttribute('d', d + ' L' + PAD_X + ',' + H + ' L' + (W - PAD_X) + ',' + H + ' Z');
    }

    // خطوط شبکهٔ افقی یکدست + اعداد محور Y (هم‌تراز با محدودهٔ داده)
    function drawGrid(sp, maxVal) {
      var gl = '', ya = '';
      for (var i = 0; i <= GRID; i++) {
        var frac = i / GRID;                         // از پایین به بالا
        var y = H - BOTTOM - frac * (H - TOP - BOTTOM);
        gl += '<line x1="0" y1="' + y.toFixed(1) + '" x2="' + W + '" y2="' + y.toFixed(1) + '"/>';
        // عدد محور بر مبنای درصدِ بیشینهٔ داده (۰…۱۰۰ وار)
        var lbl = Math.round(frac * 100);
        var topPct = ((y - TOP) / (H - TOP - BOTTOM) * 100);
        ya += '<span style="top:' + topPct.toFixed(2) + '%">' + fa(lbl) + '</span>';
      }
      gridEl.innerHTML = gl;
      if (yAxis) yAxis.innerHTML = ya;
    }

    // نقاط دایره‌ایِ توخالی روی هر داده (overlay HTML تا همیشه گرد بمانند)
    function drawDots(pts) {
      if (!dotsEl) return;
      dotsEl.innerHTML = pts.map(function (p, i) {
        var right = (W - p[0]) / W * 100;
        var top = p[1] / H * 100;
        return '<span class="cdot" data-i="' + i + '" style="right:' + right.toFixed(2) +
               '%;top:' + top.toFixed(2) + '%"></span>';
      }).join('');
    }

    function renderRange(key, animateIt) {
      var r = RANGES[key];
      var pts = pointsFor(r.values);
      var sp = spanFor(r.values);
      holder._pts = pts;
      holder._data = r;
      drawGrid(sp, Math.max.apply(null, r.values));
      drawDots(pts);
      var target = resample(pts);
      if (!current || !animateIt) {
        current = target.slice();
        drawFromYs(current);
      } else {
        var from = current.slice();
        animate(650, function (p) {
          current = from.map(function (y, i) { return y + (target[i] - y) * p; });
          drawFromYs(current);
        });
      }
      xAxis.innerHTML = r.labels.map(function (l, i) {
        var right = (W - pts[i][0]) / W * 100;
        return '<span style="right:' + right.toFixed(2) + '%">' + l + '</span>';
      }).join('');
      var totalV = r.values.reduce(function (a, b) { return a + b; }, 0);
      var from0 = parseFloat(valEl.dataset.v || '0');
      valEl.dataset.v = totalV;
      animate(700, function (p) {
        valEl.textContent = fa(group(Math.round((from0 + (totalV - from0) * p) * 1e6)));
      });
      deltaEl.textContent = r.delta;
    }

    renderRange('week', false);

    root.querySelectorAll('#rangeTabs button').forEach(function (b) {
      b.addEventListener('click', function () {
        root.querySelectorAll('#rangeTabs button').forEach(function (x) { x.classList.remove('active'); });
        b.classList.add('active');
        renderRange(b.dataset.range, true);
        holder.classList.remove('active');
      });
    });

    var plot = holder.querySelector('.chart-plot') || holder;
    function moveHover(clientX) {
      var r = plot.getBoundingClientRect();
      var pts = holder._pts, data = holder._data;
      if (!pts || !r.width) return;
      var px = (clientX - r.left) / r.width * W;
      var best = 0, bd = Infinity;
      pts.forEach(function (p, i) { var d = Math.abs(p[0] - px); if (d < bd) { bd = d; best = i; } });
      var p = pts[best];
      var leftPx = p[0] / W * r.width;
      var topPx = p[1] / H * r.height;
      holder.classList.add('active');
      hLine.style.left = leftPx + 'px';
      hLine.style.height = Math.max(0, (H - BOTTOM) / H * r.height - topPx) + 'px';
      hLine.style.top = topPx + 'px';
      hDot.style.left = leftPx + 'px';
      hDot.style.top = topPx + 'px';
      tip.style.left = leftPx + 'px';
      tip.style.top = topPx + 'px';
      tip.innerHTML =
        '<span class="tip-lbl">' + data.labels[best] + '</span>' +
        '<b class="tip-val num-fa">' + fa(String(data.values[best]).replace('.', '٫')) + ' م تومان</b>' +
        '<i class="tip-caret"></i>';
      [].slice.call(xAxis.children).forEach(function (s, i) { s.classList.toggle('on', i === best); });
      if (dotsEl) [].slice.call(dotsEl.children).forEach(function (s, i) { s.classList.toggle('on', i === best); });
    }
    plot.addEventListener('pointermove', function (e) { moveHover(e.clientX); });
    plot.addEventListener('pointerleave', function () {
      holder.classList.remove('active');
      [].slice.call(xAxis.children).forEach(function (s) { s.classList.remove('on'); });
      if (dotsEl) [].slice.call(dotsEl.children).forEach(function (s) { s.classList.remove('on'); });
    });
  }

  /* ---------- boot ---------- */
  ensureSwitcher();
  render();
})();
