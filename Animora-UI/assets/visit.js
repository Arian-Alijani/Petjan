/* =========================================================
   وت‌پلاس · ویزیت و پرونده — Phase 7
   assets/visit.js — Gate (doctor + entry-mode) + 5-step wizard
   Static UI prototype (L1–L3): in-memory only.
   ========================================================= */
(function () {
  'use strict';

  /* ---------------- helpers ---------------- */
  const $  = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const C  = () => window.CLINIC;

  const toFa = (v) => (window.toFa ? window.toFa(v) : String(v).replace(/[0-9]/g, (d) => '۰۱۲۳۴۵۶۷۸۹'[d]));
  const escHtml = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
  const money = (n) => toFa(Number(n || 0).toLocaleString('en-US')) + ' ریال';
  const fmtTime = (mins) => {
    const h = Math.floor(mins / 60), m = mins % 60;
    return toFa(String(h).padStart(2, '0')) + ':' + toFa(String(m).padStart(2, '0'));
  };
  const fmtDur = (m) => toFa(m) + ' دقیقه';

  if (typeof window.toast !== 'function') {
    window.toast = function (msg, icon = 'check') {
      let host = document.querySelector('.toast-host');
      if (!host) { host = document.createElement('div'); host.className = 'toast-host'; document.body.appendChild(host); }
      const el = document.createElement('div');
      el.className = 'toast' + (icon === 'x' ? ' error' : '');
      el.innerHTML = (window.Icons && window.Icons[icon] ? window.Icons[icon] : '') + '<span></span>';
      el.lastChild.textContent = msg;
      host.appendChild(el);
      setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 320); }, 2600);
    };
  }

  /* ---------------- catalog ---------------- */
  // Visit types (align with appointments.js TYPES)
  const TYPES = [
    { id: 'visit',    label: 'ویزیت عمومی',    icon: 'briefcase', ac: '#0d9488', dur: 30 },
    { id: 'vaccine',  label: 'واکسیناسیون',    icon: 'syringe',   ac: '#1570cd', dur: 20 },
    { id: 'dental',   label: 'دندان‌پزشکی',    icon: 'tools',     ac: '#8f74d1', dur: 60 },
    { id: 'surgery',  label: 'جراحی',          icon: 'tools',     ac: '#d92d20', dur: 90 },
    { id: 'groom',    label: 'آرایش و پیرایش', icon: 'scissors',  ac: '#e08a5c', dur: 60 },
    { id: 'followup', label: 'پیگیری',         icon: 'clock',     ac: '#b54708', dur: 20 },
  ];
  const TYPE = (id) => TYPES.find((t) => t.id === id) || TYPES[0];

  /* ---------------- state ---------------- */
  const S = {
    scene: 'gate',          // 'gate' | 'wizard' | 'done'
    docId: null,            // selected doctor id
    mode: 'appt',           // 'appt' | 'new'  (entry mode tab)
    apptFilter: 'all',      // chip: all | visit | vaccine | dental | surgery | groom | followup
    search: '',
    // new-visit builder
    nb: {
      patient: null,        // { source: 'existing'|'new', name, kind, ic, owner, phone }
      type: 'visit',
      priority: 'normal',
      note: '',
    },
    // wizard
    step: 0,                // 0..4
    ticket: null,           // active queue ticket (real or synthetic)
    enc: null,              // active encounter draft
  };

  const STEPS = [
    { key: 'vitals',  label: 'علائم حیاتی',       short: 'علائم حیاتی' },
    { key: 'history', label: 'شرح‌حال و معاینه',   short: 'شرح‌حال'      },
    { key: 'plan',    label: 'تشخیص و طرح درمان', short: 'تشخیص'       },
    { key: 'rx',      label: 'خدمات و نسخه',      short: 'خدمات و نسخه'},
    { key: 'review',  label: 'صدور فاکتور',       short: 'فاکتور'      },
  ];

  /* ---------------- pet-icon helper ---------------- */
  const petIcClass = (ic) => (['cat', 'dog', 'bird', 'rabbit'].includes(ic) ? ic : 'cat');
  const petIcSvg = (ic) => (window.Icons && window.Icons[petIcClass(ic)]) || window.Icons.paw;

  /* ---------------- boot ---------------- */
  window.initVisitPage = function () {
    renderScene();
  };

  function renderScene() {
    $('#scene-gate').classList.toggle('hidden', S.scene !== 'gate');
    $('#scene-wizard').classList.toggle('hidden', S.scene !== 'wizard');
    $('#scene-done').classList.toggle('hidden', S.scene !== 'done');

    // Re-add scene animation class (retrigger fadeUp)
    ['#scene-gate','#scene-wizard','#scene-done'].forEach((sel) => {
      const el = $(sel);
      el.classList.remove('scene');
      // force reflow
      // eslint-disable-next-line no-unused-expressions
      el.offsetHeight;
      el.classList.add('scene');
    });

    if (S.scene === 'gate') renderGate();
    else if (S.scene === 'wizard') renderWizard();
    else if (S.scene === 'done') renderDone();
  }

  /* ===========================================================
     SCENE A — GATE
     =========================================================== */

  function renderGate() {
    renderDocRail();
    renderWorkspace();
  }

  // ------- Doctor rail -------
  function docApptCount(docId) {
    const t = C().today();
    return C().QUEUE.filter((q) =>
      q.doc === docId && q.date === t && q.src === 'appointment' && q.state !== 'done'
    ).length;
  }
  function docWalkinCount(docId) {
    const t = C().today();
    return C().QUEUE.filter((q) =>
      q.doc === docId && q.date === t && q.src !== 'appointment' && q.state !== 'done'
    ).length;
  }

  function renderDocRail() {
    const docs = C().DOCTORS;
    const html = `
      <div class="rail-head">
        <div class="ttl">
          <div class="n">۱</div>
          <span>انتخاب پزشک</span>
        </div>
        <span class="hint">${toFa(docs.length)} پزشک فعال</span>
      </div>
      <div class="doc-list">
        ${docs.map((d) => {
          const bc = docApptCount(d.id);
          const active = S.docId === d.id;
          return `
            <div class="doc-card ${active ? 'is-selected' : ''}" data-doc="${d.id}" role="button" tabindex="0">
              <div class="av" style="background:${d.ac}">${escHtml(d.ini)}</div>
              <div class="meta">
                <div class="name">${escHtml(d.name)}</div>
                <div class="spec">${escHtml(d.spec)}</div>
              </div>
              <div class="metric" title="نوبت‌های امروز">
                <span class="n">${toFa(bc)}</span>
                <span class="l">نوبت امروز</span>
              </div>
              <div class="check">${window.Icons.check}</div>
            </div>
          `;
        }).join('')}
      </div>
      <div class="rail-foot">
        ${window.Icons.bulb || ''}
        <span>هر پزشک نوبت‌های خاص خود را دارد؛ بعد از انتخاب، سمت راست تنظیم می‌شود.</span>
      </div>
    `;
    $('#docRail').innerHTML = html;

    // events
    $$('#docRail .doc-card').forEach((el) => {
      el.addEventListener('click', () => selectDoctor(el.dataset.doc));
      el.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); selectDoctor(el.dataset.doc); }
      });
    });
  }

  function selectDoctor(id) {
    if (S.docId === id) return;
    S.docId = id;
    // reset entry-mode selections when switching doctor? Keep mode but reset search
    S.search = '';
    renderDocRail();
    renderWorkspace();
  }

  // ------- Workspace (right side) -------
  function renderWorkspace() {
    if (!S.docId) {
      $('#workspace').innerHTML = emptyStateHtml();
      return;
    }
    const doc = C().DOCTORS.find((d) => d.id === S.docId);
    const nBooked = docApptCount(S.docId);

    const html = `
      ${doctorLockupHtml(doc, nBooked)}
      ${tapeHtml(doc)}
      ${modeSplitHtml(nBooked)}
      <div class="mode-body">
        ${S.mode === 'appt' ? bookedListHtml(doc) : newVisitFormHtml(doc)}
      </div>
    `;
    $('#workspace').innerHTML = html;
    wireWorkspace(doc);
  }

  function emptyStateHtml() {
    return `
      <div class="empty-state">
        <div class="ill">${window.Icons.users || window.Icons.briefcase || ''}</div>
        <h3>یک پزشک را از سمت راست انتخاب کنید</h3>
        <p>برای شروع فرآیند ویزیت، ابتدا مشخص کنید ویزیت توسط کدام پزشک انجام می‌شود. سپس می‌توانید از میان نوبت‌های رزرو شدهٔ همان پزشک برای امروز یکی را برای شروع ویزیت انتخاب کنید یا یک ویزیت جدید (بدون نوبت) ثبت کنید.</p>
        <div class="arrow-hint">
          ${window.Icons.chevronRight}
          <span>انتخاب پزشک از رَیل کناری</span>
        </div>
      </div>
    `;
  }

  function doctorLockupHtml(doc, nBooked) {
    return `
      <div class="doc-lockup">
        <div class="av" style="background:${doc.ac}">${escHtml(doc.ini)}</div>
        <div class="txt">
          <div class="name">${escHtml(doc.name)}</div>
          <div class="spec">${escHtml(doc.spec)} · ${toFa(nBooked)} نوبت رزرو شده برای امروز</div>
        </div>
        <div class="status">
          <span class="dot"></span>
          <span>در دسترس</span>
        </div>
      </div>
    `;
  }

  function tapeHtml(/*doc*/) {
    const today = C().today();
    // Convert "1403/06/12" style — keep as-is; toFa handles digits
    return `
      <div class="tape">
        ${window.Icons.clock || ''}
        <span>مرحلهٔ ۲: نحوهٔ شروع ویزیت را انتخاب کنید</span>
        <span class="today">${toFa(today)}</span>
      </div>
    `;
  }

  function modeSplitHtml(nBooked) {
    const nNew = 0; // walkins today by this doc (not counted separately here)
    return `
      <div class="mode-split">
        <div class="mode-tab ${S.mode === 'appt' ? 'is-active' : ''}" data-mode="appt" role="button" tabindex="0">
          <div class="row">
            <div class="ic">${window.Icons.calendar}</div>
            <div class="head-txt">
              <div class="kicker">گزینهٔ ۱</div>
              <h3>نوبت از قبل رزرو شده</h3>
              <p>از میان نوبت‌های تأیید شدهٔ امروز این پزشک، یکی را انتخاب کنید و مستقیم وارد ویزیت شوید.</p>
              <div class="count"><span class="dot"></span>${toFa(nBooked)} نوبت آمادهٔ ویزیت</div>
            </div>
          </div>
        </div>
        <div class="mode-tab new ${S.mode === 'new' ? 'is-active' : ''}" data-mode="new" role="button" tabindex="0">
          <div class="row">
            <div class="ic">${window.Icons.plus}</div>
            <div class="head-txt">
              <div class="kicker">گزینهٔ ۲</div>
              <h3>ویزیت جدید — بدون نوبت</h3>
              <p>مراجعهٔ حضوری بدون رزرو قبلی؛ بیمار موجود را انتخاب یا اطلاعات یک بیمار جدید را وارد کنید.</p>
              <div class="count"><span class="dot"></span>پذیرش سریع</div>
            </div>
          </div>
        </div>
      </div>
    `;
  }

  function bookedListHtml(doc) {
    const t = C().today();
    const all = C().QUEUE.filter((q) =>
      q.doc === doc.id && q.date === t && q.src === 'appointment' && q.state !== 'done'
    ).sort((a, b) => a.at - b.at);

    const q = S.search.trim().toLowerCase();
    const filtered = all.filter((it) => {
      if (S.apptFilter !== 'all' && it.type !== S.apptFilter) return false;
      if (!q) return true;
      const hay = [it.pet.name, it.pet.owner, it.pet.kind, it.pet.phone].join(' ').toLowerCase();
      return hay.indexOf(q) !== -1;
    });

    // Build chip counts
    const typeCounts = {};
    all.forEach((it) => { typeCounts[it.type] = (typeCounts[it.type] || 0) + 1; });

    const chipsHtml = `
      <div class="chips">
        <div class="chip ${S.apptFilter === 'all' ? 'is-active' : ''}" data-filter="all">
          <span>همه</span><span class="c">${toFa(all.length)}</span>
        </div>
        ${TYPES.filter((t) => typeCounts[t.id]).map((t) => `
          <div class="chip ${S.apptFilter === t.id ? 'is-active' : ''}" data-filter="${t.id}">
            <span>${escHtml(t.label)}</span><span class="c">${toFa(typeCounts[t.id])}</span>
          </div>
        `).join('')}
      </div>
    `;

    let listHtml;
    if (filtered.length === 0) {
      const isEmpty = all.length === 0;
      listHtml = `
        <div class="list-empty">
          <div class="em-ic">${window.Icons.calendar}</div>
          <h4>${isEmpty ? 'هیچ نوبت رزرو شده‌ای برای این پزشک در امروز پیدا نشد' : 'با این فیلتر نتیجه‌ای یافت نشد'}</h4>
          <p>${isEmpty ? 'می‌توانید بدون نوبت هم ویزیت جدید ثبت کنید.' : 'فیلترها را تغییر دهید یا از حالت ویزیت جدید استفاده کنید.'}</p>
          <button class="switch-cta" data-switch="new">
            ${window.Icons.plus}
            <span>شروع ویزیت جدید بدون نوبت</span>
          </button>
        </div>
      `;
    } else {
      listHtml = `
        <div class="appt-list">
          ${filtered.map((it) => apptRowHtml(it)).join('')}
        </div>
      `;
    }

    return `
      <div class="body-head">
        <div class="t">نوبت‌های امروز <small>· ${toFa(filtered.length)} از ${toFa(all.length)}</small></div>
        <div class="field-search">
          ${window.Icons.search}
          <input id="apptSearch" type="text" placeholder="جستجو بر اساس نام بیمار، مالک یا شماره…" value="${escHtml(S.search)}"/>
        </div>
      </div>
      ${chipsHtml}
      ${listHtml}
    `;
  }

  function apptRowHtml(it) {
    const type = TYPE(it.type);
    const urgent = it.prio === 'urgent';
    const stateBadge = it.state === 'called' ? '<span class="pill-tag info">فراخوانده شده</span>' :
                       it.state === 'inVisit' ? '<span class="pill-tag warn">در ویزیت</span>' :
                       '<span class="pill-tag ok">آمادهٔ ویزیت</span>';
    return `
      <div class="appt-row ${urgent ? 'urgent' : ''}" data-ticket="${it.id}" role="button" tabindex="0">
        <div class="time">
          <div class="h">${fmtTime(it.at)}</div>
          <div class="dur">${fmtDur(it.du)}</div>
        </div>
        <div class="avpet ${petIcClass(it.pet.ic)}">${petIcSvg(it.pet.ic)}</div>
        <div class="info">
          <div class="top">
            <span class="name">${escHtml(it.pet.name)}</span>
            <span class="kind">· ${escHtml(it.pet.kind)}</span>
            <span class="type-mark"><span class="swatch" style="background:${type.ac}"></span>${escHtml(type.label)}</span>
            ${urgent ? '<span class="pill-tag danger">اورژانس</span>' : ''}
            ${stateBadge}
          </div>
          <div class="owner">
            ${window.Icons.users || ''}
            <span>${escHtml(it.pet.owner)}</span>
            <span class="phone">${escHtml(it.pet.phone)}</span>
          </div>
        </div>
        <button class="cta">
          <span>شروع ویزیت</span>
          ${window.Icons.chevronLeft}
        </button>
      </div>
    `;
  }

  // ------- new-visit form -------
  function newVisitFormHtml(/*doc*/) {
    return `
      <div class="newform">
        ${patientBlockHtml()}
        ${typeBlockHtml()}
        ${priorityBlockHtml()}
        ${noteBlockHtml()}
        <button class="go-btn" id="startNewBtn" ${canStartNew() ? '' : 'disabled'}>
          ${window.Icons.play || window.Icons.chevronLeft}
          <span>شروع ویزیت جدید</span>
        </button>
      </div>
    `;
  }

  function canStartNew() { return !!S.nb.patient; }

  function patientBlockHtml() {
    if (S.nb.patient) return patientPickedHtml();
    return `
      <div class="form-card">
        <div class="head-row">
          <div class="lbl"><div class="step-num">۱</div><span>انتخاب بیمار</span></div>
          <span class="hint">می‌توانید بیمار موجود یا جدید ثبت کنید</span>
        </div>
        <div class="patient-search-wrap">
          <div class="field-search">
            ${window.Icons.search}
            <input id="patSearch" type="text" placeholder="جستجوی بیمار: نام حیوان، مالک یا شماره تماس…"/>
          </div>
          <div class="patient-dropdown" id="patDrop"></div>
        </div>
        <button class="newpatient-btn" id="newPatBtn">
          ${window.Icons.plus}
          <span>ثبت بیمار جدید (بدون سابقه در سیستم)</span>
        </button>
        <div id="newPatForm" style="display:none; margin-top:10px;">
          <div class="field-grid" style="margin-top:2px;">
            <div class="fld">
              <label>نام حیوان</label>
              <input id="npName" type="text" placeholder="مثلاً: میلو"/>
            </div>
            <div class="fld">
              <label>گونه / نژاد</label>
              <input id="npKind" type="text" placeholder="مثلاً: سگ پامرانین"/>
            </div>
            <div class="fld">
              <label>نام مالک</label>
              <input id="npOwner" type="text" placeholder="مثلاً: رضا کریمی"/>
            </div>
            <div class="fld">
              <label>شماره تماس</label>
              <input id="npPhone" type="text" placeholder="۰۹۱۲…" dir="ltr" style="text-align:left"/>
            </div>
            <div class="fld">
              <label>نوع حیوان (برای آیکن)</label>
              <select id="npIc">
                <option value="dog">سگ</option>
                <option value="cat">گربه</option>
                <option value="bird">پرنده</option>
                <option value="rabbit">خرگوش</option>
              </select>
            </div>
            <div class="fld" style="align-self:end;">
              <button class="go-btn" id="saveNpBtn" style="padding:10px 14px; font-size:13px;">
                ${window.Icons.check}
                <span>ثبت بیمار</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    `;
  }

  function patientPickedHtml() {
    const p = S.nb.patient;
    return `
      <div class="form-card">
        <div class="head-row">
          <div class="lbl"><div class="step-num">۱</div><span>بیمار انتخاب شد</span></div>
          <span class="hint">${p.source === 'new' ? 'بیمار جدید — بدون سابقه' : 'از پایگاه بیماران'}</span>
        </div>
        <div class="patient-picked">
          <div class="av ${petIcClass(p.ic)}">${petIcSvg(p.ic)}</div>
          <div class="m">
            <div class="name">${escHtml(p.name)} <span style="color:var(--muted); font-size:12px; font-weight:700;">· ${escHtml(p.kind)}</span></div>
            <div class="sub">مالک: <b>${escHtml(p.owner || '—')}</b><span class="phone">${escHtml(p.phone || '')}</span></div>
          </div>
          <button class="change" id="changePatBtn">تغییر</button>
        </div>
      </div>
    `;
  }

  function typeBlockHtml() {
    return `
      <div class="form-card">
        <div class="head-row">
          <div class="lbl"><div class="step-num">۲</div><span>نوع ویزیت</span></div>
          <span class="hint">با انتخاب نوع، مدت پیش‌فرض تعیین می‌شود</span>
        </div>
        <div class="type-grid" id="typeGrid">
          ${TYPES.map((t) => `
            <div class="type-opt ${S.nb.type === t.id ? 'is-active' : ''}" data-type="${t.id}" role="button" tabindex="0">
              <div class="ic" style="${S.nb.type === t.id ? '' : `background: ${t.ac}22; color: ${t.ac};`}">${window.Icons[t.icon] || window.Icons.briefcase}</div>
              <div class="tl">
                <div class="n">${escHtml(t.label)}</div>
                <div class="d">پیش‌فرض ${toFa(t.dur)} دقیقه</div>
              </div>
            </div>
          `).join('')}
        </div>
      </div>
    `;
  }

  function priorityBlockHtml() {
    return `
      <div class="form-card">
        <div class="head-row">
          <div class="lbl"><div class="step-num">۳</div><span>اولویت</span></div>
          <span class="hint">اورژانس در صف با علامت هشدار نمایش داده می‌شود</span>
        </div>
        <div class="prio-row" id="prioRow">
          <div class="prio-opt normal ${S.nb.priority === 'normal' ? 'is-active' : ''}" data-prio="normal" role="button" tabindex="0">
            <span class="dot"></span>
            <div class="txt"><div class="n">عادی</div><div class="d">در نوبت طبیعی قرار می‌گیرد</div></div>
          </div>
          <div class="prio-opt urgent ${S.nb.priority === 'urgent' ? 'is-active' : ''}" data-prio="urgent" role="button" tabindex="0">
            <span class="dot"></span>
            <div class="txt"><div class="n">اورژانس</div><div class="d">در ابتدای صف قرار می‌گیرد</div></div>
          </div>
        </div>
      </div>
    `;
  }

  function noteBlockHtml() {
    return `
      <div class="form-card">
        <div class="head-row">
          <div class="lbl"><div class="step-num">۴</div><span>یادداشت پذیرش (اختیاری)</span></div>
          <span class="hint">مثلاً علت مراجعه یا نکاتی که دکتر باید بداند</span>
        </div>
        <div class="fld">
          <textarea id="nbNote" placeholder="مثلاً: بی‌اشتهایی از دیروز، تب خفیف در لمس، سابقهٔ حساسیت به آموکسی‌سیلین…">${escHtml(S.nb.note)}</textarea>
        </div>
      </div>
    `;
  }

  // ------- workspace event wiring -------
  function wireWorkspace(doc) {
    // Tab switcher
    $$('#workspace .mode-tab').forEach((el) => {
      el.addEventListener('click', () => setMode(el.dataset.mode));
      el.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setMode(el.dataset.mode); }
      });
    });

    if (S.mode === 'appt') {
      // Chips
      $$('#workspace .chips .chip').forEach((el) => {
        el.addEventListener('click', () => { S.apptFilter = el.dataset.filter; renderWorkspace(); });
      });
      // Search
      const s = $('#apptSearch');
      if (s) {
        s.addEventListener('input', (e) => { S.search = e.target.value; renderWorkspace(); requestAnimationFrame(() => $('#apptSearch') && $('#apptSearch').focus()); });
        // Restore caret focus after re-render
      }
      // Rows
      $$('#workspace .appt-row').forEach((el) => {
        el.addEventListener('click', (e) => {
          // ignore accidental button-only clicks — whole row triggers start anyway
          startVisitFromTicket(el.dataset.ticket);
        });
        el.addEventListener('keydown', (e) => {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); startVisitFromTicket(el.dataset.ticket); }
        });
      });
      // Switch CTA in empty state
      $$('#workspace [data-switch]').forEach((el) => {
        el.addEventListener('click', () => setMode(el.dataset.switch));
      });
    } else {
      wireNewForm(doc);
    }
  }

  function setMode(m) {
    if (S.mode === m) return;
    S.mode = m;
    S.search = '';
    S.apptFilter = 'all';
    renderWorkspace();
  }

  function wireNewForm(doc) {
    // Patient search (fuzzy over CLINIC.QUEUE pets — proxy for a patient list in this mockup)
    const s = $('#patSearch');
    const drop = $('#patDrop');

    const petsFromQueue = () => {
      // dedupe by name+owner
      const seen = new Set();
      const list = [];
      C().QUEUE.forEach((q) => {
        const key = q.pet.name + '|' + q.pet.owner;
        if (seen.has(key)) return;
        seen.add(key);
        list.push({ ...q.pet });
      });
      return list;
    };
    const patients = petsFromQueue();

    const rerenderDrop = () => {
      const q = (s.value || '').trim().toLowerCase();
      if (!q) { drop.classList.remove('open'); drop.innerHTML = ''; return; }
      const hits = patients.filter((p) =>
        (p.name + ' ' + p.owner + ' ' + p.kind + ' ' + p.phone).toLowerCase().includes(q)
      ).slice(0, 6);
      drop.innerHTML = hits.length
        ? hits.map((p) => `
            <div class="patient-opt" data-p='${escHtml(JSON.stringify(p))}'>
              <div class="av">${petIcSvg(p.ic)}</div>
              <div>
                <div class="name">${escHtml(p.name)} <span style="color:var(--muted); font-size:11px;">· ${escHtml(p.kind)}</span></div>
                <div class="meta">مالک: ${escHtml(p.owner)}</div>
              </div>
              <div class="phone">${escHtml(p.phone)}</div>
            </div>
          `).join('')
        : `<div style="padding: 14px; text-align:center; color: var(--muted); font-size: 12.5px; font-weight: 700;">بیماری یافت نشد — می‌توانید بیمار جدید ثبت کنید</div>`;
      drop.classList.add('open');
    };

    if (s) {
      s.addEventListener('input', rerenderDrop);
      s.addEventListener('focus', rerenderDrop);
      // click outside close
      document.addEventListener('click', function docClose(e) {
        if (!drop || !s) return;
        if (drop.contains(e.target) || s.contains(e.target)) return;
        drop.classList.remove('open');
      }, { once: false });
    }
    if (drop) {
      drop.addEventListener('click', (e) => {
        const opt = e.target.closest('.patient-opt');
        if (!opt) return;
        const p = JSON.parse(opt.dataset.p);
        S.nb.patient = { source: 'existing', ...p };
        renderWorkspace();
      });
    }

    // "New patient" toggle & save
    const newBtn = $('#newPatBtn');
    const newForm = $('#newPatForm');
    if (newBtn) newBtn.addEventListener('click', () => { newForm.style.display = newForm.style.display === 'none' ? 'block' : 'none'; });
    const saveBtn = $('#saveNpBtn');
    if (saveBtn) saveBtn.addEventListener('click', () => {
      const name = $('#npName').value.trim();
      const kind = $('#npKind').value.trim();
      const owner = $('#npOwner').value.trim();
      const phone = $('#npPhone').value.trim();
      const ic = $('#npIc').value;
      if (!name || !kind) { window.toast('نام و نژاد حیوان الزامی است.', 'x'); return; }
      S.nb.patient = { source: 'new', name, kind, owner, phone, ic };
      renderWorkspace();
    });

    // Change patient
    const cp = $('#changePatBtn');
    if (cp) cp.addEventListener('click', () => { S.nb.patient = null; renderWorkspace(); });

    // Type grid
    $$('#typeGrid .type-opt').forEach((el) => {
      el.addEventListener('click', () => { S.nb.type = el.dataset.type; renderWorkspace(); });
    });
    // Priority
    $$('#prioRow .prio-opt').forEach((el) => {
      el.addEventListener('click', () => { S.nb.priority = el.dataset.prio; renderWorkspace(); });
    });
    // Note
    const nt = $('#nbNote');
    if (nt) nt.addEventListener('input', (e) => { S.nb.note = e.target.value; });

    // Start
    const go = $('#startNewBtn');
    if (go) go.addEventListener('click', startVisitFromNew);
  }

  /* ===========================================================
     Ticket → wizard transitions
     =========================================================== */

  function startVisitFromTicket(tid) {
    const t = C().ticket(tid);
    if (!t) { window.toast('نوبت پیدا نشد.', 'x'); return; }
    S.ticket = t;
    S.enc = buildEncounter(t);
    S.step = 0;
    S.scene = 'wizard';
    renderScene();
    window.toast('ویزیت " ' + t.pet.name + ' " آغاز شد.', 'check');
  }

  function startVisitFromNew() {
    if (!S.docId || !S.nb.patient) { window.toast('اطلاعات ناقص است.', 'x'); return; }
    const p = S.nb.patient;
    const type = TYPE(S.nb.type);
    const t = C().today();
    const now = new Date();
    const at = now.getHours() * 60 + now.getMinutes();
    // Synthetic ticket (in-memory, not pushed to QUEUE seed — this is a UI mockup)
    S.ticket = {
      id: 'qt-new-' + Date.now(),
      apptId: null, src: 'walkin', date: t, at, du: type.dur,
      pet: { name: p.name, kind: p.kind, owner: p.owner || '—', phone: p.phone || '—', av: 'a', ic: petIcClass(p.ic) },
      doc: S.docId, type: type.id, prio: S.nb.priority, state: 'inVisit',
      admittedBy: 'پذیرش (بدون نوبت)',
      times: { admitted: t, called: t, entered: t, finished: null },
      _synthetic: true, _note: S.nb.note,
    };
    S.enc = buildEncounter(S.ticket);
    S.step = 0;
    S.scene = 'wizard';
    renderScene();
    window.toast('ویزیت جدید بدون نوبت برای " ' + p.name + ' " آغاز شد.', 'check');
  }

  function buildEncounter(ticket) {
    // If we have a matching draft encounter, reuse it; otherwise build a fresh one
    const existing = C().ENCOUNTERS.find((e) => e.ticketId === ticket.id && e.state === 'draft');
    if (existing) return JSON.parse(JSON.stringify(existing));
    return {
      id: (C().nextId ? C().nextId('enc') : 'enc-' + Date.now()),
      ticketId: ticket.id, petId: null, docId: ticket.doc, date: ticket.date,
      vitals: { w: 0, t: 0, p: 0, r: 0 },
      history: ticket._note || '', exam: '', diagnosis: '', plan: '',
      services: [], items: [], rx: [], tests: [],
      followUp: null, state: 'draft', completedAt: null,
    };
  }

  /* ===========================================================
     SCENE B — WIZARD
     =========================================================== */

  function renderWizard() {
    renderRibbon();
    renderStepper();
    renderBody();
    renderFoot();
    renderPatientSidebar();
  }

  function renderRibbon() {
    const doc = C().DOCTORS.find((d) => d.id === S.docId);
    const t = S.ticket;
    const type = TYPE(t.type);
    $('#wzRibbon').innerHTML = `
      <div class="left">
        <button class="back-btn" id="backToGateBtn">
          ${window.Icons.chevronRight}
          <span>بازگشت به انتخاب</span>
        </button>
        <h2>ویزیت ${escHtml(t.pet.name)} · ${escHtml(type.label)}</h2>
        ${t._synthetic ? '<span class="badge-lg"><span class="dot" style="background:var(--warn)"></span>بدون نوبت</span>' : '<span class="badge-lg"><span class="dot"></span>نوبت رزرو شده</span>'}
      </div>
      <div class="left">
        <span class="badge-lg"><span class="dot" style="background:${doc.ac}"></span>${escHtml(doc.short)}</span>
      </div>
    `;
    $('#backToGateBtn').addEventListener('click', () => {
      if (confirm('اگر برگردید، تغییرات این ویزیت (نمونه طراحی) از دست می‌رود. ادامه می‌دهید؟')) {
        S.scene = 'gate';
        S.ticket = null; S.enc = null; S.step = 0;
        renderScene();
      }
    });
  }

  function renderStepper() {
    $('#wzStepper').innerHTML = STEPS.map((s, i) => `
      <div class="wz-step ${i === S.step ? 'is-active' : ''} ${i < S.step ? 'is-done' : ''}" data-step="${i}">
        <div class="n">${i < S.step ? window.Icons.check : toFa(i + 1)}</div>
        <div class="l">${escHtml(s.short)}</div>
      </div>
    `).join('');
    $$('#wzStepper .wz-step').forEach((el) => {
      el.addEventListener('click', () => {
        const target = parseInt(el.dataset.step, 10);
        if (target < S.step) { S.step = target; renderScene(); }
      });
    });
  }

  function renderFoot() {
    const isLast = S.step === STEPS.length - 1;
    $('#wzFoot').innerHTML = `
      <div class="step-info">مرحلهٔ ${toFa(S.step + 1)} از ${toFa(STEPS.length)} · ${escHtml(STEPS[S.step].label)}</div>
      <div class="actions">
        <button class="btn ghost" id="wzPrev" ${S.step === 0 ? 'disabled style="opacity:.5;cursor:not-allowed;"' : ''}>${window.Icons.chevronRight}<span>مرحلهٔ قبل</span></button>
        <button class="btn outline" id="wzSave">${window.Icons.download || ''}<span>ذخیره پیش‌نویس</span></button>
        <button class="btn primary" id="wzNext">
          <span>${isLast ? 'صدور فاکتور و پایان ویزیت' : 'مرحلهٔ بعد'}</span>
          ${isLast ? (window.Icons.check || '') : window.Icons.chevronLeft}
        </button>
      </div>
    `;
    $('#wzPrev').addEventListener('click', () => { if (S.step > 0) { S.step--; renderScene(); } });
    $('#wzSave').addEventListener('click', () => window.toast('پیش‌نویس ذخیره شد (نمونه طراحی).', 'check'));
    $('#wzNext').addEventListener('click', () => {
      if (S.step < STEPS.length - 1) { S.step++; renderScene(); return; }
      // Finish
      S.scene = 'done';
      renderScene();
    });
  }

  function renderPatientSidebar() {
    const p = S.ticket.pet;
    const doc = C().DOCTORS.find((d) => d.id === S.docId);
    const type = TYPE(S.ticket.type);
    $('#wzPatient').innerHTML = `
      <div class="av-lg ${petIcClass(p.ic)}">${petIcSvg(p.ic)}</div>
      <div class="pt-name">${escHtml(p.name)}</div>
      <div class="pt-kind">${escHtml(p.kind)}</div>
      <div class="pt-tags">
        <span class="pill-tag" style="background:${type.ac}22; color:${type.ac};">${escHtml(type.label)}</span>
        ${S.ticket.prio === 'urgent' ? '<span class="pill-tag danger">اورژانس</span>' : ''}
      </div>
      <div class="meta-row"><span class="lbl">مالک</span><span class="val">${escHtml(p.owner)}</span></div>
      <div class="meta-row"><span class="lbl">تماس</span><span class="val" dir="ltr">${escHtml(p.phone)}</span></div>
      <div class="meta-row"><span class="lbl">نوع مراجعه</span><span class="val">${S.ticket._synthetic ? 'بدون نوبت' : 'نوبت رزرو شده'}</span></div>
      <div class="meta-row"><span class="lbl">مدت زمان</span><span class="val">${fmtDur(S.ticket.du)}</span></div>
      <div class="time-badge">
        ${window.Icons.clock || ''}
        <span>زمان: ${fmtTime(S.ticket.at)}</span>
      </div>
      <div class="doc-badge">
        <div class="av" style="background:${doc.ac}">${escHtml(doc.ini)}</div>
        <div class="doc-txt">
          <div class="n">${escHtml(doc.name)}</div>
          <div class="s">${escHtml(doc.spec)}</div>
        </div>
      </div>
    `;
  }

  function renderBody() {
    const step = STEPS[S.step].key;
    if (step === 'vitals') return renderVitals();
    if (step === 'history') return renderHistory();
    if (step === 'plan') return renderPlan();
    if (step === 'rx') return renderRx();
    if (step === 'review') return renderReview();
  }

  // --- Step 1: Vitals ---
  function renderVitals() {
    const v = S.enc.vitals;
    $('#wzBody').innerHTML = `
      <div class="sec-head">
        <h3>علائم حیاتی</h3>
        <div class="hint">اعداد بر اساس معاینهٔ اولیه ثبت می‌شوند</div>
      </div>
      <div class="vitals">
        <div class="vital">
          <div class="lbl">وزن</div>
          <div class="row"><input id="v-w" type="number" step="0.1" value="${v.w || ''}" placeholder="0.0"/><span class="unit">کیلوگرم</span></div>
        </div>
        <div class="vital">
          <div class="lbl">دمای بدن</div>
          <div class="row"><input id="v-t" type="number" step="0.1" value="${v.t || ''}" placeholder="0.0"/><span class="unit">°C</span></div>
        </div>
        <div class="vital">
          <div class="lbl">ضربان قلب</div>
          <div class="row"><input id="v-p" type="number" value="${v.p || ''}" placeholder="0"/><span class="unit">bpm</span></div>
        </div>
        <div class="vital">
          <div class="lbl">تعداد تنفس</div>
          <div class="row"><input id="v-r" type="number" value="${v.r || ''}" placeholder="0"/><span class="unit">/دقیقه</span></div>
        </div>
      </div>
      <div class="sec-head" style="margin-top: 20px;">
        <h3>یادداشت اولیهٔ معاینه</h3>
        <div class="hint">این نوشته در گام بعد توسط شرح‌حال کامل‌تر می‌شود</div>
      </div>
      <div class="fld">
        <textarea id="v-note" rows="3" placeholder="ظاهر عمومی، سطح انرژی، وضعیت مخاطات، نکات فوری…">${escHtml(S.enc.history || '')}</textarea>
      </div>
    `;
    ['w','t','p','r'].forEach((k) => {
      $('#v-' + k).addEventListener('input', (e) => { S.enc.vitals[k] = parseFloat(e.target.value) || 0; });
    });
    $('#v-note').addEventListener('input', (e) => { S.enc.history = e.target.value; });
  }

  // --- Step 2: History & exam ---
  function renderHistory() {
    $('#wzBody').innerHTML = `
      <div class="sec-head">
        <h3>شرح‌حال و معاینهٔ فیزیکی</h3>
        <div class="hint">هر دو بخش قابل ویرایش‌اند</div>
      </div>
      <div class="col-2">
        <div class="fld">
          <label>شرح حال بیمار</label>
          <textarea id="h-hist" rows="7" placeholder="سابقهٔ بیماری، مشاهدات مالک، طول مدت علائم…">${escHtml(S.enc.history || '')}</textarea>
        </div>
        <div class="fld">
          <label>معاینهٔ فیزیکی</label>
          <textarea id="h-exam" rows="7" placeholder="یافته‌های معاینه: قلبی، ریوی، شکمی، عصبی، پوست…">${escHtml(S.enc.exam || '')}</textarea>
        </div>
      </div>
    `;
    $('#h-hist').addEventListener('input', (e) => { S.enc.history = e.target.value; });
    $('#h-exam').addEventListener('input', (e) => { S.enc.exam = e.target.value; });
  }

  // --- Step 3: Diagnosis + plan ---
  function renderPlan() {
    $('#wzBody').innerHTML = `
      <div class="sec-head">
        <h3>تشخیص و طرح درمان</h3>
        <div class="hint">اطلاعات این بخش در صورت پرینت روی برگهٔ نسخه چاپ می‌شود</div>
      </div>
      <div class="fld" style="margin-bottom: 12px;">
        <label>تشخیص</label>
        <input id="p-dx" type="text" placeholder="مثلاً: عفونت قسمت فوقانی تنفسی" value="${escHtml(S.enc.diagnosis || '')}"/>
      </div>
      <div class="fld">
        <label>طرح درمان</label>
        <textarea id="p-plan" rows="6" placeholder="اقدامات درمانی، توصیه‌ها، پیگیری‌های لازم…">${escHtml(S.enc.plan || '')}</textarea>
      </div>
    `;
    $('#p-dx').addEventListener('input', (e) => { S.enc.diagnosis = e.target.value; });
    $('#p-plan').addEventListener('input', (e) => { S.enc.plan = e.target.value; });
  }

  // --- Step 4: Rx (services + items) — simplified visual ---
  function renderRx() {
    const services = S.enc.services;
    const items = S.enc.items;
    $('#wzBody').innerHTML = `
      <div class="sec-head">
        <h3>خدمات، اقلام و نسخه</h3>
        <div class="hint">اقلام انتخابی به‌طور خودکار وارد فاکتور می‌شوند</div>
      </div>
      <div class="col-2">
        <div>
          <div style="font-weight:800; font-size:13px; color:var(--ink); margin-bottom:8px;">خدمات</div>
          <div class="field-search" style="margin-bottom:8px;">
            ${window.Icons.plus || ''}
            <select id="svcAdd" style="border:0; outline:0; background:transparent; width:100%; font: inherit; color: var(--ink);">
              <option value="">افزودن خدمت…</option>
              ${C().SERVICES.map((sv) => `<option value="${sv.id}">${escHtml(sv.title)} — ${money(sv.price)}</option>`).join('')}
            </select>
          </div>
          <div id="svcList" style="display:flex; flex-direction:column; gap:6px;">
            ${services.length === 0 ? '<div style="color:var(--muted); font-size: 12px; padding: 8px 4px; font-weight: 700;">هیچ خدمتی افزوده نشده</div>' : services.map((s, i) => rxLineHtml('svc', s, i)).join('')}
          </div>
        </div>
        <div>
          <div style="font-weight:800; font-size:13px; color:var(--ink); margin-bottom:8px;">اقلام دارویی / کالا</div>
          <div class="field-search" style="margin-bottom:8px;">
            ${window.Icons.plus || ''}
            <select id="itmAdd" style="border:0; outline:0; background:transparent; width:100%; font: inherit; color: var(--ink);">
              <option value="">افزودن کالا…</option>
              ${C().ITEMS.map((it) => `<option value="${it.id}">${escHtml(it.title)} — ${money(it.price)}</option>`).join('')}
            </select>
          </div>
          <div id="itmList" style="display:flex; flex-direction:column; gap:6px;">
            ${items.length === 0 ? '<div style="color:var(--muted); font-size: 12px; padding: 8px 4px; font-weight: 700;">هیچ کالایی افزوده نشده</div>' : items.map((s, i) => rxLineHtml('item', s, i)).join('')}
          </div>
        </div>
      </div>
      <div style="background: var(--brand-50); border: 1px solid var(--brand-100); border-radius: 14px; padding: 14px 18px; margin-top: 18px; display: flex; justify-content: space-between; align-items: baseline;">
        <div style="font-size: 13px; color: var(--ink-2); font-weight: 800;">جمع اولیهٔ اقلام</div>
        <div style="font-size: 20px; font-weight: 800; color: var(--brand-700); font-variant-numeric: tabular-nums;">${money(computeSubtotal())}</div>
      </div>
    `;

    $('#svcAdd').addEventListener('change', (e) => {
      const id = e.target.value; if (!id) return;
      S.enc.services.push({ sv: id, qty: 1, note: '' });
      renderScene();
    });
    $('#itmAdd').addEventListener('change', (e) => {
      const id = e.target.value; if (!id) return;
      S.enc.items.push({ it: id, qty: 1 });
      renderScene();
    });
    $$('#svcList .rx-line, #itmList .rx-line').forEach((el) => {
      const kind = el.dataset.kind;
      const i = parseInt(el.dataset.idx, 10);
      el.querySelector('.qty').addEventListener('input', (e) => {
        const arr = kind === 'svc' ? S.enc.services : S.enc.items;
        arr[i].qty = Math.max(1, parseInt(e.target.value, 10) || 1);
        updateSubtotalOnly();
      });
      el.querySelector('.rm').addEventListener('click', () => {
        const arr = kind === 'svc' ? S.enc.services : S.enc.items;
        arr.splice(i, 1);
        renderScene();
      });
    });
  }

  function updateSubtotalOnly() {
    // No re-render — subtotal is at the bottom
    const el = $('#wzBody');
    if (!el) return;
    const totalEl = el.querySelector('div[style*="جمع اولیهٔ اقلام"]');
    // easier: just re-render body of step 4
    renderRx();
  }

  function rxLineHtml(kind, entry, i) {
    const catalog = kind === 'svc' ? C().SERVICES : C().ITEMS;
    const key = kind === 'svc' ? 'sv' : 'it';
    const row = catalog.find((x) => x.id === entry[key]);
    if (!row) return '';
    const sum = row.price * (entry.qty || 1);
    return `
      <div class="rx-line" data-kind="${kind}" data-idx="${i}" style="display:grid; grid-template-columns: 26px 1fr 90px 28px; gap:8px; align-items:center; padding:10px 12px; background:#fff; border:1px solid var(--line); border-radius:12px;">
        <div style="width:26px; height:26px; border-radius:8px; display:grid; place-items:center; font-size:11px; font-weight:800; background:${kind === 'svc' ? 'var(--brand-50)' : 'var(--lilac-bg)'}; color:${kind === 'svc' ? 'var(--brand-700)' : '#7b5cc4'};">${kind === 'svc' ? 'خ' : 'ک'}</div>
        <div>
          <div style="font-weight:700; font-size:13px; color:var(--ink);">${escHtml(row.title)}</div>
          <div style="font-size:11px; color:var(--muted); font-variant-numeric:tabular-nums; font-weight:700;">قیمت: ${money(row.price)} · جمع: <b style="color:var(--ink-2);">${money(sum)}</b></div>
        </div>
        <input class="qty" type="number" min="1" value="${toFa(entry.qty || 1)}" style="text-align:center; padding:6px 8px; border:1px solid var(--line); border-radius:8px; font:inherit; font-variant-numeric:tabular-nums;"/>
        <button class="rm" style="border:0; background:transparent; cursor:pointer; color:var(--muted); width:28px; height:28px; border-radius:8px; display:grid; place-items:center;" title="حذف">${window.Icons.x || '×'}</button>
      </div>
    `;
  }

  function computeSubtotal() {
    let total = 0;
    S.enc.services.forEach((s) => {
      const row = C().SERVICES.find((x) => x.id === s.sv);
      if (row) total += row.price * (s.qty || 1);
    });
    S.enc.items.forEach((s) => {
      const row = C().ITEMS.find((x) => x.id === s.it);
      if (row) total += row.price * (s.qty || 1);
    });
    return total;
  }

  // --- Step 5: Review ---
  function renderReview() {
    const sub = computeSubtotal();
    const tax = Math.round(sub * 0.09);
    const total = sub + tax;
    const linesHtml =
      (S.enc.services.length + S.enc.items.length === 0)
        ? `<div style="color:var(--muted); font-size:13px; padding: 12px 0; font-weight: 700;">هنوز خدمتی افزوده نشده — لطفاً به گام قبل برگردید.</div>`
        : `
          ${S.enc.services.map((s) => reviewLineHtml('svc', s)).join('')}
          ${S.enc.items.map((s) => reviewLineHtml('item', s)).join('')}
        `;

    $('#wzBody').innerHTML = `
      <div class="sec-head">
        <h3>پیش‌نمایش فاکتور</h3>
        <div class="hint">پس از تأیید، این ویزیت به‌عنوان انجام‌شده ثبت می‌شود</div>
      </div>
      <div style="background: #fbfefd; border: 1px solid var(--brand-100); border-radius: 14px; padding: 14px 16px;">
        ${linesHtml}
      </div>
      <div style="margin-top: 14px; display:grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <div style="padding: 12px 14px; background:#fff; border:1px solid var(--line); border-radius:12px;">
          <div style="font-size:11px; color:var(--muted); font-weight:800;">تشخیص</div>
          <div style="font-size:13px; color:var(--ink); font-weight:700; margin-top:4px;">${escHtml(S.enc.diagnosis || '—')}</div>
        </div>
        <div style="padding: 12px 14px; background:#fff; border:1px solid var(--line); border-radius:12px;">
          <div style="font-size:11px; color:var(--muted); font-weight:800;">طرح درمان</div>
          <div style="font-size:13px; color:var(--ink); font-weight:700; margin-top:4px;">${escHtml(S.enc.plan || '—')}</div>
        </div>
      </div>
      <div style="background: linear-gradient(160deg, var(--brand-50) 0%, #fff 70%); border: 1px solid var(--brand-100); border-radius: 14px; padding: 14px 18px; margin-top: 18px;">
        <div style="display:flex; justify-content:space-between; padding: 4px 0; color:var(--ink-2); font-size: 13px;"><span>جمع اقلام</span><b style="font-variant-numeric:tabular-nums;">${money(sub)}</b></div>
        <div style="display:flex; justify-content:space-between; padding: 4px 0; color:var(--ink-2); font-size: 13px;"><span>مالیات (۹٪)</span><b style="font-variant-numeric:tabular-nums;">${money(tax)}</b></div>
        <div style="display:flex; justify-content:space-between; padding: 10px 0 4px; border-top: 1px solid var(--brand-100); margin-top: 8px; font-size: 15px; color: var(--ink);">
          <span style="font-weight: 800;">مبلغ نهایی قابل پرداخت</span>
          <b style="color:var(--brand-700); font-size: 22px; font-variant-numeric:tabular-nums;">${money(total)}</b>
        </div>
      </div>
    `;
  }

  function reviewLineHtml(kind, entry) {
    const key = kind === 'svc' ? 'sv' : 'it';
    const catalog = kind === 'svc' ? C().SERVICES : C().ITEMS;
    const row = catalog.find((x) => x.id === entry[key]);
    if (!row) return '';
    const sum = row.price * (entry.qty || 1);
    return `
      <div style="display:grid; grid-template-columns: 1fr auto auto; gap:12px; align-items:center; padding: 8px 0; border-bottom: 1px dashed var(--line-2); font-size: 13px;">
        <div>
          <span style="font-weight:700; color:var(--ink);">${escHtml(row.title)}</span>
          <span style="font-size:11px; color:var(--muted); margin-inline-start:6px; font-weight:700;">${kind === 'svc' ? 'خدمت' : 'کالا'}</span>
        </div>
        <div style="color:var(--muted); font-size: 12px; font-weight:700; font-variant-numeric:tabular-nums;">${toFa(entry.qty || 1)} × ${money(row.price)}</div>
        <div style="color:var(--ink); font-weight:800; font-variant-numeric:tabular-nums;">${money(sum)}</div>
      </div>
    `;
  }

  /* ===========================================================
     SCENE C — Done
     =========================================================== */
  function renderDone() {
    const doc = C().DOCTORS.find((d) => d.id === S.docId);
    const total = computeSubtotal() + Math.round(computeSubtotal() * 0.09);
    $('#successPanel').innerHTML = `
      <div class="ok-ic">${window.Icons.check}</div>
      <h2>ویزیت با موفقیت ثبت شد</h2>
      <p>پروندهٔ بیمار به‌روزرسانی شد و فاکتور به صندوق ارسال شد. <span style="opacity:.7">(نمونه طراحی)</span></p>
      <div class="sum-grid">
        <div class="sum"><div class="l">بیمار</div><div class="v">${escHtml(S.ticket.pet.name)}</div></div>
        <div class="sum"><div class="l">پزشک</div><div class="v">${escHtml(doc.short)}</div></div>
        <div class="sum"><div class="l">مبلغ فاکتور</div><div class="v">${money(total)}</div></div>
      </div>
      <div class="actions">
        <button class="wz-foot btn primary" id="doneNextVisitBtn" style="padding: 12px 20px; border-radius: 12px; background: var(--brand-600); color:#fff; font-weight: 800; font-size: 13px; border:0; cursor:pointer; display:inline-flex; align-items:center; gap:8px;">
          ${window.Icons.plus}
          <span>ویزیت بعدی</span>
        </button>
        <button class="wz-foot btn outline" id="doneToCashierBtn" style="padding: 12px 20px; border-radius: 12px; background:#fff; border:1px solid var(--line); color:var(--ink-2); font-weight: 800; font-size: 13px; cursor:pointer; display:inline-flex; align-items:center; gap:8px;">
          ${window.Icons.card || window.Icons.receipt}
          <span>رفتن به صندوق</span>
        </button>
      </div>
    `;
    $('#doneNextVisitBtn').addEventListener('click', () => {
      // reset back to gate; keep same doctor selected
      S.scene = 'gate';
      S.ticket = null; S.enc = null; S.step = 0;
      S.mode = 'appt';
      S.nb = { patient: null, type: 'visit', priority: 'normal', note: '' };
      renderScene();
    });
    $('#doneToCashierBtn').addEventListener('click', () => {
      window.toast('انتقال به صندوق (نمونه طراحی).', 'check');
      setTimeout(() => { location.href = 'cashier.html'; }, 600);
    });
  }

})();
