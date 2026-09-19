/* =========================================================
   Vetplus · Vaccination — page controller
   Design-only: mutations live in memory, actions toast.
   ========================================================= */
(function () {
  'use strict';

  const fa = window.vfa;
  const VJ = window.VJ;

  /* ---------------- module state ---------------- */
  const S = { seg: 'all', q: '', species: '', page: 1, per: 8 };
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ---------------- tiny helpers ---------------- */
  const el = (id) => document.getElementById(id);
  const bdi = (v) => `<bdi class="num num-fa">${fa(v)}</bdi>`;
  const speciesLabel = (k) => (window.VAX_SPECIES[k] || { label: 'سایر' }).label;

  /* toast — reuse global if a sibling page defined it, else minimal local one */
  function toast(msg, icon) {
    if (typeof window.toast === 'function' && window.toast !== toast) return window.toast(msg, icon);
    let t = el('vxToast');
    if (!t) {
      t = document.createElement('div');
      t.id = 'vxToast';
      t.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%) translateY(20px);' +
        'background:var(--ink);color:#fff;padding:11px 18px;border-radius:12px;font-size:13px;font-weight:700;' +
        'z-index:200;opacity:0;transition:all .25s cubic-bezier(.4,0,.2,1);box-shadow:0 12px 30px -10px rgba(0,0,0,.5);' +
        'display:flex;align-items:center;gap:8px;font-family:inherit';
      document.body.appendChild(t);
    }
    t.innerHTML = `<span style="display:inline-flex;color:var(--brand-400)">${window.Icons[icon] || window.Icons.check}</span>${msg}`;
    requestAnimationFrame(() => { t.style.opacity = '1'; t.style.transform = 'translateX(-50%) translateY(0)'; });
    clearTimeout(t._h);
    t._h = setTimeout(() => { t.style.opacity = '0'; t.style.transform = 'translateX(-50%) translateY(20px)'; }, 2400);
  }
  window.vaxToast = toast;

  /* ---------------- filtering ---------------- */
  function filtered() {
    return window.VAX_RECORDS.filter((r) => {
      if (S.seg !== 'all' && r.status !== S.seg) return false;
      if (S.species && r.species !== S.species) return false;
      if (S.q) {
        const v = window.vaxById(r.vaccineId);
        const hay = (r.pet + ' ' + r.owner + ' ' + (v ? v.name + ' ' + v.short : '') + ' ' + r.dose).toLowerCase();
        if (!hay.includes(S.q.toLowerCase())) return false;
      }
      return true;
    });
  }

  /* ---------------- segmented control ---------------- */
  function renderSeg() {
    const c = window.vaxCounts();
    const segs = [
      { k: 'all', label: 'همه', n: c.total, tone: 'teal' },
      { k: 'due', label: 'نزدیک', n: c.due, tone: 'blue' },
      { k: 'overdue', label: 'عقب‌افتاده', n: c.overdue, tone: 'peach' },
      { k: 'scheduled', label: 'آتی', n: c.scheduled, tone: 'lilac' },
      { k: 'done', label: 'انجام‌شده', n: c.done, tone: 'green' },
    ];
    el('vxSeg').innerHTML = segs.map((s) => `
      <button class="pb-chip tone-${s.tone} ${S.seg === s.k ? 'on' : ''}" data-seg="${s.k}" role="tab" aria-selected="${S.seg === s.k}">
        <span class="dot"></span><span>${s.label}</span><span class="cnt num-fa">${fa(s.n)}</span>
      </button>`).join('');
  }

  /* ---------------- records table ---------------- */
  function dueCell(r) {
    if (r.status === 'done') {
      return `<div class="vx-due"><span class="d num-fa">${VJ.long(r.dueDate)}</span>
        <span class="rel far">نوبت بعدی</span></div>`;
    }
    const d = VJ.diff(r.dueDate);
    let cls = 'far', txt = '';
    if (d < 0) { cls = 'past'; txt = `${fa(Math.abs(d))} روز گذشته`; }
    else if (d === 0) { cls = 'soon'; txt = 'امروز'; }
    else if (d <= 14) { cls = 'soon'; txt = `${fa(d)} روز مانده`; }
    else { cls = 'far'; txt = `${fa(d)} روز مانده`; }
    return `<div class="vx-due"><span class="d num-fa">${VJ.long(r.dueDate)}</span>
      <span class="rel ${cls} num-fa">${txt}</span></div>`;
  }

  function renderRows() {
    const list = filtered();
    const host = el('vxRows');
    if (!list.length) {
      host.innerHTML = `<tr><td colspan="6"><div class="vx-empty">${window.Icons.syringe}
        <div>موردی مطابق فیلترها یافت نشد.</div></div></td></tr>`;
      el('vxPager').innerHTML = '';
      return;
    }
    const pages = Math.max(1, Math.ceil(list.length / S.per));
    if (S.page > pages) S.page = pages;
    const slice = list.slice((S.page - 1) * S.per, S.page * S.per);

    host.innerHTML = slice.map((r) => {
      const v = window.vaxById(r.vaccineId) || {};
      const st = window.vaxStatusMeta[r.status];
      const sm = window.vaxSmsMeta[r.smsState];
      return `
      <tr>
        <td>
          <div class="vx-pet-cell">
            <div class="avatar-pet ${r.tone}">${r.pet[0]}</div>
            <div>
              <div class="name">${r.pet} <span class="muted" style="font-size:11px;font-weight:400">· ${speciesLabel(r.species)}</span></div>
              <div class="sub">مالک: ${r.owner}</div>
            </div>
          </div>
        </td>
        <td>
          <div class="vx-vac-cell">
            <span class="v-name">${v.name || '—'}</span>
            <span class="v-dose"><span class="vx-tag">${v.short || ''}</span> ${r.dose}</span>
          </div>
        </td>
        <td>${dueCell(r)}</td>
        <td><span class="pill pill-${st.tone}">${st.label}</span></td>
        <td><span class="vx-sms-cell ${sm.tone}">${window.Icons[sm.icon]}${sm.label}</span></td>
        <td>
          <div class="vx-row-actions">
            <button class="vx-mini" title="ارسال پیامک" onclick="openSmsCompose('${r.id}')">${window.Icons.msg}</button>
            <button class="vx-mini done" title="ثبت انجام واکسن" onclick="vaxMarkDone('${r.id}')">${window.Icons.check}</button>
            <button class="vx-mini" title="جزئیات" onclick="openVaxDrawer('${r.id}')">${window.Icons.chevronLeft}</button>
          </div>
        </td>
      </tr>`;
    }).join('');

    // pager
    let btns = '';
    for (let i = 1; i <= pages; i++) {
      btns += `<button class="${i === S.page ? 'active' : ''}" data-pg="${i}">${fa(i)}</button>`;
    }
    el('vxPager').innerHTML = `
      <span>نمایش <b class="num-fa">${fa(slice.length)}</b> از <b class="num-fa">${fa(list.length)}</b> نوبت</span>
      <div class="pages">${btns}</div>`;
  }

  /* ---------------- vaccine catalog ---------------- */
  function renderCatalog() {
    const toneColor = {
      a: '#8f74d1', b: '#e08a5c', c: '#eab749', d: '#d16060', e: '#4aa591', f: '#5c8ad6',
    };
    el('vxCatalog').innerHTML = window.VAX_CATALOG.map((v) => {
      const sp = window.VAX_SPECIES[v.species] || window.VAX_SPECIES.all;
      const low = v.stock <= v.min;
      const pct = Math.min(100, Math.round((v.stock / (v.min * 3)) * 100));
      const col = toneColor[v.tone] || '#8f74d1';
      return `
      <div class="vx-cat">
        <div class="vx-cat-head">
          <div class="vx-cat-ic" style="background:${col}22;color:${col}">${window.Icons[sp.icon] || window.Icons.syringe}</div>
          <div>
            <div class="c-name">${v.name}</div>
            <div class="c-short">${v.short} · ${sp.label}</div>
          </div>
        </div>
        <div class="c-protects">${v.protects}</div>
        <div class="vx-cat-foot">
          <span class="vx-stockbar"><i style="width:${pct}%;background:${low ? 'var(--danger)' : col}"></i></span>
          <span class="c-stock ${low ? 'low' : ''} num-fa">${fa(v.stock)} دُز</span>
        </div>
      </div>`;
    }).join('');
  }

  /* ---------------- SMS templates ---------------- */
  function renderTemplates() {
    el('vxTemplates').innerHTML = window.VAX_SMS_TEMPLATES.map((t) => `
      <div class="vx-tmpl" onclick="vaxUseTemplate('${t.id}')">
        <div class="tm-name">${t.name} ${window.Icons.chevronLeft}</div>
        <div class="tm-body">${t.body}</div>
      </div>`).join('');
  }

  /* ---------------- reveal-on-scroll ---------------- */
  function mountReveal() {
    if (reduce) { document.querySelectorAll('.reveal').forEach((n) => n.classList.add('in')); return; }
    const io = new IntersectionObserver((ents) => {
      ents.forEach((e, i) => {
        if (e.isIntersecting) { setTimeout(() => e.target.classList.add('in'), i * 70); io.unobserve(e.target); }
      });
    }, { threshold: .12 });
    document.querySelectorAll('.reveal').forEach((n) => io.observe(n));
  }

  /* =========================================================
     Overlays
     ========================================================= */

  /* ---- record detail — centered modal ---- */
  function ensureDrawer() {
    if (el('vxDrawer')) return el('vxDrawer');
    const d = document.createElement('div');
    d.className = 'vx-detail-root';
    d.id = 'vxDrawer';
    d.innerHTML = `<div class="vx-detail-bd" onclick="closeVaxDrawer()"></div>
      <div class="vx-detail-card" id="vxDrawerBody" role="dialog" aria-modal="true"></div>`;
    document.body.appendChild(d);
    return d;
  }

  window.openVaxDrawer = function (id) {
    const r = window.VAX_RECORDS.find((x) => x.id === id);
    if (!r) return;
    const v = window.vaxById(r.vaccineId) || {};
    const st = window.vaxStatusMeta[r.status];
    const sm = window.vaxSmsMeta[r.smsState];
    ensureDrawer();
    // fake short history for this pet
    const tl = [
      { title: `${v.name || 'واکسن'} — ${r.dose}`, meta: `${r.status === 'done' ? 'انجام‌شده' : 'سررسید'}: ${VJ.long(r.dueDate)} · ${r.vet}`, active: true },
    ];
    if (r.lastDate && r.lastDate !== '—') {
      tl.push({ title: `${v.name || 'واکسن'} — نوبت قبلی`, meta: `${VJ.long(r.lastDate)} · ${r.vet}`, active: false });
    }
    tl.push({ title: 'ثبت پرونده و معاینه اولیه', meta: 'کلینیک وت‌پلاس', active: false });

    el('vxDrawerBody').innerHTML = `
      <div class="vx-dr-head">
        <div class="avatar-pet ${r.tone}">${r.pet[0]}</div>
        <div style="flex:1">
          <div class="h-name">${r.pet}</div>
          <div class="h-sub">${speciesLabel(r.species)} · مالک: ${r.owner}</div>
        </div>
        <button class="icon-btn close" onclick="closeVaxDrawer()" title="بستن">${window.Icons.x}</button>
      </div>
      <div class="vx-dr-body">
        <div style="display:flex;gap:8px;margin-bottom:6px">
          <span class="pill pill-${st.tone}">${st.label}</span>
          <span class="vx-sms-cell ${sm.tone}">${window.Icons[sm.icon]}${sm.label}</span>
        </div>

        <div class="vx-dr-sec-title">مشخصات نوبت</div>
        <dl class="vx-kv">
          <dt>واکسن</dt><dd>${v.name || '—'} <span class="vx-tag">${v.short || ''}</span></dd>
          <dt>نوبت</dt><dd>${r.dose}</dd>
          <dt>سررسید بعدی</dt><dd class="num-fa">${VJ.long(r.dueDate)}</dd>
          <dt>آخرین تزریق</dt><dd class="num-fa">${r.lastDate === '—' ? '—' : VJ.long(r.lastDate)}</dd>
          <dt>محل تزریق</dt><dd>${r.site}</dd>
          <dt>شماره سری (Lot)</dt><dd>${bdi(r.lot)}</dd>
          <dt>دامپزشک</dt><dd>${r.vet}</dd>
          <dt>موبایل مالک</dt><dd>${bdi(r.mobile)}</dd>
        </dl>

        <div class="vx-dr-sec-title">پیشینه واکسیناسیون</div>
        <div class="vx-timeline">
          ${tl.map((t) => `<div class="vx-tl-item ${t.active ? '' : 'muted'}">
            <div class="tl-title">${t.title}</div>
            <div class="tl-meta num-fa">${t.meta}</div></div>`).join('')}
        </div>

        <div style="display:flex;gap:10px;margin-top:22px">
          <button class="btn btn-primary" style="flex:1" onclick="openSmsCompose('${r.id}')">
            ${window.Icons.msg} ارسال پیامک یادآور
          </button>
          <button class="btn btn-outline" onclick="vaxMarkDone('${r.id}');closeVaxDrawer()">
            ${window.Icons.check} ثبت انجام
          </button>
        </div>
      </div>`;
    requestAnimationFrame(() => ensureDrawer().classList.add('open'));
  };
  window.closeVaxDrawer = function () { const d = el('vxDrawer'); if (d) d.classList.remove('open'); };

  /* ---- SMS composer modal ---- */
  function ensureModal() {
    if (el('vxModal')) return el('vxModal');
    const m = document.createElement('div');
    m.className = 'modal-backdrop';
    m.id = 'vxModal';
    m.style.display = 'none';
    m.innerHTML = '<div class="modal" id="vxModalCard"></div>';
    m.addEventListener('click', (e) => { if (e.target === m) closeVaxModalGeneric(); });
    document.body.appendChild(m);
    return m;
  }
  function openModal() { const m = ensureModal(); m.style.display = 'flex'; }
  window.closeVaxModalGeneric = function () { const m = el('vxModal'); if (m) m.style.display = 'none'; };

  function buildPreview(r, tmpl) {
    const v = window.vaxById(r.vaccineId) || {};
    return tmpl.body
      .replace('{owner}', r.owner)
      .replace('{pet}', r.pet)
      .replace('{vaccine}', v.name || 'واکسن')
      .replace('{date}', VJ.long(r.dueDate))
      .replace('{next}', VJ.long(r.dueDate));
  }

  window.openSmsCompose = function (id) {
    const r = window.VAX_RECORDS.find((x) => x.id === id);
    if (!r) return;
    closeVaxDrawer();
    let current = window.VAX_SMS_TEMPLATES[0];
    openModal();

    function paint() {
      const body = buildPreview(r, current);
      const chars = body.length;
      const parts = Math.ceil(chars / 70) || 1;
      el('vxModalCard').innerHTML = `
        <div class="modal-header">
          <div class="icon-box">${window.Icons.msg}</div>
          <div class="titles"><div class="eyebrow">پیامک یادآور</div><h3>ارسال پیامک به مالک</h3></div>
          <button class="icon-btn close" onclick="closeVaxModalGeneric()" title="بستن">${window.Icons.x}</button>
        </div>
        <div class="modal-body">
          <div class="vx-compose-to">
            <div class="avatar-pet ${r.tone}">${r.pet[0]}</div>
            <div style="flex:1">
              <div class="to-name">${r.owner} <span class="muted" style="font-size:11px;font-weight:400">· مالک ${r.pet}</span></div>
              <div class="to-mob">${bdi(r.mobile)}</div>
            </div>
          </div>
          <div class="field"><label>قالب پیامک</label></div>
          <div class="vx-tmpl-chips" id="vxComposeChips">
            ${window.VAX_SMS_TEMPLATES.map((t) => `<span class="chip ${t.id === current.id ? '' : ''}" data-t="${t.id}"
              style="${t.id === current.id ? 'background:var(--vx-shot-bg);color:var(--vx-shot)' : ''}">${t.name}</span>`).join('')}
          </div>
          <div class="field"><label>پیش‌نمایش پیام</label></div>
          <div class="vx-preview">${body}</div>
          <div class="vx-count">تعداد نویسه: <b class="num-fa">${fa(chars)}</b> · <b class="num-fa">${fa(parts)}</b> پیامک</div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-ghost" onclick="closeVaxModalGeneric()">انصراف</button>
          <button class="btn btn-primary" onclick="vaxSendSms('${r.id}')">${window.Icons.share} ارسال پیامک</button>
        </div>`;
      el('vxComposeChips').querySelectorAll('.chip').forEach((c) => {
        c.addEventListener('click', () => {
          current = window.VAX_SMS_TEMPLATES.find((t) => t.id === c.dataset.t);
          paint();
        });
      });
    }
    paint();
  };

  window.vaxSendSms = function (id) {
    const r = window.VAX_RECORDS.find((x) => x.id === id);
    if (r) { r.smsState = 'sent'; renderRows(); }
    closeVaxModalGeneric();
    toast('پیامک یادآور ارسال شد (نمونه طراحی)', 'check');
  };

  /* ---- SMS settings modal ---- */
  window.openSmsSettings = function () {
    openModal();
    el('vxModalCard').innerHTML = `
      <div class="modal-header">
        <div class="icon-box">${window.Icons.bell}</div>
        <div class="titles"><div class="eyebrow">تنظیمات</div><h3>پیامک اخطار خودکار</h3></div>
        <button class="icon-btn close" onclick="closeVaxModalGeneric()" title="بستن">${window.Icons.x}</button>
      </div>
      <div class="modal-body">
        <div class="field"><label>سرشماره ارسال</label><input class="input" value="۵۰۰۰۲۱۰۰۰۰" dir="ltr"/></div>
        <div class="grid-2" style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
          <div class="field"><label>زمان ارسال روزانه</label>
            <select class="selectbox"><option>۰۹:۰۰ صبح</option><option>۱۱:۰۰ صبح</option><option>۱۷:۰۰ عصر</option></select></div>
          <div class="field"><label>فاصله یادآوری</label>
            <select class="selectbox"><option>۳ روز پیش از نوبت</option><option>۷ روز پیش از نوبت</option><option>۱ روز پیش از نوبت</option></select></div>
        </div>
        <div class="vx-toggle-row" style="margin-top:6px">
          <div><div class="t-label">ارسال در روزهای تعطیل</div><div class="t-sub">پیامک‌ها در تعطیلات نیز ارسال شوند</div></div>
          <label class="vx-switch"><input type="checkbox"><span class="track"></span><span class="thumb"></span></label>
        </div>
      </div>
      <div class="modal-footer">
        <button class="btn btn-ghost" onclick="closeVaxModalGeneric()">انصراف</button>
        <button class="btn btn-primary" onclick="closeVaxModalGeneric();vaxToast('تنظیمات پیامک ذخیره شد (نمونه طراحی)','check')">ذخیره تنظیمات</button>
      </div>`;
  };

  /* ---- add vaccine modal ---- */
  window.openVaxModal = function () {
    openModal();
    const opts = window.VAX_CATALOG.map((v) => `<option value="${v.id}">${v.name} (${v.short})</option>`).join('');
    el('vxModalCard').innerHTML = `
      <div class="modal-header">
        <div class="icon-box">${window.Icons.syringe}</div>
        <div class="titles"><div class="eyebrow">ثبت جدید</div><h3>ثبت واکسن بیمار</h3></div>
        <button class="icon-btn close" onclick="closeVaxModalGeneric()" title="بستن">${window.Icons.x}</button>
      </div>
      <div class="modal-body">
        <div class="field"><label>بیمار</label><input class="input" placeholder="نام بیمار را جستجو کنید…"/></div>
        <div class="grid-2" style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
          <div class="field"><label>نوع واکسن</label><select class="selectbox" id="vxNewType">${opts}</select></div>
          <div class="field"><label>نوبت</label>
            <select class="selectbox"><option>نوبت اول</option><option>یادآور اول</option><option>یادآور دوم</option><option>نوبت سالانه</option></select></div>
          <div class="field"><label>تاریخ تزریق</label><input class="input num-fa" placeholder="۱۴۰۴/۰۵/۱۱"/></div>
          <div class="field"><label>سررسید بعدی</label><input class="input num-fa" placeholder="۱۴۰۵/۰۵/۱۱"/></div>
          <div class="field"><label>دامپزشک</label>
            <select class="selectbox"><option>دکتر آریا نیک</option><option>دکتر سمانه راد</option></select></div>
          <div class="field"><label>شماره سری (Lot)</label><input class="input" dir="ltr" placeholder="FV-8821"/></div>
        </div>
        <div class="vx-toggle-row" style="margin-top:4px">
          <div><div class="t-label">ارسال پیامک تأیید</div><div class="t-sub">پس از ثبت به مالک ارسال شود</div></div>
          <label class="vx-switch"><input type="checkbox" checked><span class="track"></span><span class="thumb"></span></label>
        </div>
      </div>
      <div class="modal-footer">
        <button class="btn btn-ghost" onclick="closeVaxModalGeneric()">انصراف</button>
        <button class="btn btn-primary" onclick="closeVaxModalGeneric();vaxToast('واکسن ثبت شد (نمونه طراحی)','check')">ثبت واکسن</button>
      </div>`;
  };

  /* ---------------- row/quick actions ---------------- */
  window.vaxMarkDone = function (id) {
    const r = window.VAX_RECORDS.find((x) => x.id === id);
    if (r) { r.status = 'done'; renderSeg(); renderRows(); }
    toast('واکسن به‌عنوان انجام‌شده ثبت شد (نمونه طراحی)', 'check');
  };
  window.vaxUseTemplate = function (tid) {
    const t = window.VAX_SMS_TEMPLATES.find((x) => x.id === tid);
    toast(`قالب «${t ? t.name : ''}» انتخاب شد (نمونه طراحی)`, 'msg');
  };
  window.vaxToggle = function (input, name) {
    toast(`${name} ${input.checked ? 'فعال' : 'غیرفعال'} شد (نمونه طراحی)`, 'bell');
  };
  window.vaxBulkSms = function () {
    const n = filtered().filter((r) => r.status === 'due' || r.status === 'overdue').length;
    toast(`یادآور برای ${fa(n)} نوبت زمان‌بندی شد (نمونه طراحی)`, 'megaphone');
  };

  /* ---------------- primary section tabs ---------------- */
  const PANELS = { visits: 'vxPanelVisits', stock: 'vxPanelStock', settings: 'vxPanelSettings' };
  function switchTab(key) {
    if (!PANELS[key]) return;
    document.querySelectorAll('#vxMainSeg > button').forEach((b) => {
      const on = b.dataset.tab === key;
      b.classList.toggle('active', on);
      b.setAttribute('aria-selected', on ? 'true' : 'false');
    });
    Object.entries(PANELS).forEach(([k, id]) => {
      const p = el(id); if (p) p.hidden = (k !== key);
    });
    // visits-only filter rows live in the shared toolbar; show only on نوبت
    document.querySelectorAll('.vx-visits-tools').forEach((r) => { r.hidden = (key !== 'visits'); });
  }

  /* inject icons into the main section bar (raw SVG has no intrinsic sizing here) */
  function mountTabIcons() {
    document.querySelectorAll('.vx-mainseg-ic[data-ic]').forEach((n) => {
      n.innerHTML = window.Icons[n.dataset.ic] || '';
    });
  }

  /* ---------------- binding ---------------- */
  function bind() {
    // primary section tabs (now in the toolbar)
    document.querySelectorAll('#vxMainSeg > button').forEach((b) => {
      b.addEventListener('click', () => switchTab(b.dataset.tab));
    });
    // segmented
    el('vxSeg').addEventListener('click', (e) => {
      const b = e.target.closest('button[data-seg]');
      if (!b) return;
      S.seg = b.dataset.seg; S.page = 1;
      renderSeg(); renderRows();
    });
    // search (debounced)
    let h;
    const vxSearchWrap = el('vxSearchWrap');
    el('vxSearch').addEventListener('input', (e) => {
      if (vxSearchWrap) vxSearchWrap.classList.toggle('has-value', !!e.target.value.trim());
      clearTimeout(h);
      h = setTimeout(() => { S.q = e.target.value.trim(); S.page = 1; renderRows(); }, 180);
    });
    const vxClear = el('vxSearchClear');
    if (vxClear) vxClear.addEventListener('click', () => {
      const inp = el('vxSearch'); inp.value = ''; S.q = ''; S.page = 1;
      if (vxSearchWrap) vxSearchWrap.classList.remove('has-value');
      renderRows(); inp.focus();
    });
    // species select
    el('vxSpecies').addEventListener('change', (e) => { S.species = e.target.value; S.page = 1; renderRows(); });
    // pager delegation
    el('vxPager').addEventListener('click', (e) => {
      const b = e.target.closest('button[data-pg]');
      if (!b) return;
      S.page = +b.dataset.pg; renderRows();
      window.scrollTo({ top: el('vxRows').getBoundingClientRect().top + window.scrollY - 120, behavior: reduce ? 'auto' : 'smooth' });
    });
    // keyboard
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { closeVaxDrawer(); closeVaxModalGeneric(); return; }
      if (e.target.matches('input, textarea, select')) return;
      if (e.key === '/') { e.preventDefault(); el('vxSearch').focus(); }
      if (e.key.toLowerCase() === 'n') { e.preventDefault(); openVaxModal(); }
    });
  }

  /* ---------------- boot ---------------- */
  window.initVaccinationPage = function () {
    mountTabIcons();
    renderSeg();
    renderRows();
    renderCatalog();
    renderTemplates();
    bind();
    mountReveal();
  };

})();
