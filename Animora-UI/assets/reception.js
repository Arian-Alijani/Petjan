/* =========================================================
   وت‌پلاس · پذیرش و صف (فاز ۵)
   assets/reception.js — کنترل صف زنده + deep-link + اکشن‌ها
   Static UI prototype: only FLOW/CLINIC globals required (L1–L3).
   ========================================================= */
(function () {
  'use strict';

  const $ = (s, r=document) => r.querySelector(s);
  const $$ = (s, r=document) => Array.from(r.querySelectorAll(s));
  const C = () => window.CLINIC;
  const toFa = (v) => (window.toFa ? window.toFa(v) : String(v));

  /* ---------------- toast (local fallback if not defined) ---------------- */
  if (typeof window.toast !== 'function') {
    window.toast = function (msg, icon = 'check') {
      let host = document.querySelector('.toast-host');
      if (!host) { host = document.createElement('div'); host.className = 'toast-host'; document.body.appendChild(host); }
      const el = document.createElement('div');
      el.className = 'toast' + (icon === 'x' ? ' error' : icon === 'bulb' ? ' warn' : '');
      el.innerHTML = (window.Icons && window.Icons[icon] ? window.Icons[icon] : '') + '<span></span>';
      el.lastChild.textContent = msg;
      host.appendChild(el);
      setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 320); }, 2600);
    };
  }

  /* ---------------- helpers ---------------- */
  const fmtTime = (mins) => {
    const h = Math.floor(mins / 60), m = mins % 60;
    return (window.toFa ? window.toFa(String(h).padStart(2, '0')) : String(h)) + ':' + (window.toFa ? window.toFa(String(m).padStart(2, '0')) : String(m).padStart(2, '0'));
  };
  const nowHM = () => {
    const d = new Date();
    return fmtTime(d.getHours() * 60 + d.getMinutes());
  };
  const PRIO_META = {
    normal: { label: 'عادی', tone: 'ok' },
    urgent: { label: 'اورژانس', tone: 'danger' },
  };
  const SRC_META = {
    appointment: { label: 'نوبت از پیش', color: 'var(--brand-600)' },
    walkin:      { label: 'حضوری',      color: 'var(--info)' },
    urgent:      { label: 'اورژانس',    color: 'var(--danger)' },
  };

  // Order: urgent first, then earliest `at` (§۴.۲)
  const compareTickets = (a, b) => {
    const ua = a.prio === 'urgent' ? 0 : 1;
    const ub = b.prio === 'urgent' ? 0 : 1;
    if (ua !== ub) return ua - ub;
    return (a.at || 0) - (b.at || 0);
  };
  const isActive = (q) => !['done', 'left', 'noshow', 'cancel'].includes(q.state);

  const petAvatar = (q) => {
    const av = q.pet.av || 'a';
    const ic = q.pet.ic || 'paw';
    return `<div class="avatar-pet ${av}" aria-hidden="true">${(window.Icons && Icons[ic]) || Icons.paw}</div>`;
  };

  /* ---------------- state ---------------- */
  const state = {
    tab: 'all',
    query: '',
    page: 1,
    pageSize: 8,
    modal: null,         // { kind: 'walkin' | 'admit' | 'urgent' }
    modalTarget: null,
  };

  /* ---------------- actions (all flow through FLOW.transition) ---------------- */
  function admitTicket(qt) {
    if (qt.state !== 'waiting') { toast('این نوبت قبلاً پذیرفته شده است.', 'x'); return; }
    const r = window.FLOW.transition(qt, 'called');
    if (!r.ok) toast(r.error, 'x');
    else      toast(`«${qt.pet.name}» فراخوانده شد (نمونه طراحی)`, 'megaphone');
    render();
  }

  function enterVisit(qt) {
    if (!['called', 'waiting'].includes(qt.state)) { toast('نوبت آماده ورود به ویزیت نیست.', 'x'); return; }
    if (qt.state === 'waiting') {
      const w = window.FLOW.transition(qt, 'called');
      if (!w.ok) { toast(w.error, 'x'); return; }
    }
    const r = window.FLOW.transition(qt, 'inVisit');
    if (!r.ok) { toast(r.error, 'x'); }
    else { toast(`«${qt.pet.name}» وارد اتاق ویزیت شد (نمونه طراحی)`, 'check'); render(); }
  }

  function leaveQueue(qt) {
    const r = window.FLOW.transition(qt, 'left');
    if (!r.ok) toast(r.error, 'x');
    else      toast(`«${qt.pet.name}» از صف خارج شد (نمونه طراحی)`, 'x');
    render();
  }

  function noShow(qt) {
    const r = window.FLOW.transition(qt, 'noshow');
    if (!r.ok) toast(r.error, 'x');
    else      toast(`«${qt.pet.name}» عدم‌مراجعه ثبت شد (نمونه طراحی)`, 'alert');
    render();
  }

  function toggleUrgent(qt) {
    qt.prio = (qt.prio === 'urgent') ? 'normal' : 'urgent';
    const label = qt.prio === 'urgent' ? 'اورژانس فعال شد' : 'اولویت عادی شد';
    toast(`${label} برای «${qt.pet.name}» (نمونه طراحی)`, qt.prio === 'urgent' ? 'alert' : 'check');
    render();
  }

  function callDoctor(qt, docId) {
    if (!qt.doc) { toast('پزشک معالج انتخاب نشده است.', 'x'); return; }
    const from = qt.state;
    if (from === 'waiting') {
      const w = window.FLOW.transition(qt, 'called');
      if (!w.ok) { toast(w.error, 'x'); return; }
    }
    const doc = C().DOCTORS.find(d => d.id === (docId || qt.doc));
    toast(`پزشک ${doc ? doc.short : ''} فراخوانده شد (نمونه طراحی)`, 'megaphone');
    render();
  }

  function startVisit(qt) {
    enterVisit(qt);
    // پس از ورود به ویزیت، به صفحه ویزیت هدایت می‌شود (نمونه طراحی)
    setTimeout(() => navigateTo('visits.html#enc-new?from=' + qt.id), 700);
  }

  function navigateTo(href) {
    if (typeof window.navigateTo === 'function') window.navigateTo(href);
    else location.href = href;
  }

  /* ---------------- create walk-in ticket ---------------- */
  function createWalkin({ name, owner, phone, docId, urgent }) {
    const id = C().nextId('qt');
    const idEnc = null; // encounter on encounter page
    const list = (urgent === 'soft' ? C().QUEUE : C().QUEUE); // both go into QUEUE
    const now = (function () {
      const d = new Date(); return d.getHours() * 60 + d.getMinutes();
    })();
    const qt = {
      id: id, apptId: null, src: urgent ? 'urgent' : 'walkin', date: C().today(), at: now, du: 30,
      pet: { name, kind: '—', owner, phone, av: 'b', ic: 'paw' },
      doc: docId, type: 'visit', prio: urgent ? 'urgent' : 'normal', state: 'waiting',
      admittedBy: 'پذیرش',
      times: { admitted: C().today() + ' ' + fmtTime(now), called: null, entered: null, finished: null },
    };
    C().QUEUE.push(qt);
    toast(`مراجعهٔ «${name}» ثبت شد (نمونه طراحی)`, urgent ? 'alert' : 'check');
    state.modal = null;
    render();
  }

  /* ---------------- render ---------------- */
  function render() {
    renderHero();
    renderStats();
    renderList();
  }

  function renderHero() {
    const q = C().QUEUE;
    const today = C().today();
    $('#rc-today-long').textContent = ' ' + C().J.long(today);
    $('#rc-now-hm').textContent = nowHM();

    const activeUrgent = q.filter(x => x.prio === 'urgent' && isActive(x)).length;
    $('#rc-urgent-cnt').textContent = activeUrgent;
    const doneToday = q.filter(x => x.state === 'done').length;
    $('#rc-done-cnt').textContent = doneToday;
    const waits = q
      .filter(x => isActive(x) && x.times && x.times.admitted)
      .map(x => Math.max(0, Math.floor((new Date().getTime()) / 60000) - parseHM(x.times.admitted)))
      .filter(n => Number.isFinite(n) && n < 480);
    const avg = waits.length ? Math.round(waits.reduce((a, b) => a + b, 0) / waits.length) : 0;
    $('#rc-avg-wait').textContent = avg;
    const next = q.filter(isActive).sort(compareTickets)[0];
    $('#rc-next-pet').textContent = next ? (`${next.pet.name} · ${C().DOCTORS.find(d => d.id === next.doc).short}`) : '—';
  }
  // parse '1405/06/10 09:32' → minutes since now (approx; uses today date)
  function parseHM(s) {
    if (!s) return NaN;
    const parts = s.split(' ');
    if (parts.length < 2) return NaN;
    const [h, m] = parts[1].split(':').map(Number);
    if (!Number.isFinite(h) || !Number.isFinite(m)) return NaN;
    return h * 60 + m;
  }

  function renderStats() {
    const q = C().QUEUE;
    const stats = [
      { tone:'info',    icon:'clock',     label:'در انتظار',     val: q.filter(x => x.state === 'waiting').length, foot:'اولویت‌بندی بر اساس زمان و اورژانس' },
      { tone:'lilac',   icon:'megaphone', label:'فراخوانده‌شده', val: q.filter(x => x.state === 'called').length,   foot:'پزشک معالج منتظر ورود بیمار است' },
      { tone:'ok',      icon:'briefcase', label:'در حال ویزیت', val: q.filter(x => x.state === 'inVisit').length,  foot:'پرونده فعال روی میز پزشک' },
      { tone:'warn',    icon:'check',     label:'پایان‌یافته امروز', val: q.filter(x => x.state === 'done').length, foot:'صورتحساب به صندوق' },
    ];
    const html = stats.map(s => `
      <div class="stat rc-stat">
        <div class="stat-head">
          <div class="stat-icon ${s.tone}">${window.Icons[s.icon] || window.Icons.paw}</div>
          <div class="stat-delta">${window.toFa ? window.toFa(s.val) : s.val}</div>
        </div>
        <div class="stat-value">${window.toFa ? window.toFa(s.val) : s.val}</div>
        <div class="stat-label">${s.label}</div>
        <div class="stat-foot">${s.foot}</div>
      </div>
    `).join('');
    $('#rc-stats').innerHTML = html;
  }

  function renderList() {
    const all = C().QUEUE.slice().sort(compareTickets);
    // tab filter
    let rows = all;
    if (state.tab === 'active')  rows = all.filter(isActive);
    if (state.tab === 'urgent')  rows = all.filter(x => x.prio === 'urgent' && isActive(x));
    if (state.tab === 'done')    rows = all.filter(x => x.state === 'done');

    // search
    const q = state.query.trim();
    if (q) {
      rows = rows.filter(x =>
        x.pet.name.includes(q) ||
        x.pet.owner.includes(q) ||
        (C().DOCTORS.find(d => d.id === x.doc) || {}).name?.includes(q)
      );
    }

    const total = rows.length;
    $('#rc-total').textContent = total;
    const start = (state.page - 1) * state.pageSize;
    const slice = rows.slice(start, start + state.pageSize);
    $('#rc-page-num').textContent = state.page;

    const host = $('#rc-list');
    if (!slice.length) {
      host.innerHTML = `
        <div class="rc-empty">
          <div class="ico">${window.Icons.inbox || window.Icons.clipboard}</div>
          <div style="font-weight:800;color:var(--ink);margin-bottom:4px">موردی در این فیلتر یافت نشد</div>
          <div>تغییر فیلتر یا ثبت مراجعهٔ جدید را امتحان کنید.</div>
        </div>`;
      return;
    }

    host.innerHTML = slice.map((qt, i) => {
      const seq = start + i + 1;
      const prio = PRIO_META[qt.prio] || PRIO_META.normal;
      const src  = SRC_META[qt.src] || SRC_META.walkin;
      const doc  = C().DOCTORS.find(d => d.id === qt.doc) || C().DOCTORS[0];
      const cls = `rc-row ${qt.prio === 'urgent' ? 'urgent' : ''} ${qt.state}`;
      const actions = actionsFor(qt);
      return `
        <article class="${cls}" role="listitem">
          <div class="seq">${window.toFa ? window.toFa(seq) : seq}</div>
          <div>
            <div class="rc-pet">
              <div class="avbox">${petAvatar(qt)}</div>
              <div class="meta">
                <div class="name">${qt.pet.name} <span style="color:var(--muted-2);font-weight:700;font-size:12px">· ${qt.pet.kind}</span></div>
                <div class="sub">مالک: <b>${qt.pet.owner}</b> · <b>${qt.pet.phone}</b></div>
                <div class="rc-pills">
                  <span class="pill pill-${prio.tone === 'danger' ? 'danger' : (prio.tone === 'ok' ? 'ok' : 'mute')}"><b>${prio.label}</b></span>
                  <span class="pill pill-info">${src.label}</span>
                  <span class="pill pill-${stateMetaTone(qt.state)}">${stateMetaLabel(qt.state)}</span>
                  <span class="src-dot"><span class="dot" style="background:${src.color}"></span>${doc.spec}</span>
                </div>
                <div class="rc-actions">${actions}</div>
              </div>
            </div>
          </div>
          <div class="rc-when">
            <div class="time">${fmtTime(qt.at)}</div>
            <div class="dur">${qt.du || 30} دقیقه</div>
            <div class="doc"><span class="av" style="background:${doc.ac}">${doc.ini}</span>${doc.short}</div>
          </div>
        </article>`;
    }).join('');
  }
  function stateMetaLabel(s) {
    return ({waiting:'در انتظار',called:'فراخوانده',inVisit:'در ویزیت',done:'پایان‌یافته',left:'ترک صف',noshow:'عدم مراجعه',cancel:'لغو'})[s] || s;
  }
  function stateMetaTone(s) {
    return ({waiting:'info',called:'warn',inVisit:'ok',done:'ok',left:'mute',noshow:'danger',cancel:'mute'})[s] || 'mute';
  }

  function actionsFor(qt) {
    const b = (cls, icon, label, fn) => `<button class="btn ${cls}" data-act="${fn}" data-id="${qt.id}">${window.Icons[icon] || ''}<span>${label}</span></button>`;
    const out = [];
    if (qt.state === 'waiting') {
      out.push(b('btn-primary', 'megaphone', 'فراخوان پزشک', 'call'));
      out.push(b('btn-outline', 'briefcase', 'ورود به ویزیت', 'visit'));
      out.push(b('btn-ghost', 'check', 'پذیرش قطعی', 'admit'));
    } else if (qt.state === 'called') {
      out.push(b('btn-primary', 'briefcase', 'ورود به ویزیت', 'visit'));
    } else if (qt.state === 'inVisit') {
      out.push(b('btn-outline', 'file', 'مشاهده پرونده', 'view-enc'));
    }
    out.push(b('btn-ghost', 'alert', qt.prio === 'urgent' ? 'عادی' : 'اورژانس', 'prio'));
    out.push(b('btn-ghost', 'phone', 'تماس', 'call-owner'));
    out.push(b('btn-danger-ghost', 'x', 'ترک/عدم مراجعه', 'left'));
    return out.join('');
  }

  /* ---------------- events ---------------- */
  function onListClick(e) {
    const btn = e.target.closest('button[data-act]');
    if (!btn) return;
    const id = btn.getAttribute('data-id');
    const act = btn.getAttribute('data-act');
    const qt = C().ticket(id);
    if (!qt) return;

    const dispatch = {
      'admit':       () => admitTicket(qt),
      'call':        () => callDoctor(qt),
      'visit':       () => startVisit(qt),
      'left':        () => leaveQueue(qt),
      'noshow':      () => noShow(qt),
      'prio':        () => toggleUrgent(qt),
      'call-owner':  () => toast(`تماس با ${qt.pet.phone}… (نمونه طراحی)`, 'phone'),
      'view-enc':    () => navigateTo('visits.html#enc-' + (C().ENCOUNTERS.find(e=>e.ticketId === qt.id)||{}).id),
    }[act];
    if (dispatch) dispatch();
  }
  function onListKey(e) {
    if (e.key !== 'Enter') return;
    const row = e.target.closest('.rc-row');
    if (!row) return;
    row.querySelector('button[data-act]')?.click();
  }

  function bind() {
    $('#rc-list').addEventListener('click', onListClick);
    $('#rc-list').addEventListener('keydown', onListKey);

    $$('#rc-tabs button').forEach(b => b.addEventListener('click', () => {
      $$('#rc-tabs button').forEach(x => x.classList.remove('on'));
      b.classList.add('on');
      state.tab = b.dataset.tab;
      state.page = 1;
      renderList();
    }));

    $('#rc-search').addEventListener('input', (e) => {
      state.query = e.target.value || '';
      state.page = 1;
      renderList();
    });

    $('#rc-page-size').addEventListener('change', (e) => {
      state.pageSize = parseInt(window.toFa ? e.target.value : e.target.value, 10) || 8;
      state.page = 1;
      renderList();
    });
    $('#rc-prev').addEventListener('click', () => { state.page = Math.max(1, state.page - 1); renderList(); });
    $('#rc-next').addEventListener('click', () => {
      const total = +($('#rc-total').textContent.replace(/[۰-۹]/g, ch => '۰۱۲۳۴۵۶۷۸۹'.indexOf(ch)));
      const max = Math.max(1, Math.ceil(total / state.pageSize));
      state.page = Math.min(max, state.page + 1); renderList();
    });

    $('#rc-walkin').addEventListener('click', () => openModal('walkin'));
    $('#rc-refresh').addEventListener('click', () => { render(); toast('صف بازخوانی شد (نمونه طراحی)', 'refresh'); });
    $('#rc-filter-btn').addEventListener('click', () => toast('فیلتر پیشرفته به‌زودی (نمونه طراحی)', 'bulb'));
  }

  /* ---------------- walk-in modal ---------------- */
  function openModal(kind) {
    state.modal = kind;
    const root = document.createElement('div');
    root.className = 'rc-modal-backdrop';
    root.id = 'rc-modal-root';
    root.addEventListener('click', (e) => { if (e.target === root) closeModal(); });
    document.body.appendChild(root);
    if (kind === 'walkin') renderWalkinModal(root);
  }
  function closeModal() {
    const el = $('#rc-modal-root'); if (el) el.remove();
    state.modal = null; state.modalTarget = null;
  }
  function renderWalkinModal(root) {
    const doctors = C().DOCTORS;
    root.innerHTML = `
      <div class="rc-modal" role="dialog" aria-label="ثبت مراجعهٔ حضوری">
        <div style="display:flex; align-items:center; gap:10px; margin-bottom: 12px;">
          <div style="width:36px;height:36px;border-radius:10px;background:var(--brand-50);color:var(--brand-700);display:grid;place-items:center">${Icons.plus || Icons.check}</div>
          <h3 style="margin:0;flex:1">ثبت مراجعهٔ حضوری</h3>
          <button class="close-btn" data-close>${Icons.x}</button>
        </div>
        <div class="grid2">
          <div>
            <label>نام حیوان</label>
            <input class="input" placeholder="مثلاً: پشمک" data-f="name"/>
          </div>
          <div>
            <label>نام مالک</label>
            <input class="input" placeholder="نام و نام خانوادگی" data-f="owner"/>
          </div>
        </div>
        <div class="grid2" style="margin-top:10px">
          <div>
            <label>شماره تماس</label>
            <input class="input" placeholder="۰۹xxxxxxxxx" data-f="phone"/>
          </div>
          <div>
            <label>پزشک معالج</label>
            <select class="selectbox" data-f="doc">
              ${doctors.map(d => `<option value="${d.id}">${d.short} · ${d.spec}</option>`).join('')}
            </select>
          </div>
        </div>
        <div style="margin-top:10px">
          <label>اولویت</label>
          <div style="display:flex; gap:8px">
            <label style="display:inline-flex; gap:6px; align-items:center; padding:8px 12px; border-radius:10px; background:var(--bg-2); cursor:pointer">
              <input type="radio" name="prio" value="normal" checked/> عادی
            </label>
            <label style="display:inline-flex; gap:6px; align-items:center; padding:8px 12px; border-radius:10px; background:var(--bg-2); cursor:pointer">
              <input type="radio" name="prio" value="urgent"/> اورژانس
            </label>
          </div>
        </div>
        <div class="actions">
          <button class="btn btn-ghost" data-close>انصراف</button>
          <button class="btn btn-primary" id="rc-modal-save">${Icons.check}<span>ثبت مراجعه</span></button>
        </div>
      </div>`;
    root.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', closeModal));
    $('#rc-modal-save', root).addEventListener('click', () => {
      const name = root.querySelector('[data-f="name"]').value.trim() || 'بدون‌نام';
      const owner = root.querySelector('[data-f="owner"]').value.trim() || '—';
      const phone = root.querySelector('[data-f="phone"]').value.trim() || '—';
      const docId = root.querySelector('[data-f="doc"]').value;
      const prio = root.querySelector('input[name="prio"]:checked').value;
      createWalkin({ name, owner, phone, docId, urgent: prio === 'urgent' });
    });
  }

  /* ---------------- deep-link #admit-<qtId|apptId> ---------------- */
  function interpretHash() {
    const h = location.hash || '';
    const m = h.match(/^#admit-(.+)$/);
    if (!m) return;
    const ref = m[1];
    let qt = C().QUEUE.find(q => q.id === ref || q.apptId === ref);
    if (!qt) { toast('این نوبت در صف امروز یافت نشد.', 'x'); return; }
    if (qt.state === 'waiting') {
      admitTicket(qt);
      // رفتن به بالای کارت مرتبط
      setTimeout(() => {
        const el = document.querySelector('[data-id="' + qt.id + '"]');
        el?.closest('.rc-row')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }, 50);
    }
  }
  window.addEventListener('hashchange', interpretHash);

  /* ---------------- boot ---------------- */
  window.initReceptionPage = function () {
    if (!window.FLOW || !window.CLINIC) {
      console.error('[reception] FLOW/CLINIC not loaded');
      return;
    }
    FLOW.seedToday(); // warm
    bind();
    render();
    interpretHash();
    // gentle heartbeat for hero timestamp (~no-op renders)
    setInterval(() => $('#rc-now-hm').textContent = nowHM(), 60_000);
  };
})();
