/* =========================================================
   وت‌پلاس · پانسیون (Boarding)
   assets/boarding.js — full interactive controller
   Static UI prototype (L1–L3): all mutations are in-memory;
   every action shows a "(نمونه طراحی)" toast.
   ========================================================= */
(function () {
  'use strict';

  const $  = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const C  = () => window.CLINIC;

  /* ── toast ──────────────────────────────────────────────── */
  if (typeof window.toast !== 'function') {
    window.toast = function (msg, icon = 'check') {
      let host = document.querySelector('.toast-host');
      if (!host) {
        host = document.createElement('div');
        host.className = 'toast-host';
        document.body.appendChild(host);
      }
      const el = document.createElement('div');
      el.className = 'toast' + (icon === 'x' ? ' error' : '');
      el.innerHTML = (window.Icons && window.Icons[icon] ? window.Icons[icon] : '') + '<span></span>';
      el.lastChild.textContent = msg;
      host.appendChild(el);
      setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 320); }, 2700);
    };
  }
  const toast = (m, i) => window.toast(m, i);

  /* ── helpers ────────────────────────────────────────────── */
  const fa    = (v) => (window.toFa ? window.toFa(v) : String(v));
  const money = (n) => fa(Number(n || 0).toLocaleString('en-US')) + ' ریال';
  const esc   = (s) => String(s == null ? '' : s).replace(/[&<>"]/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  /* ── species catalog ────────────────────────────────────── */
  const SPECIES = {
    dog:    { label: 'سگ',      icon: 'dog' },
    cat:    { label: 'گربه',    icon: 'cat' },
    bird:   { label: 'پرنده',   icon: 'bird' },
    rabbit: { label: 'خرگوش',   icon: 'rabbit' },
    rodent: { label: 'جونده',   icon: 'rabbit' },
    other:  { label: 'سایر',    icon: 'paw' },
  };
  const TONES = ['a', 'b', 'c', 'd', 'e', 'f'];
  function petTone(id) { return TONES[Math.abs([...String(id)].reduce((a, c) => a + c.charCodeAt(0), 0)) % TONES.length]; }

  /* ── rooms config ───────────────────────────────────────── */
  let ROOMS = [
    { id: 'A-1', label: 'A-1', type: 'standard', sp: 'cat',    cap: 1 },
    { id: 'A-2', label: 'A-2', type: 'standard', sp: 'dog',    cap: 1 },
    { id: 'A-3', label: 'A-3', type: 'standard', sp: 'dog',    cap: 1 },
    { id: 'B-1', label: 'B-1', type: 'standard', sp: 'cat',    cap: 1 },
    { id: 'B-2', label: 'B-2', type: 'standard', sp: 'bird',   cap: 1 },
    { id: 'VIP', label: 'سوئیت VIP', type: 'vip', sp: 'dog',  cap: 1 },
  ];

  /* ── extra services (boarding-specific) ─────────────────── */
  let EXTRA_SVCS = [
    { id: 'bx-1', title: 'پیاده‌روی و بازی (هر جلسه)', price: 150000 },
    { id: 'bx-2', title: 'حمام و خشک‌کردن',             price: 400000 },
    { id: 'bx-3', title: 'آراستگی و کوتاهی مو',          price: 550000 },
    { id: 'bx-4', title: 'اسکراب و درمان پوستی',         price: 350000 },
    { id: 'bx-5', title: 'خدمات دامپزشک (ویزیت در باکس)', price: 600000 },
  ];

  /* ── mock stays ─────────────────────────────────────────── */
  const T   = C().today();
  const day = (off) => C().J.fromDay(C().J.toDay(T) + off);

  let STAYS = [
    {
      id: 'bd-1', tone: 'b',
      pet: { name: 'لئو', sp: 'dog', breed: 'ژرمن شپرد', owner: 'امیر رستگار', phone: '۰۹۳۵ ۱۱۲ ۲۳۳۴', patientId: 'pt-3' },
      room: 'A-2', in: day(-3), out: day(+1),
      daily: 800000, paymentMode: 'postpaid', status: 'active', transferred: false,
      note: 'پیاده‌روی هر صبح؛ حساسیت غذایی به مرغ — فقط غذای ماهی.',
      items: [
        { kind: 'item', ref: 'it-3', title: 'غذای خشک بالغ سگ (۵ کیلو)', qty: 1, price: 850000, at: day(-3) },
        { kind: 'svc',  ref: 'bx-1', title: 'پیاده‌روی و بازی (هر جلسه)',   qty: 3, price: 150000, at: day(-1) },
      ],
    },
    {
      id: 'bd-2', tone: 'a',
      pet: { name: 'میشا', sp: 'cat', breed: 'پرشین', owner: 'نگار محمدی', phone: '۰۹۱۲ ۳۴۵ ۶۷۸۹', patientId: 'pt-1' },
      room: 'B-1', in: day(-5), out: day(0),
      daily: 550000, paymentMode: 'prepaid', status: 'active', transferred: false,
      note: 'داروی قطره چشم روزی دو بار — توسط پرسنل.',
      items: [
        { kind: 'item', ref: 'it-2', title: 'غذای کنسرو مرغ',    qty: 4, price: 85000,  at: day(-4) },
        { kind: 'item', ref: 'it-8', title: 'خاک مخصوص گربه',    qty: 1, price: 280000, at: day(-5) },
        { kind: 'svc',  ref: 'bx-2', title: 'حمام و خشک‌کردن',    qty: 1, price: 400000, at: day(-1) },
      ],
    },
    {
      id: 'bd-3', tone: 'e',
      pet: { name: 'گلچین', sp: 'bird', breed: 'کاسکو', owner: 'سارا یوسفی', phone: '۰۹۱۹ ۸۷۶ ۵۴۳۲', patientId: '' },
      room: 'B-2', in: day(-10), out: day(+4),
      daily: 320000, paymentMode: 'postpaid', status: 'active', transferred: false,
      note: '',
      items: [
        { kind: 'item', ref: 'it-9', title: 'ویتامین مایع', qty: 1, price: 150000, at: day(-8) },
      ],
    },
    {
      id: 'bd-4', tone: 'f',
      pet: { name: 'کوکو', sp: 'rabbit', breed: 'خرگوش لوپ', owner: 'مریم آذری', phone: '۰۹۱۰ ۵۵۶ ۶۷۷۸', patientId: '' },
      room: 'A-1', in: day(-8), out: day(-2),
      daily: 380000, paymentMode: 'prepaid', status: 'done', transferred: true,
      note: 'تحویل با رضایت کامل.',
      items: [
        { kind: 'item', ref: 'it-5', title: 'مکمل تقویت مفصل', qty: 1, price: 450000, at: day(-7) },
      ],
    },
    {
      id: 'bd-5', tone: 'c',
      pet: { name: 'هاپو', sp: 'dog', breed: 'گلدن رتریور', owner: 'فاطمه رحیمی', phone: '۰۹۳۵ ۱۰۸ ۵۵۶۶', patientId: 'pt-10' },
      room: 'A-3', in: day(-1), out: day(+5),
      daily: 900000, paymentMode: 'postpaid', status: 'active', transferred: false,
      note: 'حساسیت پوستی — از صابون معمولی استفاده نشود.',
      items: [],
    },
    {
      id: 'bd-6', tone: 'd',
      pet: { name: 'سیمبا', sp: 'cat', breed: 'مین‌کون', owner: 'کیانا فتحی', phone: '۰۹۲۱ ۶۶۴ ۲۲۱۰', patientId: 'pt-11' },
      room: 'VIP', in: day(-12), out: day(-5),
      daily: 1200000, paymentMode: 'prepaid', status: 'done', transferred: false,
      note: '',
      items: [
        { kind: 'svc',  ref: 'bx-3', title: 'آراستگی و کوتاهی مو', qty: 1, price: 550000, at: day(-9) },
        { kind: 'svc',  ref: 'bx-2', title: 'حمام و خشک‌کردن',      qty: 2, price: 400000, at: day(-6) },
      ],
    },
  ];

  /* ── domain helpers ─────────────────────────────────────── */
  const J          = () => C().J;
  const nightsOf   = (st) => Math.max(1, J().diff(st.out, st.in));
  const elapsedOf  = (st) => Math.min(Math.max(0, J().diff(T, st.in)), nightsOf(st));
  const remainingOf= (st) => st.status === 'done' ? 0 : Math.max(0, J().diff(st.out, T));
  const stayCost   = (st) => nightsOf(st) * st.daily;
  const itemsCost  = (st) => st.items.reduce((a, it) => a + it.qty * it.price, 0);
  const totalCost  = (st) => stayCost(st) + itemsCost(st);
  const isLeaving  = (st) => st.status === 'active' && remainingOf(st) <= 1;
  const isActive   = (st) => st.status === 'active';
  /* overdue: expected checkout was in past but still active */
  const isOverdue  = (st) => st.status === 'active' && J().toDay(st.out) < J().toDay(T);

  function statusMeta(st) {
    if (st.status === 'done')                    return { label: 'تخلیه‌شده',   tone: 'mute',   icon: 'check' };
    if (isOverdue(st))                           return { label: 'عقب‌افتاده',   tone: 'danger', icon: 'alert' };
    const r = remainingOf(st);
    if (r === 0)                                 return { label: 'تخلیه امروز', tone: 'warn',   icon: 'clock' };
    if (r === 1)                                 return { label: 'فردا تخلیه',  tone: 'info',   icon: 'hourglass' };
    return                                              { label: 'اقامت فعلی',  tone: 'ok',     icon: 'home' };
  }

  function payLabel(mode) {
    return mode === 'prepaid' ? 'پیش‌پرداخت' : 'پس‌پرداخت';
  }
  function payTone(mode) {
    return mode === 'prepaid' ? 'ok' : 'info';
  }

  /* ── page state ─────────────────────────────────────────── */
  const S = { tab: 'all', query: '', species: '', section: 'board' };

  function tabMatches(st) {
    if (S.tab === 'all')      return true;
    if (S.tab === 'current')  return st.status === 'active' && !isLeaving(st) && !isOverdue(st);
    if (S.tab === 'overdue')  return isOverdue(st);
    if (S.tab === 'leaving')  return isLeaving(st) && !isOverdue(st);
    if (S.tab === 'done')     return st.status === 'done';
    return true;
  }
  function filtered() {
    return STAYS.filter((st) => {
      if (!tabMatches(st)) return false;
      if (S.species && st.pet.sp !== S.species) return false;
      if (S.query) {
        const h = (st.pet.name + st.pet.owner + st.pet.phone + st.pet.breed + st.room).toLowerCase();
        if (!h.includes(S.query.toLowerCase())) return false;
      }
      return true;
    }).sort((a, b) => {
      // overdue first, then by checkout date
      if (isOverdue(a) !== isOverdue(b)) return isOverdue(a) ? -1 : 1;
      if ((a.status === 'done') !== (b.status === 'done')) return a.status === 'done' ? 1 : -1;
      return J().toDay(a.out) - J().toDay(b.out);
    });
  }

  /* ═══════════════════════════════════════════════════════════
     Tab counts
     ═══════════════════════════════════════════════════════════ */
  function renderTabCounts() {
    $('#ct-all').textContent     = fa(STAYS.length);
    $('#ct-current').textContent = fa(STAYS.filter((s) => isActive(s) && !isLeaving(s) && !isOverdue(s)).length);
    $('#ct-overdue').textContent = fa(STAYS.filter(isOverdue).length);
    $('#ct-leaving').textContent = fa(STAYS.filter((s) => isLeaving(s) && !isOverdue(s)).length);
    $('#ct-done').textContent    = fa(STAYS.filter((s) => s.status === 'done').length);
  }

  /* ═══════════════════════════════════════════════════════════
     Stay cards grid — full width, click opens modal
     ═══════════════════════════════════════════════════════════ */
  function renderCards() {
    const rows = filtered();
    const host = $('#bd-cards');
    if (!rows.length) {
      host.innerHTML = `<div class="bd-empty-state">
        <div class="ico">${Icons.home}</div>
        <div class="ttl">هیچ اقامتی در این بخش نیست</div>
        <div class="sub">تب دیگری را بررسی کنید یا یک پذیرش جدید ثبت کنید.</div>
      </div>`;
      return;
    }
    host.innerHTML = rows.map((st) => {
      const sp    = SPECIES[st.pet.sp] || SPECIES.other;
      const m     = statusMeta(st);
      const pct   = Math.min(100, Math.round(elapsedOf(st) / nightsOf(st) * 100));
      const rem   = remainingOf(st);
      const od    = isOverdue(st);
      const dayLbl = st.status === 'done'
        ? 'اقامت پایان یافت'
        : od ? `${fa(Math.abs(rem))} روز عقب‌افتاده`
        : rem === 0 ? 'امروز تخلیه می‌شود'
        : rem === 1 ? 'فردا تخلیه می‌شود'
        : `${fa(rem)} روز مانده`;

      return `
      <div class="bd-card ${od ? 'bd-card--overdue' : isLeaving(st) ? 'bd-card--urgent' : ''}" data-stay="${st.id}" role="button" tabindex="0" aria-label="جزئیات اقامت ${esc(st.pet.name)}">
        <div class="bd-card-head">
          <div class="bd-card-avatar ${st.tone}">${Icons[sp.icon] || Icons.paw}</div>
          <div class="bd-card-who">
            <div class="bd-card-name">${esc(st.pet.name)}</div>
            <div class="bd-card-breed">${esc(sp.label)} · ${esc(st.pet.breed)}</div>
          </div>
          <div class="bd-card-badges">
            <span class="pill pill-${m.tone}" style="font-size:10.5px;padding:3px 8px">${m.label}</span>
            <span class="bd-room-chip">اتاق ${fa(st.room)}</span>
          </div>
        </div>

        <div class="bd-card-checkin">
          <span class="bd-card-checkin-label">ورود</span>
          <span class="bd-card-checkin-date">${J().long(st.in)}</span>
          <span class="bd-card-checkin-sep">·</span>
          <span class="bd-card-checkin-nights">${fa(nightsOf(st))} شب</span>
          <span class="bd-card-checkin-sep">·</span>
          <span class="bd-card-checkin-label">خروج</span>
          <span class="bd-card-checkin-date">${J().long(st.out)}</span>
        </div>

        <div class="bd-card-timeline">
          <div class="bd-prog-wrap">
            <div class="bd-prog ${od ? 'bd-prog--danger' : ''}"><i style="width:${Math.min(100,pct)}%"></i></div>
            <div class="bd-prog-info">
              <span>${fa(elapsedOf(st))} از ${fa(nightsOf(st))} شب گذشته</span>
              <span class="${od ? 'bd-danger-txt' : rem === 0 ? 'bd-urgent-txt' : rem <= 1 ? 'bd-warn-txt' : 'bd-muted-txt'}">${dayLbl}</span>
            </div>
          </div>
        </div>

        <div class="bd-card-foot">
          <div class="bd-card-owner">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/></svg>
            ${esc(st.pet.owner)}
          </div>
          <div class="bd-card-cost">
            <span class="bd-cost-total">${money(totalCost(st))}</span>
            <span class="pill pill-${payTone(st.paymentMode)}" style="font-size:10px;padding:2px 7px">${payLabel(st.paymentMode)}</span>
          </div>
        </div>

        <div class="bd-card-cta">
          <span>مشاهده جزئیات</span>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 18l-6-6 6-6"/></svg>
        </div>
      </div>`;
    }).join('');

    $$('#bd-cards .bd-card').forEach((el) => {
      const go = () => {
        const st = STAYS.find((s) => s.id === el.dataset.stay);
        if (st) openStayDetailModal(st);
      };
      el.addEventListener('click', go);
      el.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); } });
    });
  }

  /* ═══════════════════════════════════════════════════════════
     Stay Detail Modal — centered, redesigned
     ═══════════════════════════════════════════════════════════ */
  function openStayDetailModal(st) {
    // re-look so mutations reflect
    const getSt = () => STAYS.find((s) => s.id === st.id) || st;
    _openDetailFor(getSt(), () => { renderCards(); renderTabCounts(); });
  }

  function _openDetailFor(st, onUpdate) {
    const sp  = SPECIES[st.pet.sp] || SPECIES.other;
    const m   = statusMeta(st);
    const pct = Math.min(100, Math.round(elapsedOf(st) / nightsOf(st) * 100));
    const rem = remainingOf(st);
    const od  = isOverdue(st);
    const nights = nightsOf(st);
    const elapsed = elapsedOf(st);

    /* ── day timeline cells ── */
    const cells = [];
    for (let i = 0; i < nights; i++) {
      const dateStr = J().fromDay(J().toDay(st.in) + i);
      const isPast  = i < elapsed;
      const isToday = J().diff(T, dateStr) === 0 && st.status === 'active';
      const dayItems = st.items.filter((it) => it.at === dateStr);
      cells.push(`
        <div class="bd-dm-day ${isPast ? 'past' : ''} ${isToday ? 'today' : ''}" title="${J().long(dateStr)}${dayItems.length ? ' — ' + fa(dayItems.length) + ' مورد مصرفی' : ''}">
          <div class="bd-dm-day-num">${fa(i + 1)}</div>
          <div class="bd-dm-day-date">${J().medium(dateStr)}</div>
          ${dayItems.length ? `<div class="bd-dm-day-dot" title="${esc(dayItems.map(x=>x.title).join('، '))}"></div>` : ''}
        </div>`);
    }

    /* ── consumed items ── */
    const editable = st.status === 'active';
    const svcOpts  = C().SERVICES.map((s) => `<option value="svc:${s.id}">${s.title} · ${money(s.price)}</option>`).join('');
    const itemOpts = C().ITEMS.map((s) => `<option value="item:${s.id}">${s.title} · ${money(s.price)}</option>`).join('');
    const extOpts  = EXTRA_SVCS.map((s) => `<option value="bx:${s.id}">${s.title} · ${money(s.price)}</option>`).join('');

    const itemsHtml = st.items.map((it, i) => `
      <div class="bd-dm-cons-row" data-i="${i}">
        <div class="bd-cons-tag ${it.kind === 'item' ? 'item' : ''}">${it.kind === 'svc' ? 'خدمت' : 'کالا'}</div>
        <div class="bd-cons-ttl">
          ${esc(it.title)}
          <span class="bd-cons-sub">${J().medium(it.at)} · فی ${money(it.price)}</span>
        </div>
        <div class="bd-cons-qty">× ${fa(it.qty)}</div>
        <div class="bd-cons-amt">${money(it.qty * it.price)}</div>
        <button class="bd-cons-rm" data-rm="${i}" title="حذف" ${editable ? '' : 'style="visibility:hidden"'}>${Icons.x}</button>
      </div>`).join('');

    const addRowHtml = editable ? `
      <div class="bd-add-row">
        <select class="selectbox" id="bd-dm-add-ref" style="font-size:12.5px">
          <option value="">افزودن خدمت یا کالا…</option>
          <optgroup label="خدمات پانسیون">${extOpts}</optgroup>
          <optgroup label="خدمات کلینیک">${svcOpts}</optgroup>
          <optgroup label="کالاها">${itemOpts}</optgroup>
        </select>
        <input class="input" type="number" min="1" id="bd-dm-add-qty" value="1" title="تعداد" style="width:72px;font-size:12.5px"/>
        <button class="btn btn-primary btn--sm" id="bd-dm-add-btn">${Icons.plus} افزودن</button>
      </div>` : '';

    const dayLbl = st.status === 'done'
      ? 'اقامت به پایان رسیده'
      : od ? `${fa(Math.abs(rem))} روز عقب‌افتاده`
      : `${fa(elapsed)} روز گذشته · ${rem === 0 ? 'امروز تخلیه' : fa(rem) + ' روز مانده'}`;

    const innerHtml = `
      <!-- Modal header with avatar -->
      <div class="bd-dm-header">
        <div class="bd-dm-av ${st.tone}">${Icons[sp.icon] || Icons.paw}</div>
        <div class="bd-dm-hinfo">
          <div class="bd-dm-name">${esc(st.pet.name)}</div>
          <div class="bd-dm-meta">${esc(sp.label)} · ${esc(st.pet.breed)}</div>
        </div>
        <div class="bd-dm-hbadges">
          <span class="pill pill-${m.tone}">${m.label}</span>
          <span class="pill pill-${payTone(st.paymentMode)}">${payLabel(st.paymentMode)}</span>
          ${st.transferred ? '<span class="pill pill-info">فاکتور منتقل شد</span>' : ''}
        </div>
        <button class="icon-btn close bd-dm-close" data-close title="بستن">${Icons.x}</button>
      </div>

      <!-- Entry info strip -->
      <div class="bd-dm-entry-strip">
        <div class="bd-dm-entry-item">
          <div class="bd-dm-entry-ic bd-dm-entry-ic--in">${Icons.calendar}</div>
          <div>
            <div class="bd-dm-entry-eyebrow">تاریخ ورود</div>
            <div class="bd-dm-entry-val">${J().long(st.in)}</div>
          </div>
        </div>
        <div class="bd-dm-entry-arrow">${Icons.chevronLeft}</div>
        <div class="bd-dm-entry-item">
          <div class="bd-dm-entry-ic">${Icons.calendar}</div>
          <div>
            <div class="bd-dm-entry-eyebrow">تاریخ خروج</div>
            <div class="bd-dm-entry-val">${J().long(st.out)}</div>
          </div>
        </div>
        <div class="bd-dm-entry-arrow">${Icons.chevronLeft}</div>
        <div class="bd-dm-entry-item">
          <div class="bd-dm-entry-ic">${Icons.home}</div>
          <div>
            <div class="bd-dm-entry-eyebrow">اتاق</div>
            <div class="bd-dm-entry-val">${fa(st.room)}</div>
          </div>
        </div>
        <div class="bd-dm-entry-arrow">${Icons.chevronLeft}</div>
        <div class="bd-dm-entry-item">
          <div class="bd-dm-entry-ic">${Icons.coins}</div>
          <div>
            <div class="bd-dm-entry-eyebrow">نرخ شبانه</div>
            <div class="bd-dm-entry-val">${money(st.daily)}</div>
          </div>
        </div>
      </div>

      <!-- Contacts row -->
      <div class="bd-dm-contacts">
        <span class="bd-meta-chip">${Icons.users} <span>${esc(st.pet.owner)}</span></span>
        <span class="bd-meta-chip">${Icons.phone} <bdi>${fa(st.pet.phone)}</bdi></span>
      </div>

      <!-- Scrollable body -->
      <div class="bd-dm-body">

        <!-- Progress + days -->
        <div class="bd-dm-section">
          <div class="bd-dm-section-head">
            <span class="bd-dm-section-title">روزهای اقامت</span>
            <span class="bd-dm-section-meta">${dayLbl}</span>
          </div>
          <div class="bd-dm-prog-wrap">
            <div class="bd-prog ${od ? 'bd-prog--danger' : ''}" style="height:8px"><i style="width:${pct}%"></i></div>
            <div class="bd-prog-info" style="margin-top:5px">
              <span>${fa(elapsed)} از ${fa(nights)} شب گذشته</span>
              <span class="${od ? 'bd-danger-txt' : rem === 0 ? 'bd-urgent-txt' : rem <= 1 ? 'bd-warn-txt' : 'bd-muted-txt'}">${dayLbl}</span>
            </div>
          </div>
          <div class="bd-dm-days-row">${cells.join('')}</div>
        </div>

        ${st.note ? `
        <!-- Note -->
        <div class="bd-dm-section bd-dm-note-section">
          <div class="bd-dm-section-head"><span class="bd-dm-section-title">یادداشت نگهداری</span></div>
          <div class="bd-note-box" style="margin:0"><span class="bd-note-icon">${Icons.clipboard}</span><div>${esc(st.note)}</div></div>
        </div>` : ''}

        <!-- Consumed items -->
        <div class="bd-dm-section">
          <div class="bd-dm-section-head">
            <span class="bd-dm-section-title">اقلام و خدمات مصرفی</span>
            <span class="pill pill-mute">${fa(st.items.length)} مورد</span>
          </div>
          ${itemsHtml || `<div class="bd-cons-empty">${Icons.receipt}<span>هنوز موردی ثبت نشده است</span></div>`}
          ${addRowHtml}
        </div>

        <!-- Totals -->
        <div class="bd-dm-section">
          <div class="bd-dm-section-head"><span class="bd-dm-section-title">صورتحساب</span></div>
          <div class="bd-totals" style="margin:0">
            <div class="bd-total-row"><div class="lbl">اقامت (${fa(nights)} شب × ${money(st.daily)})</div><div class="val">${money(stayCost(st))}</div></div>
            <div class="bd-total-row"><div class="lbl">اقلام و خدمات مصرفی</div><div class="val">${money(itemsCost(st))}</div></div>
            <div class="bd-total-row grand"><div class="lbl">جمع صورتحساب</div><div class="val">${money(totalCost(st))}</div></div>
          </div>
        </div>

      </div>

      <!-- Footer actions -->
      <div class="bd-dm-footer">
        ${_renderDetailActions(st)}
      </div>`;

    const root = document.createElement('div');
    root.className = 'modal-backdrop';
    root.innerHTML = `<div class="modal bd-modal-detail" role="dialog" aria-modal="true">${innerHtml}</div>`;
    document.body.appendChild(root);

    const close = () => {
      root.remove();
      document.removeEventListener('keydown', onKey);
      if (onUpdate) onUpdate();
    };
    const onKey = (e) => { if (e.key === 'Escape') close(); };
    document.addEventListener('keydown', onKey);
    root.addEventListener('click', (e) => { if (e.target === root) close(); });
    root.querySelectorAll('[data-close]').forEach((b) => b.addEventListener('click', close));

    // Remove items
    root.querySelectorAll('[data-rm]').forEach((b) => {
      b.addEventListener('click', () => {
        st.items.splice(+b.dataset.rm, 1);
        toast('مورد حذف شد (نمونه طراحی)', 'trash');
        close();
        openStayDetailModal(st);
      });
    });

    // Add item
    if (editable) {
      const sel = root.querySelector('#bd-dm-add-ref');
      const qty = root.querySelector('#bd-dm-add-qty');
      root.querySelector('#bd-dm-add-btn')?.addEventListener('click', () => {
        const v = sel?.value;
        if (!v) { toast('ابتدا یک مورد انتخاب کنید', 'x'); return; }
        const [kind, ref] = v.split(':');
        let cat;
        if (kind === 'bx') cat = EXTRA_SVCS.find((x) => x.id === ref);
        else cat = (kind === 'svc' ? C().SERVICES : C().ITEMS).find((x) => x.id === ref);
        if (!cat) return;
        const q = Math.max(1, parseInt(qty?.value, 10) || 1);
        st.items.push({ kind: kind === 'bx' ? 'svc' : kind, ref, title: cat.title, qty: q, price: cat.price, at: T });
        toast(`«${cat.title}» به اقلام «${st.pet.name}» اضافه شد (نمونه طراحی)`, 'check');
        close();
        openStayDetailModal(st);
      });
    }

    // Footer actions
    root.querySelectorAll('[data-action]').forEach((b) => {
      b.addEventListener('click', () => {
        const a = b.dataset.action;
        if (a === 'transfer')     { close(); openTransferModal(st); }
        if (a === 'checkout')     { close(); openCheckoutModal(st); }
        if (a === 'print')        toast('چاپ صورتحساب — نسخه بعدی (نمونه طراحی)', 'bulb');
        if (a === 'goto-cashier') navigateTo('cashier.html');
      });
    });
  }

  function _renderDetailActions(st) {
    if (st.status === 'done') {
      return `
        <span class="bd-foot-hint">اقامت پایان‌یافته</span>
        <div class="bd-foot-btns">
          <button class="btn btn-ghost" data-action="print">${Icons.printer} چاپ</button>
          <button class="btn btn-outline" data-action="goto-cashier">${Icons.receipt} صندوق</button>
        </div>`;
    }
    return `
      <div class="bd-foot-btns">
        <button class="btn btn-ghost" data-action="print">${Icons.printer} چاپ</button>
        <button class="btn btn-outline" data-action="checkout">${Icons.home} تخلیه و پایان</button>
      </div>
      <div class="bd-foot-btns">
        ${st.transferred
          ? `<button class="btn btn-outline" data-action="goto-cashier">${Icons.receipt} مشاهده در صندوق</button>`
          : `<button class="btn btn-primary" data-action="transfer">${Icons.wallet} انتقال به صندوق</button>`}
      </div>`;
  }

  /* ── navigateTo helper (may not exist globally) ─────────── */
  function navigateTo(page) {
    if (typeof window.navigateTo === 'function') window.navigateTo(page);
    else window.location.href = page;
  }

  /* ═══════════════════════════════════════════════════════════
     History section — card grid with detail modal
     ═══════════════════════════════════════════════════════════ */
  let histQuery = '', histSpecies = '';

  function renderHistory() {
    const rows = STAYS.filter((st) => {
      if (histSpecies && st.pet.sp !== histSpecies) return false;
      if (histQuery) {
        const h = (st.pet.name + st.pet.owner + st.room + st.pet.breed).toLowerCase();
        if (!h.includes(histQuery.toLowerCase())) return false;
      }
      return true;
    }).sort((a, b) => J().toDay(b.in) - J().toDay(a.in));

    $('#bd-hist-count').textContent = fa(rows.length) + ' اقامت';

    const host = $('#bd-hist-grid');
    if (!rows.length) {
      host.innerHTML = `<div class="bd-empty-state"><div class="ico">${Icons.timeline}</div><div class="ttl">هیچ اقامتی یافت نشد</div></div>`;
      return;
    }

    host.innerHTML = rows.map((st) => {
      const sp = SPECIES[st.pet.sp] || SPECIES.other;
      const m  = statusMeta(st);
      const pct = Math.min(100, Math.round(elapsedOf(st) / nightsOf(st) * 100));
      const nights = nightsOf(st);

      return `
      <div class="bd-hcard" data-hist="${st.id}" role="button" tabindex="0" aria-label="جزئیات اقامت ${esc(st.pet.name)}">
        <div class="bd-hcard-top">
          <div class="bd-card-avatar ${st.tone}" style="width:40px;height:40px;border-radius:12px;flex:0 0 auto">${Icons[sp.icon] || Icons.paw}</div>
          <div style="flex:1;min-width:0">
            <div class="bd-card-name">${esc(st.pet.name)}</div>
            <div class="bd-card-breed">${esc(sp.label)} · ${esc(st.pet.breed)}</div>
          </div>
          <span class="pill pill-${m.tone}" style="font-size:10.5px;padding:3px 8px;flex:0 0 auto">${m.label}</span>
        </div>

        <div class="bd-hcard-dates">
          <div class="bd-hcard-date-item">
            <span class="bd-hcard-date-eyebrow">ورود</span>
            <span class="bd-hcard-date-val">${J().long(st.in)}</span>
          </div>
          <div class="bd-hcard-date-sep">${Icons.chevronLeft}</div>
          <div class="bd-hcard-date-item">
            <span class="bd-hcard-date-eyebrow">خروج</span>
            <span class="bd-hcard-date-val">${J().long(st.out)}</span>
          </div>
          <div class="bd-hcard-date-sep">${Icons.chevronLeft}</div>
          <div class="bd-hcard-date-item">
            <span class="bd-hcard-date-eyebrow">مدت</span>
            <span class="bd-hcard-date-val bd-hcard-nights">${fa(nights)} شب</span>
          </div>
        </div>

        <div class="bd-prog" style="height:4px;margin:4px 0 8px"><i style="width:${pct}%"></i></div>

        <div class="bd-hcard-foot">
          <div class="bd-card-owner">
            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/></svg>
            ${esc(st.pet.owner)}
          </div>
          <div style="display:flex;align-items:center;gap:8px">
            <span class="bd-room-chip">اتاق ${fa(st.room)}</span>
            <span class="bd-cost-total" style="font-size:11.5px">${money(totalCost(st))}</span>
          </div>
        </div>
      </div>`;
    }).join('');

    $$('#bd-hist-grid .bd-hcard').forEach((el) => {
      const go = () => {
        const st = STAYS.find((s) => s.id === el.dataset.hist);
        if (st) _openDetailFor(st, () => renderHistory());
      };
      el.addEventListener('click', go);
      el.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); } });
    });
  }

  window.bdOpenFromHistory = function (id) {
    const st = STAYS.find((s) => s.id === id);
    if (!st) return;
    _openDetailFor(st, () => { renderAll(); });
  };

  /* ═══════════════════════════════════════════════════════════
     Settings section
     ═══════════════════════════════════════════════════════════ */
  function renderSettingsRooms() {
    const occupied = STAYS.filter(isActive).map((s) => s.room);
    $('#bd-rooms-grid').innerHTML = ROOMS.map((r) => {
      const occ = occupied.includes(r.id);
      const sp = SPECIES[r.sp] || SPECIES.other;
      return `
        <div class="bd-room-tile ${occ ? 'occupied' : 'free'}">
          <div class="bd-room-tile-head">
            <span class="bd-rt-ic">${Icons[sp.icon] || Icons.paw}</span>
            <b>${fa(r.label)}</b>
            <span class="bd-rt-type">${r.type === 'vip' ? 'VIP' : 'معمولی'}</span>
          </div>
          <div class="bd-room-status ${occ ? 'occ' : 'free'}">
            ${occ ? Icons.home : Icons.check}
            <span>${occ ? 'اشغال' : 'آزاد'}</span>
          </div>
        </div>`;
    }).join('');
  }

  function renderSettingsSvcs() {
    $('#bd-svcs-list').innerHTML = EXTRA_SVCS.map((s) => `
      <div class="bd-svc-row">
        <div class="bd-svc-name">${esc(s.title)}</div>
        <div class="bd-svc-price">${money(s.price)}</div>
        <button class="vx-mini" onclick="bdEditSvc('${s.id}')" title="ویرایش">${Icons.edit}</button>
      </div>`).join('');
  }

  window.bdSettingsSave   = (label) => toast(`${label} ذخیره شد (نمونه طراحی)`, 'check');
  window.bdSettingsToggle = (inp, name) => toast(`${name} ${inp.checked ? 'فعال' : 'غیرفعال'} شد (نمونه طراحی)`, 'bell');
  window.bdAddRoom        = () => toast('افزودن اتاق (نمونه طراحی)', 'plus');
  window.bdAddService     = () => toast('افزودن خدمت (نمونه طراحی)', 'plus');
  window.bdEditSvc        = (id) => { const s = EXTRA_SVCS.find((x) => x.id === id); if (s) toast(`ویرایش «${s.title}» (نمونه طراحی)`, 'edit'); };

  /* ═══════════════════════════════════════════════════════════
     Generic modal (for new-stay, transfer, checkout)
     ═══════════════════════════════════════════════════════════ */
  function openModal(innerHtml, opts = {}) {
    const root = document.createElement('div');
    root.className = 'modal-backdrop';
    root.innerHTML = `<div class="modal ${opts.wide ? 'bd-modal-wide' : ''}" role="dialog" aria-modal="true">${innerHtml}</div>`;
    document.body.appendChild(root);
    const close = () => { root.remove(); document.removeEventListener('keydown', onKey); };
    const onKey = (e) => { if (e.key === 'Escape') close(); };
    document.addEventListener('keydown', onKey);
    root.addEventListener('click', (e) => { if (e.target === root) close(); });
    root.querySelectorAll('[data-close]').forEach((b) => b.addEventListener('click', close));
    return { root, close };
  }

  function mhead(icon, eyebrow, title) {
    return `
      <div class="modal-header">
        <div class="icon-box">${Icons[icon] || Icons.paw}</div>
        <div class="titles"><div class="eyebrow">${eyebrow}</div><h3>${title}</h3></div>
        <button class="icon-btn close" data-close title="بستن">${Icons.x}</button>
      </div>`;
  }

  /* ── transfer modal ─────────────────────────────────────── */
  function openTransferModal(st) {
    const lines = [
      { title: `اقامت پانسیون — اتاق ${fa(st.room)}`, qty: nightsOf(st), price: st.daily },
      ...st.items.map((it) => ({ title: it.title, qty: it.qty, price: it.price })),
    ];
    const { root, close } = openModal(`
      ${mhead('wallet', 'انتقال به صندوق', 'پیش‌نمایش فاکتور اقامت')}
      <div class="modal-body">
        <p class="bd-modal-hint">فاکتور «<b>${esc(st.pet.name)}</b> — ${esc(st.pet.owner)}» به صورت پیش‌نویس در <b>صندوق</b> ایجاد می‌شود:</p>
        <div class="bd-tlines">
          ${lines.map((l) => `
            <div class="bd-tline">
              <span>${esc(l.title)} <span class="bd-tline-qty">× ${fa(l.qty)}</span></span>
              <span class="bd-tline-amt">${money(l.qty * l.price)}</span>
            </div>`).join('')}
        </div>
        <div class="bd-totals" style="margin:0">
          <div class="bd-total-row grand" style="border:0;padding-top:0;margin-top:0">
            <div class="lbl">مبلغ قابل انتقال</div>
            <div class="val">${money(totalCost(st))}</div>
          </div>
        </div>
      </div>
      <div class="modal-footer">
        <button class="btn btn-primary" id="bd-do-transfer">${Icons.check} تأیید و انتقال</button>
        <button class="btn btn-ghost" data-close>انصراف</button>
      </div>`, { wide: true });

    root.querySelector('#bd-do-transfer').addEventListener('click', () => {
      st.transferred = true;
      close();
      toast(`فاکتور «${st.pet.name}» به مبلغ ${money(totalCost(st))} به صندوق منتقل شد (نمونه طراحی)`, 'check');
      renderAll();
    });
  }

  /* ── checkout modal ─────────────────────────────────────── */
  function openCheckoutModal(st) {
    const { root, close } = openModal(`
      ${mhead('home', 'تخلیه', `پایان اقامت «${esc(st.pet.name)}»`)}
      <div class="modal-body">
        <p class="bd-modal-hint">اتاق <b>${fa(st.room)}</b> آزاد می‌شود و اقامت به بخش «تخلیه‌شده» منتقل می‌گردد.</p>
        ${!st.transferred ? `<div class="bd-warn-banner">${Icons.alert} <span>فاکتور هنوز به صندوق منتقل نشده است — پس از تخلیه نیز می‌توان انتقال داد.</span></div>` : ''}
      </div>
      <div class="modal-footer">
        <button class="btn btn-primary" id="bd-do-checkout">${Icons.check} تأیید تخلیه</button>
        <button class="btn btn-ghost" data-close>انصراف</button>
      </div>`);

    root.querySelector('#bd-do-checkout').addEventListener('click', () => {
      st.status = 'done';
      close();
      toast(`«${st.pet.name}» تخلیه شد — اتاق ${fa(st.room)} آزاد است (نمونه طراحی)`, 'home');
      renderAll();
    });
  }

  /* ── new stay modal ─────────────────────────────────────── */
  function openNewStayModal() {
    const spOpts = Object.entries(SPECIES).filter(([k]) => k !== 'other').map(([k, v]) =>
      `<option value="${k}">${v.label}</option>`).join('');
    const rmOpts = ROOMS.map((r) => `<option value="${r.id}">${fa(r.label)}</option>`).join('');

    const knownPets = [
      { pet: 'میشا', sp: 'cat', breed: 'پرشین', owner: 'نگار محمدی', phone: '۰۹۱۲ ۳۴۵ ۶۷۸۹' },
      { pet: 'لئو',  sp: 'dog', breed: 'ژرمن شپرد', owner: 'امیر رستگار', phone: '۰۹۳۵ ۱۱۲ ۲۳۳۴' },
      { pet: 'هاپو', sp: 'dog', breed: 'گلدن رتریور', owner: 'فاطمه رحیمی', phone: '۰۹۳۵ ۱۰۸ ۵۵۶۶' },
      { pet: 'سیمبا', sp: 'cat', breed: 'مین‌کون', owner: 'کیانا فتحی', phone: '۰۹۲۱ ۶۶۴ ۲۲۱۰' },
    ];
    const acSuggs = knownPets.map((p) => `
      <button type="button" class="bd-ac-item" data-pet='${JSON.stringify(p)}'>
        ${Icons[SPECIES[p.sp]?.icon] || Icons.paw}
        <div><b>${p.pet}</b> <span style="color:var(--muted)">${p.breed}</span></div>
        <div class="bd-ac-owner">${p.owner}</div>
      </button>`).join('');

    const { root, close } = openModal(`
      ${mhead('home', 'پذیرش پانسیون', 'ثبت اقامت جدید')}
      <div class="modal-body bd-new-modal-body">

        <div class="bd-new-mode" id="bd-new-mode">
          <button class="active" data-mode="existing">از پرونده‌های موجود</button>
          <button data-mode="manual">وارد کردن دستی</button>
        </div>

        <div id="bd-mode-existing">
          <div class="field">
            <label>جستجوی بیمار</label>
            <div class="pb-search" style="flex:none;width:100%">
              <span class="ic">${Icons.search}</span>
              <input id="bd-ac-input" type="text" placeholder="نام حیوان یا مالک را تایپ کنید…" autocomplete="off"/>
            </div>
            <div class="bd-ac-list" id="bd-ac-list">${acSuggs}</div>
          </div>
          <div class="bd-selected-pet" id="bd-selected-pet" hidden></div>
        </div>

        <div id="bd-mode-manual" hidden>
          <div class="grid-2">
            <div class="field"><label>نام حیوان *</label><input class="input" id="bd-f-name" placeholder="مثلاً: لئو"/></div>
            <div class="field"><label>گونه *</label><select class="selectbox" id="bd-f-sp">${spOpts}</select></div>
            <div class="field"><label>نژاد</label><input class="input" id="bd-f-breed" placeholder="مثلاً: ژرمن شپرد"/></div>
            <div class="field"><label>نام مالک *</label><input class="input" id="bd-f-owner" placeholder="مثلاً: امیر رستگار"/></div>
            <div class="field"><label>موبایل مالک</label><input class="input" id="bd-f-phone" dir="ltr" placeholder="09xx-xxx-xxxx"/></div>
          </div>
        </div>

        <div class="bd-form-divider">اطلاعات اقامت</div>
        <div class="grid-2">
          <div class="field"><label>اتاق / سوئیت *</label><select class="selectbox" id="bd-f-room">${rmOpts}</select></div>
          <div class="field"><label>نرخ شبانه (ریال)</label><input class="input" type="number" min="0" step="50000" id="bd-f-daily" value="800000"/></div>
          <div class="field"><label>تاریخ ورود *</label><input class="input" id="bd-f-in" dir="ltr" value="${T}" placeholder="۱۴۰۴/۰۶/۰۱"/></div>
          <div class="field"><label>تاریخ خروج *</label><input class="input" id="bd-f-out" dir="ltr" placeholder="۱۴۰۴/۰۶/۱۰"/></div>
        </div>
        <div class="field">
          <label>نوع پرداخت</label>
          <div class="bd-pay-mode" id="bd-pay-mode">
            <label class="bd-pay-opt">
              <input type="radio" name="bd-pay" value="postpaid" checked/>
              <span>پس‌پرداخت <small>تسویه موقع تحویل</small></span>
            </label>
            <label class="bd-pay-opt">
              <input type="radio" name="bd-pay" value="prepaid"/>
              <span>پیش‌پرداخت <small>قبل از اقامت</small></span>
            </label>
          </div>
        </div>
        <div class="field">
          <label>یادداشت نگهداری</label>
          <textarea class="textarea" id="bd-f-note" placeholder="رژیم غذایی، دارو، رفتار خاص…" rows="2"></textarea>
        </div>
      </div>
      <div class="modal-footer">
        <button class="btn btn-primary" id="bd-stay-ok">${Icons.check} ثبت پذیرش</button>
        <button class="btn btn-ghost" data-close>انصراف</button>
      </div>`, { wide: true });

    let currentMode = 'existing', selectedPet = null;
    root.querySelectorAll('#bd-new-mode button').forEach((b) => {
      b.addEventListener('click', () => {
        currentMode = b.dataset.mode;
        root.querySelectorAll('#bd-new-mode button').forEach((x) => x.classList.remove('active'));
        b.classList.add('active');
        root.querySelector('#bd-mode-existing').hidden = (currentMode !== 'existing');
        root.querySelector('#bd-mode-manual').hidden = (currentMode !== 'manual');
      });
    });

    const acInput = root.querySelector('#bd-ac-input');
    const acList  = root.querySelector('#bd-ac-list');
    const selPet  = root.querySelector('#bd-selected-pet');
    acInput?.addEventListener('input', () => {
      const q = acInput.value.toLowerCase();
      root.querySelectorAll('.bd-ac-item').forEach((el) => {
        const txt = el.textContent.toLowerCase();
        el.style.display = !q || txt.includes(q) ? '' : 'none';
      });
    });
    root.querySelectorAll('.bd-ac-item').forEach((b) => {
      b.addEventListener('click', () => {
        try { selectedPet = JSON.parse(b.dataset.pet); } catch (_) { return; }
        acList.hidden = true;
        selPet.hidden = false;
        selPet.innerHTML = `
          <div class="bd-sel-pet-card">
            <div class="bd-sel-icon">${Icons[SPECIES[selectedPet.sp]?.icon] || Icons.paw}</div>
            <div>
              <div class="bd-sel-name">${esc(selectedPet.pet)} <span>${SPECIES[selectedPet.sp]?.label || ''}</span></div>
              <div class="bd-sel-sub">${esc(selectedPet.owner)} · ${fa(selectedPet.phone)}</div>
            </div>
            <button type="button" class="icon-btn" id="bd-sel-clear" title="تغییر">${Icons.x}</button>
          </div>`;
        root.querySelector('#bd-sel-clear')?.addEventListener('click', () => {
          selectedPet = null; acList.hidden = false; selPet.hidden = true; acInput.value = ''; acInput.focus();
        });
      });
    });

    root.querySelector('#bd-stay-ok').addEventListener('click', () => {
      const v   = (id) => (root.querySelector(id)?.value || '').trim();
      const inD = v('#bd-f-in'), outD = v('#bd-f-out');
      let petData;

      if (currentMode === 'existing') {
        if (!selectedPet) { toast('یک بیمار از فهرست انتخاب کنید', 'x'); return; }
        petData = { name: selectedPet.pet, sp: selectedPet.sp, breed: selectedPet.breed, owner: selectedPet.owner, phone: selectedPet.phone, patientId: '' };
      } else {
        const name = v('#bd-f-name'), owner = v('#bd-f-owner');
        if (!name || !owner) { toast('نام حیوان و مالک الزامی است', 'x'); return; }
        petData = { name, sp: v('#bd-f-sp') || 'dog', breed: v('#bd-f-breed') || 'نامشخص', owner, phone: v('#bd-f-phone') || '—', patientId: '' };
      }

      if (!/^\d{4}\/\d{2}\/\d{2}$/.test(inD) || !/^\d{4}\/\d{2}\/\d{2}$/.test(outD)) {
        toast('تاریخ ورود و خروج را با قالب ۱۴۰۴/۰۶/۰۱ وارد کنید', 'x'); return;
      }
      if (J().toDay(outD) <= J().toDay(inD)) { toast('تاریخ خروج باید پس از ورود باشد', 'x'); return; }

      const payMode = root.querySelector('input[name="bd-pay"]:checked')?.value || 'postpaid';
      const uid = STAYS.length + 1;
      const st = {
        id: `bd-${uid}`, tone: TONES[(uid - 1) % TONES.length],
        pet: petData,
        room: v('#bd-f-room') || ROOMS[0].id,
        in: inD, out: outD,
        daily: Math.max(0, Number(v('#bd-f-daily')) || 0),
        paymentMode: payMode, status: 'active', transferred: false,
        note: v('#bd-f-note'), items: [],
      };
      STAYS.unshift(st);
      close();
      toast(`پذیرش «${petData.name}» برای ${fa(nightsOf(st))} شب ثبت شد (نمونه طراحی)`, 'check');
      renderAll();
    });
  }

  /* ═══════════════════════════════════════════════════════════
     Section switching
     ═══════════════════════════════════════════════════════════ */
  function switchSection(key) {
    S.section = key;
    $$('#bd-main-seg button').forEach((b) => {
      const on = b.dataset.section === key;
      b.classList.toggle('active', on);
      b.setAttribute('aria-selected', String(on));
    });
    $$('.bd-section').forEach((el) => { el.hidden = el.id !== `bd-section-${key}`; });
    // tier-2 board filters (status tabs + search + actions) belong to مدیریت اقامت only
    const boardTools = $('#bd-board-tools');
    if (boardTools) boardTools.hidden = key !== 'board';
    if (key === 'settings') { renderSettingsRooms(); renderSettingsSvcs(); }
    if (key === 'history')  { renderHistory(); }
  }

  /* ── species dropdown ───────────────────────────────────── */
  function initSpeciesDropdown() {
    const btn = $('#bd-filter-species');
    const dd  = $('#bd-species-dropdown');
    if (!btn || !dd) return;

    dd.querySelectorAll('.bd-dd-ic[data-ic]').forEach((el) => {
      el.innerHTML = Icons[el.dataset.ic] || '';
    });

    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const rect = btn.getBoundingClientRect();
      dd.style.top  = (rect.bottom + 6) + 'px';
      dd.style.right = (document.body.clientWidth - rect.right) + 'px';
      dd.hidden = !dd.hidden;
    });
    document.addEventListener('click', () => { dd.hidden = true; });
    dd.querySelectorAll('.bd-dd-item').forEach((b) => {
      b.addEventListener('click', (e) => {
        e.stopPropagation();
        S.species = b.dataset.sp;
        const lbl = b.textContent.trim();
        $('#bd-species-label').textContent = S.species ? lbl : 'همه گونه‌ها';
        dd.querySelectorAll('.bd-dd-item').forEach((x) => x.classList.toggle('active', x === b));
        dd.hidden = true;
        renderCards();
      });
    });
  }

  /* ── reveal animation ───────────────────────────────────── */
  function mountReveal() {
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce) { $$('.reveal').forEach((n) => n.classList.add('in')); return; }
    const io = new IntersectionObserver((ents) => {
      ents.forEach((e, i) => {
        if (e.isIntersecting) { setTimeout(() => e.target.classList.add('in'), i * 50); io.unobserve(e.target); }
      });
    }, { threshold: 0.1 });
    $$('.reveal').forEach((n) => io.observe(n));
  }

  /* ═══════════════════════════════════════════════════════════
     render all
     ═══════════════════════════════════════════════════════════ */
  function renderAll() {
    renderTabCounts();
    renderCards();
  }

  /* ═══════════════════════════════════════════════════════════
     boot
     ═══════════════════════════════════════════════════════════ */
  window.initBoardingPage = function () {
    if (!window.CLINIC) { console.error('[boarding] CLINIC not loaded'); return; }

    $$('#bd-main-seg .bd-seg-ic[data-ic]').forEach((el) => { el.innerHTML = Icons[el.dataset.ic] || ''; });

    $$('#bd-main-seg button').forEach((b) => {
      b.addEventListener('click', () => switchSection(b.dataset.section));
    });

    $$('#bd-status-tabs button').forEach((b) => {
      b.addEventListener('click', () => {
        $$('#bd-status-tabs button').forEach((x) => { x.classList.remove('active'); x.setAttribute('aria-selected', 'false'); });
        b.classList.add('active'); b.setAttribute('aria-selected', 'true');
        S.tab = b.dataset.tab; renderCards();
      });
    });

    const searchWrap = $('#bd-search-wrap');
    const searchInp  = $('#bd-search');
    let sh;
    searchInp?.addEventListener('input', (e) => {
      searchWrap?.classList.toggle('has-value', !!e.target.value.trim());
      clearTimeout(sh);
      sh = setTimeout(() => { S.query = e.target.value.trim(); renderCards(); }, 160);
    });
    $('#bd-search-clear')?.addEventListener('click', () => {
      searchInp.value = ''; S.query = '';
      searchWrap?.classList.remove('has-value');
      renderCards(); searchInp.focus();
    });

    $('#bd-new')?.addEventListener('click', openNewStayModal);

    const hSearch = $('#bd-hist-search'), hWrap = hSearch?.closest('.pb-search');
    let hh;
    hSearch?.addEventListener('input', (e) => {
      hWrap?.classList.toggle('has-value', !!e.target.value.trim());
      clearTimeout(hh);
      hh = setTimeout(() => { histQuery = e.target.value.trim(); renderHistory(); }, 160);
    });
    $('#bd-hist-clear')?.addEventListener('click', () => {
      hSearch.value = ''; histQuery = '';
      hWrap?.classList.remove('has-value');
      renderHistory(); hSearch.focus();
    });
    $('#bd-hist-species')?.addEventListener('change', (e) => { histSpecies = e.target.value; renderHistory(); });

    initSpeciesDropdown();
    mountReveal();
    renderAll();

    document.addEventListener('keydown', (e) => {
      if (e.target.matches('input,textarea,select')) return;
      if (e.key === 'n' || e.key === 'N') { e.preventDefault(); openNewStayModal(); }
      if (e.key === '/') { e.preventDefault(); searchInp?.focus(); }
    });
  };

})();
