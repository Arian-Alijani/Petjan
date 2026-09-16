/* =========================================================
   Vetplus · Sale & Cashier — v2 (focused workflow)
   assets/sale-page.js

   Design goals:
   - Two-panel workspace: primary invoice work surface + activity sidebar
   - Catalog moved into a keyboard-driven drawer (scales to 1000s of items)
   - Clearer separation between "ready visits" and existing invoices
   - Better empty state that funnels the user into the right action
   - Persistent focus on primary work; clearer visual hierarchy
   ========================================================= */
(function () {
  'use strict';

  const $  = (s, r=document) => r.querySelector(s);
  const $$ = (s, r=document) => Array.from(r.querySelectorAll(s));
  const C  = () => window.CLINIC;
  const F  = () => window.FLOW;

  /* ----- toast (idempotent) ----- */
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

  /* ----- utils ----- */
  const toFa   = (v) => (window.toFa ? window.toFa(v) : String(v));
  const money  = (n) => (window.money ? window.money(n) : toFa(String(n)));
  const moneyU = (n) => `${money(n)}<span class="u">تومان</span>`;
  const Icons  = window.Icons || {};

  const parseFaNum = (s) => {
    if (typeof s !== 'string') return Number(s) || 0;
    const t = s.replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d))
               .replace(/[٠-٩]/g, d => '٠١٢٣٤٥٦٧٨٩'.indexOf(d))
               .replace(/[^\d.-]/g, '');
    return parseInt(t) || 0;
  };

  /* ----- catalog icon mapping ----- */
  const CAT_ICON = {
    svc: {
      'sv-1':  'clipboard', 'sv-2':  'syringe', 'sv-3':  'tools',
      'sv-4':  'scissors',  'sv-5':  'bath',    'sv-6':  'flask',
      'sv-7':  'image',     'sv-8':  'image',   'sv-9':  'clipboard',
      'sv-10': 'syringe',   'sv-11': 'home',    'sv-12': 'clipboard',
    },
    item: {
      'it-1': 'bone', 'it-2': 'bone', 'it-3': 'bone', 'it-4': 'bath',
      'it-5': 'shield', 'it-6': 'bone', 'it-7': 'scissors',
      'it-8': 'box', 'it-9': 'flask', 'it-10': 'shield',
    },
  };
  const iconFor = (kind, ref) => (CAT_ICON[kind] && CAT_ICON[kind][ref]) || (kind === 'svc' ? 'clipboard' : 'box');

  /* ----- Grouping for catalog scale ----- */
  // Group services by conceptual category so the drawer stays organized
  const SVC_GROUPS = {
    'sv-1':  { label: 'ویزیت',    key: 'consult' },
    'sv-9':  { label: 'ویزیت',    key: 'consult' },
    'sv-12': { label: 'ویزیت',    key: 'consult' },
    'sv-2':  { label: 'واکسن',     key: 'vaccine' },
    'sv-10': { label: 'تزریق',    key: 'inject'  },
    'sv-3':  { label: 'جراحی',    key: 'surgery' },
    'sv-4':  { label: 'دندان',    key: 'dental'  },
    'sv-5':  { label: 'گرومینگ',  key: 'groom'   },
    'sv-6':  { label: 'آزمایشگاه',  key: 'lab'     },
    'sv-7':  { label: 'تصویربرداری',  key: 'img'     },
    'sv-8':  { label: 'تصویربرداری',  key: 'img'     },
    'sv-11': { label: 'بستری',    key: 'stay'    },
  };
  const ITEM_GROUPS = {
    'it-1': { label: 'دارو',   key: 'drug'   },
    'it-6': { label: 'دارو',   key: 'drug'   },
    'it-9': { label: 'دارو',   key: 'drug'   },
    'it-5': { label: 'مکمل',   key: 'suppl'  },
    'it-2': { label: 'غذا',    key: 'food'   },
    'it-3': { label: 'غذا',    key: 'food'   },
    'it-4': { label: 'بهداشتی', key: 'hygiene'},
    'it-7': { label: 'بهداشتی', key: 'hygiene'},
    'it-8': { label: 'مصرفی',  key: 'consum' },
    'it-10':{ label: 'ضدانگل',  key: 'antip'  },
  };
  const groupOf = (kind, id) => (kind === 'svc' ? (SVC_GROUPS[id] || { label: 'خدمات', key: 'misc' })
                                                : (ITEM_GROUPS[id] || { label: 'کالا', key: 'misc' }));

  /* ----- doctor lookup ----- */
  const doctorOf = (id) => (C().DOCTORS || []).find(d => d.id === id) || null;

  /* ----- state ----- */
  const state = {
    seg: 'ready',           // 'ready' | 'invoices'  (segmented control)
    query: '',
    activeId: null,
    activeEncId: null,
    payment: { method: 'cash', amount: 0 },
    pickerOpen: false,
    pickerQuery: '',
    // Catalog drawer
    catOpen: false,
    catQuery: '',
    catFilter: 'all',      // 'all' | 'svc' | 'item' | group-key
    catHl: 0,               // highlighted index for keyboard nav
    catList: [],           // last rendered visible list (for keyboard)
    recent: JSON.parse(localStorage.getItem('sw_recent_cat') || '[]'), // {kind, ref}
  };

  const persistRecent = () => localStorage.setItem('sw_recent_cat', JSON.stringify(state.recent.slice(0, 8)));

  const pushRecent = (kind, ref) => {
    state.recent = [{kind, ref}, ...state.recent.filter(r => !(r.kind === kind && r.ref === ref))].slice(0, 8);
    persistRecent();
  };

  /* ======================================================
     ACTIVITY SIDEBAR — segmented control + list
     ====================================================== */
  function readyEncounters() {
    const invEncIds = new Set(C().INVOICES.map(i => i.encId).filter(Boolean));
    return (C().ENCOUNTERS || []).filter(e => e.state === 'done' && !invEncIds.has(e.id));
  }

  function invoicesAll() {
    return C().INVOICES.slice();
  }

  function matchQuery(rec) {
    const q = state.query.trim().toLowerCase();
    if (!q) return true;
    const hay = [rec.id, rec.customer && rec.customer.owner, rec.customer && rec.customer.pet, rec.customer && rec.customer.phone].filter(Boolean).join(' ').toLowerCase();
    return hay.indexOf(q) >= 0;
  }
  function matchEncQuery(enc) {
    const q = state.query.trim().toLowerCase();
    if (!q) return true;
    const tk = C().ticket(enc.ticketId);
    const pet = tk ? tk.pet : {};
    const hay = [enc.id, pet.owner, pet.name, pet.phone].filter(Boolean).join(' ').toLowerCase();
    return hay.indexOf(q) >= 0;
  }

  function renderSeg() {
    const inv = C().INVOICES;
    const readyCt = readyEncounters().length;
    const openCt = inv.filter(i => i.state === 'draft' || i.state === 'partial').length;

    const seg = [
      { k: 'ready', label: 'صف ویزیت', ct: readyCt, pulse: readyCt > 0 },
      { k: 'invoices', label: 'فاکتورها', ct: inv.length, hint: openCt },
    ];

    $('#sw-seg').innerHTML = seg.map(s => `
      <button class="sw-seg-btn ${state.seg === s.k ? 'on' : ''} ${s.pulse ? 'pulse' : ''}" data-seg="${s.k}">
        ${s.label}
        <span class="ct">${toFa(s.ct)}</span>
      </button>
    `).join('');

    $$('#sw-seg .sw-seg-btn').forEach(b => {
      b.addEventListener('click', () => {
        state.seg = b.dataset.seg;
        state.query = '';
        const inp = $('#sw-search'); if (inp) inp.value = '';
        renderSidebar();
      });
    });
  }

  function renderItemInv(inv) {
    const c = inv.customer || {};
    const isDone = inv.state === 'settled';
    const isVoid = inv.state === 'void' || inv.state === 'refunded';
    const isEnc = !!inv.encId;
    const avKey = c.pet ? ['a','b','c','d','e','f'][c.pet.charCodeAt(0) % 6] : 'ph';
    const pillIco = isEnc ? (Icons.clipboard || '') : (Icons.cart || '');
    return `
      <div class="sw-item ${state.activeId === inv.id ? 'on' : ''}" data-inv="${inv.id}">
        <div class="sw-item-row1">
          <span class="sw-item-id">${inv.id}</span>
          <span class="sw-item-pill ${isEnc ? 'visit' : 'sale'}">${pillIco} ${isEnc ? 'از ویزیت' : 'فروش'}</span>
          <span class="sw-item-dot ${inv.state}" title="${inv.state}"></span>
        </div>
        <div class="sw-item-who">
          <div class="avatar-pet ${avKey}">${(c.owner || '?').slice(0,1)}</div>
          <div style="flex:1;min-width:0">
            <div class="name">${c.owner || 'مهمان'}</div>
            <div class="sub">${c.pet ? c.pet : 'بدون حیوان'}${c.phone ? ' · ' + toFa(c.phone) : ''}</div>
          </div>
        </div>
        <div class="sw-item-foot">
          <div class="sw-item-total">${moneyU(inv.total)}</div>
          ${isVoid
            ? `<div class="sw-item-tag void">${inv.state === 'void' ? 'ابطال' : 'مسترد'}</div>`
            : isDone
              ? `<div class="sw-item-tag ok">تسویه</div>`
              : `<div class="sw-item-tag due">مانده ${money(inv.due)}</div>`}
        </div>
      </div>
    `;
  }

  function renderItemEnc(enc) {
    const tk = C().ticket(enc.ticketId);
    const pet = tk ? tk.pet : { owner: '—', name: '—', av: 'ph' };
    const doc = doctorOf(enc.docId);
    const lineCt = (enc.services || []).length + (enc.items || []).length;
    let prev = 0;
    (enc.services || []).forEach(s => {
      const svc = C().SERVICES.find(x => x.id === s.sv);
      if (svc) prev += svc.price * (s.qty || 1);
    });
    (enc.items || []).forEach(it => {
      const cat = C().ITEMS.find(x => x.id === it.it);
      if (cat) prev += cat.price * (it.qty || 1);
    });
    return `
      <div class="sw-item ready" data-enc="${enc.id}">
        <div class="sw-item-row1">
          <span class="sw-item-id">${enc.id}</span>
          <span class="sw-item-pill ready">${Icons.clipboard || ''} آماده فاکتور</span>
        </div>
        <div class="sw-item-who">
          <div class="avatar-pet ${pet.av || 'ph'}">${(pet.owner || '?').slice(0,1)}</div>
          <div style="flex:1;min-width:0">
            <div class="name">${pet.owner || 'مالک'}</div>
            <div class="sub">${pet.name || ''}${pet.kind ? ' · ' + pet.kind : ''}${doc ? ' · ' + doc.short : ''}</div>
          </div>
        </div>
        <div class="sw-item-cta">
          <span>${toFa(lineCt)} آیتم · ${money(prev)} تومان</span>
          <span>صدور فاکتور ${Icons.arrowUpRight || '›'}</span>
        </div>
      </div>
    `;
  }

  function renderSidebar() {
    renderSeg();
    let html = '';

    if (state.seg === 'ready') {
      const list = readyEncounters().filter(matchEncQuery);
      if (list.length === 0) {
        html = `<div class="sw-side-empty">
          <div class="hero">${Icons.check || ''}</div>
          <div>
            <b style="color:var(--ink-2);display:block;margin-bottom:4px">همه ویزیت‌ها فاکتور شده‌اند</b>
            ${state.query ? 'موردی با این جستجو نیست' : 'ویزیت‌های تکمیل‌شده اینجا ظاهر می‌شوند'}
          </div>
        </div>`;
      } else {
        html = list.map(renderItemEnc).join('');
      }
    } else {
      // invoices: group by state
      const all = invoicesAll().filter(matchQuery);
      const openList    = all.filter(i => i.state === 'draft' || i.state === 'partial');
      const settledList = all.filter(i => i.state === 'settled');
      const otherList   = all.filter(i => i.state === 'void' || i.state === 'refunded');

      if (all.length === 0) {
        html = `<div class="sw-side-empty">
          <div class="hero">${Icons.receipt || ''}</div>
          <div>فاکتوری یافت نشد</div>
        </div>`;
      } else {
        const group = (label, list) => list.length === 0 ? '' :
          `<div class="sw-group-head"><span>${label}</span><span class="rule"></span><span>${toFa(list.length)}</span></div>` +
          list.map(renderItemInv).join('');
        html = group('باز و در انتظار', openList) + group('تسویه‌شده', settledList) + group('ابطال و عودت', otherList);
      }
    }

    $('#sw-qlist').innerHTML = html;

    // bind card clicks
    $$('#sw-qlist [data-inv]').forEach(card => {
      card.addEventListener('click', () => selectInvoice(card.dataset.inv));
    });
    $$('#sw-qlist [data-enc]').forEach(card => {
      card.addEventListener('click', () => buildFromEncounter(card.dataset.enc));
    });
  }

  /* ======================================================
     ACTIONS on activity
     ====================================================== */
  function selectInvoice(id) {
    state.activeId = id;
    state.activeEncId = null;
    const inv = C().invoice(id);
    if (inv) state.payment.amount = Math.max(0, inv.due || 0);
    // switch to invoices tab so the user sees selection context
    if (state.seg !== 'invoices') state.seg = 'invoices';
    renderSidebar();
    renderInvoice();
  }

  function buildFromEncounter(encId) {
    const enc = C().encounter(encId);
    if (!enc) return;
    // Preview totals before creating the invoice — reduces mis-clicks and gives
    // the cashier a chance to jump straight into the visit record if needed.
    const tk = C().ticket(enc.ticketId);
    const pet = tk ? tk.pet : { owner: '—', name: '—' };
    const doc = doctorOf(enc.docId);
    let est = 0;
    const svcLines = (enc.services || []).map(s => {
      const svc = C().SERVICES.find(x => x.id === s.sv);
      if (!svc) return null;
      const sum = svc.price * (s.qty || 1);
      est += sum;
      return { title: svc.title, qty: s.qty || 1, price: svc.price, kind: 'svc' };
    }).filter(Boolean);
    const itemLines = (enc.items || []).map(it => {
      const cat = C().ITEMS.find(x => x.id === it.it);
      if (!cat) return null;
      const sum = cat.price * (it.qty || 1);
      est += sum;
      return { title: cat.title, qty: it.qty || 1, price: cat.price, kind: 'item' };
    }).filter(Boolean);

    const backdrop = document.createElement('div');
    backdrop.className = 'sw-modal-backdrop sw-confirm-backdrop';
    backdrop.innerHTML = `
      <div class="sw-modal sw-confirm sw-build-modal" role="dialog" aria-modal="true">
        <div class="sw-confirm-head">
          <div class="sw-confirm-ico brand">${Icons.receipt || ''}</div>
          <div>
            <h3>صدور فاکتور از ویزیت</h3>
            <div class="sub">مالک: <b>${pet.owner || '—'}</b> · حیوان: <b>${pet.name || '—'}</b>${doc ? ' · پزشک: <b>' + doc.short + '</b>' : ''}</div>
          </div>
        </div>
        <div class="sw-confirm-body">
          <div class="sw-build-lines">
            ${(svcLines.concat(itemLines)).map(l => `
              <div class="sw-build-line">
                <span class="k ${l.kind}">${l.kind === 'svc' ? 'خدمت' : 'کالا'}</span>
                <span class="t">${l.title}</span>
                <span class="q">${toFa(l.qty)} × ${money(l.price)}</span>
                <span class="s">${money(l.price * l.qty)}</span>
              </div>
            `).join('')}
          </div>
          <div class="sw-build-total">
            <span>جمع اولیهٔ آیتم‌ها (قبل از تخفیف/مالیات/بیمه)</span>
            <b>${money(est)} <span class="u">تومان</span></b>
          </div>
          <div class="sw-confirm-note">
            پس از صدور، می‌توانید آیتم‌ها، تخفیف و بیمه را تنظیم کرده و پرداخت را ثبت کنید.
          </div>
        </div>
        <div class="sw-confirm-foot">
          <button class="btn btn-outline" data-act="close">انصراف</button>
          <a class="btn btn-outline" href="visits.html#enc-${enc.id}" data-act="view">${Icons.clipboard || ''} مشاهدهٔ پرونده</a>
          <button class="btn btn-primary" data-act="issue">${Icons.check || '✓'} صدور فاکتور</button>
        </div>
      </div>
    `;
    document.body.appendChild(backdrop);
    const close = () => backdrop.remove();
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });
    backdrop.querySelector('[data-act="close"]').addEventListener('click', close);
    backdrop.querySelector('[data-act="issue"]').addEventListener('click', () => {
      const res = F().buildInvoice(enc);
      if (!res.ok) { window.toast(res.error || 'خطا در ساخت فاکتور', 'x'); return; }
      C().INVOICES.unshift(res.invoice);
      close();
      window.toast(`فاکتور «${res.invoice.id}» از ویزیت صادر شد — آمادهٔ دریافت پرداخت`);
      state.seg = 'invoices';
      selectInvoice(res.invoice.id);
    });
    const onKey = (e) => { if (e.key === 'Escape') { close(); document.removeEventListener('keydown', onKey); }};
    document.addEventListener('keydown', onKey);
  }

  function newBlankInvoice(preset) {
    const inv = {
      id: C().nextId('INV'),
      encId: null, src: 'sale', date: C().today(),
      customer: {
        owner: (preset && preset.owner) || '',
        pet:   (preset && preset.pet)   || '',
        phone: (preset && preset.phone) || '',
      },
      lines: [], discount: { kind: 'pct', value: 0 }, insurance: null, taxPct: 9,
      base: 0, discountAmt: 0, afterDiscount: 0, tax: 0, insShare: 0,
      total: 0, paid: 0, due: 0, state: 'draft', payments: [],
    };
    C().INVOICES.unshift(inv);
    state.seg = 'invoices';
    selectInvoice(inv.id);
    window.toast(`فاکتور «${inv.id}» ایجاد شد — آیتم اضافه کنید`);
  }

  /* ======================================================
     INVOICE WORK SURFACE
     ====================================================== */
  function renderInvoice() {
    const host = $('#sw-inv');
    const inv = state.activeId ? C().invoice(state.activeId) : null;

    if (!inv) {
      host.innerHTML = renderEmpty();
      $('#swe-blank') && $('#swe-blank').addEventListener('click', () => newBlankInvoice());
      $('#swe-visit') && $('#swe-visit').addEventListener('click', () => {
        state.seg = 'ready';
        renderSidebar();
        const b = $('#sw-qlist'); if (b) b.parentElement.scrollTop = 0;
      });
      return;
    }
    // recompute
    Object.assign(inv, F().calcInvoice(inv));
    host.innerHTML = renderInvoiceContent(inv);
    bindInvoice(inv);
  }

  function renderEmpty() {
    const readyCt = readyEncounters().length;
    return `
      <div class="sw-empty">
        <div class="sw-empty-hero">${Icons.receipt || ''}</div>
        <h2>صدور فاکتور را از اینجا شروع کنید</h2>
        <p>ویزیت‌های آماده را به یک کلیک فاکتور کنید، یا برای مشتری گذری (پت‌شاپ) فروش مستقیم ثبت نمایید.</p>
        <div class="sw-empty-actions">
          <div class="sw-empty-card ${readyCt ? 'primary' : ''}" id="swe-visit">
            <div class="ico">${Icons.clipboard || ''}</div>
            <h4>فاکتور از ویزیت</h4>
            <p>ویزیت‌های پایان‌یافته که هنوز فاکتور نشده‌اند.</p>
            ${readyCt ? `<div class="count">${Icons.warning || '!'} ${toFa(readyCt)} مورد در انتظار</div>` : `<div style="font-size:11px;color:var(--muted-2)">همه فاکتور شده‌اند</div>`}
          </div>
          <div class="sw-empty-card" id="swe-blank">
            <div class="ico">${Icons.cart || ''}</div>
            <h4>فروش مستقیم</h4>
            <p>مشتری گذری، کالای پت‌شاپ یا خدمات جانبی.</p>
            <div style="font-size:11px;color:var(--muted-2)">فاکتور خالی ایجاد می‌شود</div>
          </div>
        </div>
      </div>
    `;
  }

  function renderInvoiceContent(inv) {
    const statusLabel = { draft: 'پیش‌نویس', partial: 'تسویه جزئی', settled: 'تسویه کامل', void: 'ابطال شده', refunded: 'مسترد شده' }[inv.state] || inv.state;
    const c = inv.customer || {};
    const isEnc = !!inv.encId;
    const enc = isEnc ? C().encounter(inv.encId) : null;
    const doc = enc ? doctorOf(enc.docId) : null;
    const isLocked = inv.state === 'settled' || inv.state === 'void' || inv.state === 'refunded';
    const avKey = c.pet ? ['a','b','c','d','e','f'][c.pet.charCodeAt(0) % 6] : 'ph';

    return `
      <!-- ---- HEAD ---- -->
      <div class="sw-inv-head">
        <div class="top">
          <div class="meta">
            <span class="lbl">شمارهٔ فاکتور</span>
            <span class="num">${inv.id}</span>
          </div>
          <div class="sw-inv-status ${inv.state}"><span class="dot"></span>${statusLabel}</div>
        </div>

        ${renderTerminalBanner(inv)}

        <div class="sw-cust ${c.owner ? '' : 'guest'}" id="sw-cust-picker" role="button" tabindex="0">
          ${c.owner
            ? `<div class="avatar-pet ${avKey}">${(c.owner || '?').slice(0,1)}</div>
               <div class="info">
                 <div class="name">${c.owner}${c.pet ? ` · ${c.pet}` : ''}</div>
                 <div class="sub">
                   ${c.phone ? `<span>${toFa(c.phone)}</span>` : ''}
                   ${isEnc ? `<span class="tag">پرونده دار</span>` : ''}
                 </div>
               </div>`
            : `<div class="avatar-pet ph">${Icons.users || '?'}</div>
               <div class="info">
                 <div class="name">مشتری گذری — انتخاب مشتری</div>
                 <div class="sub"><span>بدون پرونده — برای انتخاب کلیک کنید</span></div>
               </div>`
          }
          <div class="chev">${Icons.chevronDown || '▾'}</div>
        </div>

        ${enc && doc ? `
          <div class="sw-visit-ctx">
            <div class="ico">${Icons.clipboard || ''}</div>
            <div class="txt">
              فاکتور از ویزیت پایان‌یافته توسط <b>${doc.name}</b> ${enc.completedAt ? `— ${toFa(enc.completedAt.split(' ')[1] || '')}` : ''}
            </div>
            <a class="link" href="visits.html#enc-${enc.id}">مشاهدهٔ پرونده</a>
          </div>
        ` : ''}
      </div>

      <!-- ---- BODY (scrollable) ---- -->
      <div class="sw-body">
        <div class="sw-body-head">
          <div class="ttl">
            آیتم‌های فاکتور
            <span class="ct">${toFa(inv.lines.length)}</span>
          </div>
          ${!isLocked ? `
            <div class="sw-add-group">
              <button class="sw-add-btn" id="sw-open-catalog">
                ${Icons.plus || '+'} افزودن آیتم
                <kbd>Ctrl+K</kbd>
              </button>
              <button class="sw-add-btn sw-add-custom" id="sw-add-custom" title="افزودن آیتم دلخواه که در لیست آماده نیست">
                ${Icons.edit || Icons.plus || '+'} آیتم دلخواه
              </button>
            </div>
          ` : ''}
        </div>

        <div class="sw-lines">
          ${inv.lines.length === 0
            ? `<div class="sw-lines-empty">
                هیچ آیتمی افزوده نشده.<br/>
                <b>روی «افزودن آیتم» کلیک کنید</b> یا از میانبر استفاده نمایید:
                <div class="hint-key">
                  <kbd>Ctrl</kbd> + <kbd>K</kbd> برای باز کردن کاتالوگ
                </div>
              </div>`
            : inv.lines.map((ln, idx) => renderLine(ln, idx, isLocked)).join('')
          }
        </div>

        <div class="sw-adj">
          <div class="sw-adj-cell">
            <label>تخفیف فاکتور</label>
            <div class="row">
              <div class="seg">
                <button data-dk="pct" class="${(inv.discount || {}).kind !== 'amt' ? 'on' : ''}">درصد</button>
                <button data-dk="amt" class="${(inv.discount || {}).kind === 'amt' ? 'on' : ''}">مبلغ</button>
              </div>
              <input type="text" id="sw-disc-val" value="${toFa((inv.discount || {}).value || 0)}" ${isLocked ? 'disabled' : ''}/>
            </div>
          </div>
          <div class="sw-adj-cell">
            <label>پوشش بیمه</label>
            <select id="sw-ins" ${isLocked ? 'disabled' : ''}>
              <option value="">بدون بیمه</option>
              ${(C().INSURERS || []).map(o => `<option value="${o.id}" ${inv.insurance && inv.insurance.org === o.id ? 'selected' : ''}>${o.name} (${toFa(o.pct)}٪)</option>`).join('')}
            </select>
          </div>
        </div>

        ${inv.payments && inv.payments.length > 0 ? renderHistory(inv) : ''}
      </div>

      <!-- ---- FOOTER: summary + payment + actions ---- -->
      <div class="sw-foot">
        <div class="sw-foot-inner">
          <div class="sw-summary">
            <div class="sw-summary-row">
              <span class="lbl">جمع آیتم‌ها:</span>
              <span class="val">${moneyU(inv.base)}</span>
            </div>
            ${inv.discountAmt > 0 ? `<div class="sw-summary-row"><span class="lbl">تخفیف:</span><span class="val neg">− ${moneyU(inv.discountAmt)}</span></div>` : ''}
            <div class="sw-summary-row">
              <span class="lbl">مالیات (${toFa(inv.taxPct || 0)}٪):</span>
              <span class="val">${moneyU(inv.tax)}</span>
            </div>
            ${inv.insShare > 0 ? `<div class="sw-summary-row"><span class="lbl">سهم بیمه:</span><span class="val pos">− ${moneyU(inv.insShare)}</span></div>` : ''}
            <div class="spacer"></div>
            <div class="sw-summary-grand">
              <span class="lbl">مبلغ نهایی</span>
              <span class="val">${money(inv.total)}</span>
              <span class="u">تومان</span>
            </div>
          </div>

          ${inv.paid > 0 || inv.state === 'partial' ? `
            <div class="sw-paid-info">
              <div class="chip"><span class="lbl">پرداخت‌شده:</span><span class="val">${moneyU(inv.paid)}</span></div>
              ${inv.due > 0 ? `<div class="chip due"><span class="lbl">مانده:</span><span class="val">${moneyU(inv.due)}</span></div>` : ''}
            </div>
          ` : ''}

          ${!isLocked && inv.lines.length > 0 ? renderPayment(inv) : ''}
        </div>

        <div class="sw-actions">
          <button class="btn btn-outline" id="sw-print">${Icons.printer || ''} چاپ</button>
          ${!isLocked ? `<button class="btn btn-danger-ghost" id="sw-void">${Icons.x || ''} ابطال</button>` : ''}
          ${inv.state === 'partial' || inv.state === 'settled' ? `<button class="btn btn-outline" id="sw-refund">${Icons.arrowDownLeft || ''} عودت وجه</button>` : ''}
          <div class="spacer"></div>
          ${isLocked && inv.state === 'settled' ? `
            <button class="btn btn-outline" id="sw-new-after">${Icons.plus || '+'} فاکتور بعدی</button>
          ` : ''}
          ${!isLocked && inv.lines.length > 0 ? renderPayCTA(inv) : ''}
        </div>
      </div>
    `;
  }

  /* ---- Terminal-state banner (void / refunded). The step progress row was
         intentionally removed; only the terminal status banners remain. ---- */
  function renderTerminalBanner(inv) {
    if (inv.state === 'void') {
      return `<div class="sw-stepper terminal void">
        <div class="term-ico">${Icons.x || '×'}</div>
        <div class="term-txt"><b>فاکتور ابطال شد</b><span>این فاکتور دیگر قابل ویرایش یا پرداخت نیست.</span></div>
      </div>`;
    }
    if (inv.state === 'refunded') {
      return `<div class="sw-stepper terminal refunded">
        <div class="term-ico">${Icons.arrowDownLeft || '↩'}</div>
        <div class="term-txt"><b>وجه فاکتور عودت داده شد</b><span>مبلغ ${money(Math.abs((inv.payments || []).filter(p => p.amount < 0).reduce((a, p) => a + p.amount, 0)))} تومان مسترد شد.</span></div>
      </div>`;
    }
    return '';
  }

  /* ---- Smart CTA label: reflects what will happen when clicked ---- */
  function renderPayCTA(inv) {
    const amt = state.payment.amount || inv.due;
    const cappedAmt = Math.min(amt, inv.due);
    const willSettle = cappedAmt >= inv.due && inv.due > 0;
    const label = willSettle ? 'پرداخت و تسویه کامل' : 'ثبت پرداخت جزئی';
    const ico = willSettle ? (Icons.check || '✓') : (Icons.wallet || '');
    return `
      <button class="btn-pay ${willSettle ? 'ready-settle' : 'partial'}" id="sw-pay">
        ${ico}
        <span>${label}</span>
        <span class="amt">${money(cappedAmt)} تومان</span>
      </button>
    `;
  }

  function renderLine(ln, idx, isLocked) {
    const sub = Math.round((ln.price || 0) * (ln.qty || 1));
    const disc = Math.round(sub * (Number(ln.discPct) || 0) / 100);
    const net = sub - disc;
    return `
      <div class="sw-line" data-idx="${idx}">
        <div class="tag ${ln.kind === 'item' ? 'item' : ''} ${ln.custom ? 'custom' : ''}">${ln.custom ? (Icons.edit || Icons.plus || '✎') : (Icons[iconFor(ln.kind, ln.ref)] || Icons.clipboard || '')}</div>
        <div class="ttl">
          <div class="name">${ln.title}${ln.custom ? '<span class="sw-line-badge">دلخواه</span>' : ''}</div>
          <div class="unit">${money(ln.price)} تومان × ${toFa(ln.qty)}</div>
        </div>
        <div class="qty">
          <button data-act="dec" ${isLocked ? 'disabled' : ''} aria-label="کاهش">−</button>
          <input type="text" value="${toFa(ln.qty)}" data-act="qty" ${isLocked ? 'disabled' : ''}/>
          <button data-act="inc" ${isLocked ? 'disabled' : ''} aria-label="افزایش">+</button>
        </div>
        <div class="disc">
          <input type="text" value="${toFa(Number(ln.discPct) || 0)}" data-act="disc" ${isLocked ? 'disabled' : ''}/>
        </div>
        <div class="sub">
          ${disc > 0 ? `<span class="stroke">${money(sub)}</span>` : ''}
          <span>${money(net)}<span class="u">تومان</span></span>
        </div>
        <button class="rm" data-act="rm" ${isLocked ? 'disabled' : ''} aria-label="حذف">${Icons.trash || '×'}</button>
      </div>
    `;
  }

  function renderPayment(inv) {
    const methods = [
      { k: 'cash',   label: 'نقدی',   ic: 'wallet' },
      { k: 'card',   label: 'کارت',    ic: 'card' },
      { k: 'cheque', label: 'چک',      ic: 'cheque' },
      { k: 'credit', label: 'نسیه',   ic: 'clock' },
      { k: 'mixed',  label: 'ترکیبی', ic: 'copy' },
    ];
    const amt = state.payment.amount || inv.due;
    const cappedAmt = Math.min(amt, inv.due);
    const afterPay = Math.max(0, inv.due - cappedAmt);
    const willSettle = cappedAmt >= inv.due && inv.due > 0;
    const overAmt = amt > inv.due;

    // Payment status hint — subtle preview of what happens after this payment
    let preview = '';
    if (amt <= 0) {
      preview = `<div class="sw-pay-preview neutral">
        <span class="ico">${Icons.info || 'ℹ'}</span>
        مبلغ پرداخت را وارد کنید یا یکی از میان‌برها را انتخاب نمایید.
      </div>`;
    } else if (willSettle) {
      preview = `<div class="sw-pay-preview success">
        <span class="ico">${Icons.check || '✓'}</span>
        با این پرداخت فاکتور <b>کاملاً تسویه</b> می‌شود.
      </div>`;
    } else if (overAmt) {
      preview = `<div class="sw-pay-preview warn">
        <span class="ico">${Icons.warning || '!'}</span>
        مبلغ بیش از مانده است — فقط <b>${money(inv.due)}</b> تومان ثبت خواهد شد.
      </div>`;
    } else {
      preview = `<div class="sw-pay-preview partial">
        <span class="ico">${Icons.clock || '◔'}</span>
        پرداخت جزئی؛ پس از این ثبت <b>${money(afterPay)}</b> تومان مانده باقی می‌ماند.
      </div>`;
    }

    return `
      <div class="sw-pay ${willSettle ? 'will-settle' : ''}" style="margin-top:14px">
        <div class="sw-pay-head">
          <h4>${Icons.wallet || ''} روش پرداخت</h4>
          <div class="amt-preview">
            مانده: <b>${money(inv.due)}</b> تومان
          </div>
        </div>
        <div class="sw-methods">
          ${methods.map(m => `
            <button class="sw-method ${state.payment.method === m.k ? 'on' : ''}" data-m="${m.k}">
              ${Icons[m.ic] || ''}${m.label}
            </button>
          `).join('')}
        </div>
        <div class="sw-pay-amount-row">
          <div class="amount-wrap ${willSettle ? 'full' : ''}">
            <span class="u">تومان</span>
            <input type="text" id="sw-pay-amt" value="${toFa(amt)}" placeholder="مبلغ پرداخت"/>
          </div>
          <div class="sw-pay-presets">
            <button class="sw-pay-preset wide ${willSettle ? 'active' : ''}" data-pre="due">مانده کامل</button>
            <button class="sw-pay-preset" data-pre="half">٪۵۰</button>
            <button class="sw-pay-preset" data-pre="clear">صفر</button>
          </div>
        </div>
        ${preview}
      </div>
    `;
  }

  function renderHistory(inv) {
    const M = { cash: 'نقدی', card: 'کارت', cheque: 'چک', credit: 'نسیه', mixed: 'ترکیبی' };
    return `
      <div class="sw-history">
        <h4>${Icons.clock || ''} تاریخچهٔ پرداخت‌ها (${toFa(inv.payments.length)})</h4>
        ${inv.payments.map(p => `
          <div class="sw-history-item ${p.amount < 0 ? 'refund' : ''}">
            <span class="m ${p.method}">${M[p.method] || p.method}</span>
            <span class="note">${p.note || (p.amount < 0 ? 'عودت وجه' : 'پرداخت')} · ${p.at || ''}</span>
            <span class="amt">${p.amount < 0 ? '− ' : ''}${money(Math.abs(p.amount))} تومان</span>
          </div>
        `).join('')}
      </div>
    `;
  }

  /* ======================================================
     WIRING for invoice editor
     ====================================================== */
  function bindInvoice(inv) {
    // customer picker
    const cp = $('#sw-cust-picker');
    if (cp) {
      cp.addEventListener('click', openPicker);
      cp.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openPicker(); }});
    }

    // open catalog drawer
    $('#sw-open-catalog') && $('#sw-open-catalog').addEventListener('click', () => openCatalog());

    // add a custom (non-catalog) item — for goods/services not in the ready list
    $('#sw-add-custom') && $('#sw-add-custom').addEventListener('click', () => openCustomItemModal());

    // lines
    $$('#sw-inv .sw-line').forEach(row => {
      const idx = Number(row.dataset.idx);
      const ln = inv.lines[idx]; if (!ln) return;
      row.querySelectorAll('[data-act]').forEach(el => {
        const act = el.dataset.act;
        if (act === 'inc') el.addEventListener('click', () => { ln.qty = Math.min(999, (ln.qty || 1) + 1); renderInvoice(); });
        if (act === 'dec') el.addEventListener('click', () => { ln.qty = Math.max(1, (ln.qty || 1) - 1); renderInvoice(); });
        if (act === 'qty') el.addEventListener('change', () => {
          const v = parseFaNum(el.value) || 1;
          ln.qty = Math.max(1, Math.min(999, v)); renderInvoice();
        });
        if (act === 'disc') el.addEventListener('change', () => {
          const v = parseFaNum(el.value);
          ln.discPct = Math.max(0, Math.min(100, v)); renderInvoice();
        });
        if (act === 'rm') el.addEventListener('click', () => {
          row.classList.add('deleting');
          setTimeout(() => { inv.lines.splice(idx, 1); renderInvoice(); renderSidebar(); }, 200);
        });
      });
    });

    // discount seg
    $$('#sw-inv .sw-adj-cell .seg button[data-dk]').forEach(b => {
      b.addEventListener('click', () => {
        inv.discount = inv.discount || {};
        inv.discount.kind = b.dataset.dk;
        renderInvoice();
      });
    });
    $('#sw-disc-val') && $('#sw-disc-val').addEventListener('change', (e) => {
      const v = parseFaNum(e.target.value);
      inv.discount = inv.discount || { kind: 'pct' };
      inv.discount.value = Math.max(0, v);
      renderInvoice();
    });
    $('#sw-ins') && $('#sw-ins').addEventListener('change', (e) => {
      inv.insurance = e.target.value ? { org: e.target.value } : null;
      renderInvoice();
    });

    // payment method chips
    $$('#sw-inv .sw-method').forEach(b => {
      b.addEventListener('click', () => {
        state.payment.method = b.dataset.m;
        renderInvoice();
      });
    });

    // amount preset
    $$('#sw-inv .sw-pay-preset').forEach(b => {
      b.addEventListener('click', () => {
        const pre = b.dataset.pre;
        if (pre === 'due')   state.payment.amount = inv.due;
        if (pre === 'half')  state.payment.amount = Math.round(inv.due / 2);
        if (pre === 'clear') state.payment.amount = 0;
        renderInvoice();
      });
    });

    $('#sw-pay-amt') && $('#sw-pay-amt').addEventListener('change', (e) => {
      state.payment.amount = Math.max(0, parseFaNum(e.target.value));
      renderInvoice();
    });

    // buttons
    $('#sw-pay') && $('#sw-pay').addEventListener('click', () => doPayment(inv));
    $('#sw-print') && $('#sw-print').addEventListener('click', () => window.toast('فاکتور به چاپگر ارسال شد (نمونه طراحی)'));
    $('#sw-void') && $('#sw-void').addEventListener('click', () => doVoid(inv));
    $('#sw-refund') && $('#sw-refund').addEventListener('click', () => doRefund(inv));
    $('#sw-new-after') && $('#sw-new-after').addEventListener('click', () => {
      // Prefer routing user to ready-visits if any, else new blank invoice
      state.activeId = null;
      state.activeEncId = null;
      if (readyEncounters().length > 0) {
        state.seg = 'ready';
        renderSidebar();
        renderInvoice();
      } else {
        newBlankInvoice();
      }
    });
  }

  function doPayment(inv) {
    const amt = state.payment.amount || inv.due;
    if (amt <= 0) { window.toast('مبلغ پرداخت باید بزرگ‌تر از صفر باشد', 'x'); return; }
    if (amt > inv.due) { window.toast('مبلغ بیش از مانده — فقط به‌اندازهٔ مانده ثبت می‌شود', 'x'); }
    const capped = Math.min(amt, inv.due);
    const methodUsed = state.payment.method;
    const res = F().applyPayment(inv, { method: methodUsed, amount: capped, note: 'پرداخت از صندوق' });
    if (!res.ok) { window.toast(res.error || 'خطا در ثبت پرداخت', 'x'); return; }

    // Celebrate full settlement with a success modal — this is the "moment of truth"
    // the user was waiting for. For partial payments a quiet toast is enough.
    if (inv.state === 'settled') {
      showSettlementSuccess(inv, capped, methodUsed);
    } else {
      window.toast(`پرداخت ${money(capped)} تومان ثبت شد — مانده: ${money(inv.due)} تومان`);
    }

    state.payment.amount = Math.max(0, inv.due);
    renderSidebar();
    renderInvoice();
    renderKPIs();
  }
  function doVoid(inv) {
    openConfirm({
      tone: 'danger',
      icon: Icons.trash || Icons.x || '×',
      title: 'ابطال فاکتور',
      body: `آیا از ابطال فاکتور <b>«${inv.id}»</b> اطمینان دارید؟<br/>این عمل غیرقابل بازگشت است و فاکتور از لیست فعال حذف می‌شود.`,
      confirmLabel: 'بله، فاکتور را باطل کن',
      cancelLabel: 'انصراف',
      onConfirm: () => {
        const res = F().voidInvoice(inv);
        if (!res.ok) { window.toast(res.error || 'خطا در ابطال', 'x'); return; }
        window.toast(`فاکتور «${inv.id}» ابطال شد`);
        renderSidebar();
        renderInvoice();
        renderKPIs();
      },
    });
  }

  function doRefund(inv) {
    openRefundModal(inv);
  }

  function openRefundModal(inv) {
    const maxAmt = inv.paid || 0;
    const backdrop = document.createElement('div');
    backdrop.className = 'sw-modal-backdrop sw-confirm-backdrop';
    backdrop.innerHTML = `
      <div class="sw-modal sw-confirm sw-modal-refund" role="dialog">
        <div class="sw-confirm-head">
          <div class="sw-confirm-ico warn">${Icons.arrowDownLeft || '↩'}</div>
          <div>
            <h3>عودت وجه فاکتور</h3>
            <div class="sub">حداکثر مبلغ قابل عودت: <b>${money(maxAmt)}</b> تومان</div>
          </div>
        </div>
        <div class="sw-confirm-body">
          <label class="sw-confirm-label">مبلغ عودت (تومان)</label>
          <div class="amount-wrap">
            <span class="u">تومان</span>
            <input type="text" id="sw-refund-amt" value="${toFa(maxAmt)}" autofocus/>
          </div>
          <div class="sw-refund-presets">
            <button data-pre="full">کل مبلغ (${money(maxAmt)})</button>
            <button data-pre="half">نیمی (${money(Math.round(maxAmt / 2))})</button>
          </div>
          <div class="sw-confirm-note">
            مبلغ عودت از پرداخت‌های ثبت‌شده کسر می‌شود و در تاریخچه با یک ردیف منفی ثبت خواهد شد.
          </div>
        </div>
        <div class="sw-confirm-foot">
          <button class="btn btn-outline" data-close>انصراف</button>
          <button class="btn btn-primary" id="sw-refund-do">${Icons.check || '✓'} ثبت عودت</button>
        </div>
      </div>
    `;
    document.body.appendChild(backdrop);
    const close = () => backdrop.remove();
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });
    backdrop.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', close));

    const inp = backdrop.querySelector('#sw-refund-amt');
    backdrop.querySelectorAll('.sw-refund-presets button').forEach(b => {
      b.addEventListener('click', () => {
        const pre = b.dataset.pre;
        inp.value = toFa(pre === 'full' ? maxAmt : Math.round(maxAmt / 2));
      });
    });
    backdrop.querySelector('#sw-refund-do').addEventListener('click', () => {
      const amt = parseFaNum(inp.value);
      const res = F().refund(inv, amt);
      if (!res.ok) { window.toast(res.error || 'خطا در عودت', 'x'); return; }
      close();
      window.toast(`مبلغ ${money(amt)} تومان عودت شد`);
      renderSidebar();
      renderInvoice();
      renderKPIs();
    });
    setTimeout(() => inp.focus(), 100);
  }

  /* ---- Themed confirm dialog (replaces window.confirm) ---- */
  function openConfirm({ tone = 'neutral', icon = '', title = '', body = '', confirmLabel = 'تأیید', cancelLabel = 'انصراف', onConfirm }) {
    const backdrop = document.createElement('div');
    backdrop.className = 'sw-modal-backdrop sw-confirm-backdrop';
    backdrop.innerHTML = `
      <div class="sw-modal sw-confirm sw-confirm-${tone}" role="dialog" aria-modal="true">
        <div class="sw-confirm-head">
          <div class="sw-confirm-ico ${tone}">${icon}</div>
          <div>
            <h3>${title}</h3>
          </div>
        </div>
        <div class="sw-confirm-body">
          <p style="margin:0; color: var(--ink-2); font-size: 13.5px; line-height: 1.75;">${body}</p>
        </div>
        <div class="sw-confirm-foot">
          <button class="btn btn-outline" data-close>${cancelLabel}</button>
          <button class="btn ${tone === 'danger' ? 'btn-danger' : 'btn-primary'}" data-confirm>${confirmLabel}</button>
        </div>
      </div>
    `;
    document.body.appendChild(backdrop);
    const close = () => backdrop.remove();
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });
    backdrop.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', close));
    backdrop.querySelector('[data-confirm]').addEventListener('click', () => {
      close();
      if (onConfirm) onConfirm();
    });
    // ESC to close
    const onKey = (e) => {
      if (e.key === 'Escape') { close(); document.removeEventListener('keydown', onKey); }
    };
    document.addEventListener('keydown', onKey);
  }

  /* ---- Settlement success — the "moment of truth" ---- */
  function showSettlementSuccess(inv, thisPayment, method) {
    const M = { cash: 'نقدی', card: 'کارت', cheque: 'چک', credit: 'نسیه', mixed: 'ترکیبی' };
    const now = new Date();
    const pad2 = (n) => String(n).padStart(2, '0');
    const time = `${pad2(now.getHours())}:${pad2(now.getMinutes())}`;
    const receiptNo = 'RCP-' + toFa(Date.now().toString().slice(-6));
    const readyCt = readyEncounters().length;
    const nextLabel = readyCt > 0 ? `ویزیت بعدی (${toFa(readyCt)})` : 'فاکتور جدید';

    const backdrop = document.createElement('div');
    backdrop.className = 'sw-modal-backdrop sw-success-backdrop';
    backdrop.innerHTML = `
      <div class="sw-modal sw-success" role="dialog" aria-modal="true">
        <div class="sw-success-hero">
          <div class="sw-success-ring">
            <svg class="sw-success-check" viewBox="0 0 56 56" width="56" height="56" aria-hidden="true">
              <circle cx="28" cy="28" r="26" fill="none" stroke="currentColor" stroke-width="3" class="ring-circle"/>
              <path d="M15 29 L25 39 L42 20" fill="none" stroke="currentColor" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" class="ring-check"/>
            </svg>
          </div>
          <div class="sw-confetti">
            <span></span><span></span><span></span><span></span>
            <span></span><span></span><span></span><span></span>
          </div>
        </div>
        <div class="sw-success-body">
          <h2>فاکتور با موفقیت تسویه شد</h2>
          <p>پرداخت ثبت و فاکتور «${inv.id}» بسته شد.</p>

          <div class="sw-success-rows">
            <div class="row"><span class="lbl">شمارهٔ رسید</span><span class="val">${receiptNo}</span></div>
            <div class="row"><span class="lbl">مشتری</span><span class="val">${(inv.customer && inv.customer.owner) || 'مشتری گذری'}${inv.customer && inv.customer.pet ? ' · ' + inv.customer.pet : ''}</span></div>
            <div class="row"><span class="lbl">روش پرداخت این تراکنش</span><span class="val method"><span class="m-chip m-${method}">${M[method] || method}</span></span></div>
            <div class="row"><span class="lbl">مبلغ این پرداخت</span><span class="val">${money(thisPayment)} <span class="u">تومان</span></span></div>
            <div class="row total"><span class="lbl">جمع کل تسویه‌شده</span><span class="val">${money(inv.paid)} <span class="u">تومان</span></span></div>
          </div>
        </div>
        <div class="sw-success-foot">
          <button class="btn btn-outline" data-act="refund">${Icons.arrowDownLeft || '↩'} عودت وجه</button>
          <button class="btn btn-outline" data-act="print">${Icons.printer || ''} چاپ رسید</button>
          <button class="btn btn-outline" data-act="close">${Icons.x || '×'} بستن</button>
          <button class="btn btn-primary" data-act="next">${Icons.arrowLeft || Icons.arrowUpRight || '‹'} ${nextLabel}</button>
        </div>
      </div>
    `;
    document.body.appendChild(backdrop);
    const close = () => backdrop.remove();
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });
    backdrop.querySelector('[data-act="close"]').addEventListener('click', close);
    backdrop.querySelector('[data-act="print"]').addEventListener('click', () => {
      window.toast('رسید به چاپگر ارسال شد (نمونه طراحی)');
    });
    backdrop.querySelector('[data-act="refund"]').addEventListener('click', () => {
      close();
      openRefundModal(inv);
    });
    backdrop.querySelector('[data-act="next"]').addEventListener('click', () => {
      close();
      state.activeId = null;
      if (readyCt > 0) {
        state.seg = 'ready';
        renderSidebar();
        renderInvoice();
      } else {
        newBlankInvoice();
      }
    });
    // ESC closes
    const onKey = (e) => {
      if (e.key === 'Escape') { close(); document.removeEventListener('keydown', onKey); }
    };
    document.addEventListener('keydown', onKey);
  }

  /* ======================================================
     CATALOG DRAWER — scales to 1000s of items
     ====================================================== */
  function openCatalog() {
    state.catOpen = true;
    state.catQuery = '';
    state.catFilter = 'all';
    state.catHl = 0;
    $('#cat-drawer').classList.add('open');
    $('#cat-drawer').setAttribute('aria-hidden', 'false');
    setTimeout(() => { const s = $('#cat-search'); if (s) { s.value = ''; s.focus(); } }, 100);
    renderCatalog();
  }
  function closeCatalog() {
    state.catOpen = false;
    $('#cat-drawer').classList.remove('open');
    $('#cat-drawer').setAttribute('aria-hidden', 'true');
  }

  function catalogAllItems() {
    const svcs  = (C().SERVICES || []).map(s => ({ ...s, kind: 'svc', group: groupOf('svc', s.id) }));
    const items = (C().ITEMS    || []).map(i => ({ ...i, kind: 'item', group: groupOf('item', i.id) }));
    return svcs.concat(items);
  }

  function catalogFiltered() {
    let list = catalogAllItems();
    const f = state.catFilter;
    if (f === 'svc' || f === 'item') {
      list = list.filter(x => x.kind === f);
    } else if (f !== 'all') {
      list = list.filter(x => x.group.key === f);
    }
    const q = state.catQuery.trim().toLowerCase();
    if (q) {
      list = list.filter(x =>
        x.title.toLowerCase().indexOf(q) >= 0 ||
        (x.group && x.group.label && x.group.label.toLowerCase().indexOf(q) >= 0)
      );
    }
    return list;
  }

  function highlight(text, q) {
    if (!q) return text;
    const idx = text.toLowerCase().indexOf(q.toLowerCase());
    if (idx < 0) return text;
    return text.slice(0, idx) + '<mark>' + text.slice(idx, idx + q.length) + '</mark>' + text.slice(idx + q.length);
  }

  function renderCatFilters() {
    const all = catalogAllItems();
    // Build filter list: All, Services, Items, then top groups
    const groupCounts = {};
    all.forEach(x => {
      const k = x.group.key;
      groupCounts[k] = groupCounts[k] || { key: k, label: x.group.label, ct: 0 };
      groupCounts[k].ct++;
    });
    const svcCt = all.filter(x => x.kind === 'svc').length;
    const itemCt = all.filter(x => x.kind === 'item').length;

    const filters = [
      { k: 'all', label: 'همه', ct: all.length },
      { k: 'svc', label: 'خدمات', ct: svcCt },
      { k: 'item', label: 'دارو و کالا', ct: itemCt },
    ];
    // add groups sorted by count desc
    Object.values(groupCounts)
      .sort((a, b) => b.ct - a.ct)
      .forEach(g => filters.push({ k: g.key, label: g.label, ct: g.ct }));

    $('#cat-filters').innerHTML = filters.map(f => `
      <button class="cat-filter ${state.catFilter === f.k ? 'on' : ''}" data-f="${f.k}">
        ${f.label}<span class="ct">${toFa(f.ct)}</span>
      </button>
    `).join('');

    $$('#cat-filters .cat-filter').forEach(b => {
      b.addEventListener('click', () => {
        state.catFilter = b.dataset.f;
        state.catHl = 0;
        renderCatalog();
      });
    });
  }

  function renderCatRecent() {
    const host = $('#cat-recent');
    if (!state.recent || state.recent.length === 0 || state.catQuery.trim()) {
      host.classList.add('hidden');
      host.innerHTML = '';
      return;
    }
    const all = catalogAllItems();
    const chips = state.recent.map(r => all.find(x => x.kind === r.kind && x.id === r.ref)).filter(Boolean);
    if (chips.length === 0) { host.classList.add('hidden'); return; }
    host.classList.remove('hidden');
    host.innerHTML = `
      <div class="cat-drawer-recent-label">
        ${Icons.clock || ''} اخیراً استفاده شده
      </div>
      <div class="cat-drawer-recent-list">
        ${chips.map(x => `
          <div class="cat-recent-chip ${x.kind === 'item' ? 'item' : ''}" data-kind="${x.kind}" data-ref="${x.id}">
            <span class="ico">${Icons[iconFor(x.kind, x.id)] || Icons.clipboard || ''}</span>
            <span>${x.title}</span>
          </div>
        `).join('')}
      </div>
    `;
    $$('#cat-recent .cat-recent-chip').forEach(el => {
      el.addEventListener('click', () => addToInvoice(el.dataset.ref, el.dataset.kind));
    });
  }

  function renderCatalog() {
    renderCatFilters();
    renderCatRecent();

    const list = catalogFiltered();
    state.catList = list;
    if (state.catHl >= list.length) state.catHl = 0;

    const host = $('#cat-list');
    const empty = $('#cat-empty');
    if (list.length === 0) {
      host.innerHTML = '';
      empty.style.display = 'block';
    } else {
      empty.style.display = 'none';
      host.innerHTML = list.map((x, idx) => `
        <div class="cat-row ${x.kind === 'item' ? 'item' : ''} ${idx === state.catHl ? 'hl' : ''}" data-idx="${idx}" data-kind="${x.kind}" data-ref="${x.id}" role="option">
          <div class="ico">${Icons[iconFor(x.kind, x.id)] || Icons.clipboard || ''}</div>
          <div class="info">
            <div class="name">${highlight(x.title, state.catQuery.trim())}</div>
            <div class="cat-name">
              <span class="kind">${x.kind === 'svc' ? 'خدمت' : 'کالا'}</span>
              <span>${x.group.label}</span>
            </div>
          </div>
          <div class="price">${money(x.price)}<span class="u">ت</span></div>
          <button class="add" aria-label="افزودن">${Icons.plus || '+'}</button>
        </div>
      `).join('');

      $$('#cat-list .cat-row').forEach(row => {
        row.addEventListener('click', () => {
          addToInvoice(row.dataset.ref, row.dataset.kind);
        });
        row.addEventListener('mouseenter', () => {
          const i = Number(row.dataset.idx);
          if (!Number.isNaN(i)) { state.catHl = i; updateHl(); }
        });
      });
    }

    $('#cat-count').textContent = list.length
      ? `${toFa(list.length)} نتیجه`
      : '';
  }

  function updateHl() {
    $$('#cat-list .cat-row').forEach((el, i) => el.classList.toggle('hl', i === state.catHl));
    const hl = $('#cat-list .cat-row.hl');
    if (hl) hl.scrollIntoView({ block: 'nearest' });
  }

  function addToInvoice(refId, kind) {
    let inv = state.activeId ? C().invoice(state.activeId) : null;
    if (!inv || inv.state === 'settled' || inv.state === 'void' || inv.state === 'refunded') {
      newBlankInvoice();
      inv = C().invoice(state.activeId);
    }
    const cat = kind === 'svc'
      ? C().SERVICES.find(x => x.id === refId)
      : C().ITEMS.find(x => x.id === refId);
    if (!cat) return;
    const existing = inv.lines.find(l => l.ref === cat.id && l.kind === kind);
    if (existing) existing.qty = (existing.qty || 1) + 1;
    else inv.lines.push({ ref: cat.id, kind, title: cat.title, qty: 1, price: cat.price, discPct: 0 });
    pushRecent(kind, cat.id);
    renderInvoice();
    renderSidebar();
    window.toast(`«${cat.title}» به فاکتور افزوده شد`);
  }

  /* ---- Custom (non-catalog) item — for goods/services not in the ready list ---- */
  function openCustomItemModal() {
    const backdrop = document.createElement('div');
    backdrop.className = 'sw-modal-backdrop sw-confirm-backdrop';
    backdrop.innerHTML = `
      <div class="sw-modal sw-confirm sw-modal-custom" role="dialog" aria-modal="true">
        <div class="sw-confirm-head">
          <div class="sw-confirm-ico neutral">${Icons.edit || Icons.plus || '+'}</div>
          <div>
            <h3>افزودن آیتم دلخواه</h3>
            <div class="sub">برای کالا یا خدمتی که در لیست آماده وجود ندارد.</div>
          </div>
        </div>
        <div class="sw-confirm-body">
          <div class="sw-custom-form">
            <div class="sw-custom-field name">
              <label>نام آیتم</label>
              <input type="text" id="ci-name" autocomplete="off" placeholder="مثلاً: عکس رادیولوژی، کالای متفرقه…"/>
            </div>
            <div class="sw-custom-row">
              <div class="sw-custom-field">
                <label>مبلغ واحد (تومان)</label>
                <input type="text" id="ci-price" inputmode="numeric" placeholder="۰"/>
              </div>
              <div class="sw-custom-field">
                <label>تعداد</label>
                <input type="text" id="ci-qty" inputmode="numeric" value="۱"/>
              </div>
            </div>
            <div class="sw-custom-preview" id="ci-preview"></div>
          </div>
        </div>
        <div class="sw-confirm-foot">
          <button class="btn btn-outline" data-close>انصراف</button>
          <button class="btn btn-primary" id="ci-add">${Icons.plus || '+'} افزودن به فاکتور</button>
        </div>
      </div>
    `;
    document.body.appendChild(backdrop);
    const close = () => { backdrop.remove(); document.removeEventListener('keydown', onKey); };
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });
    backdrop.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', close));

    const nameEl  = backdrop.querySelector('#ci-name');
    const priceEl = backdrop.querySelector('#ci-price');
    const qtyEl   = backdrop.querySelector('#ci-qty');
    const prev    = backdrop.querySelector('#ci-preview');

    const updatePreview = () => {
      const price = Math.max(0, parseFaNum(priceEl.value) || 0);
      const qty   = Math.max(1, Math.min(999, parseFaNum(qtyEl.value) || 1));
      if (price > 0) {
        prev.innerHTML = `جمع این ردیف: <b>${money(price * qty)}</b> تومان`;
        prev.classList.add('on');
      } else {
        prev.classList.remove('on');
        prev.textContent = '';
      }
    };
    [priceEl, qtyEl].forEach(el => el.addEventListener('input', updatePreview));

    const submit = () => {
      const title = (nameEl.value || '').trim();
      const price = Math.max(0, parseFaNum(priceEl.value) || 0);
      const qty   = Math.max(1, Math.min(999, parseFaNum(qtyEl.value) || 1));
      if (!title) { window.toast('نام آیتم را وارد کنید', 'x'); nameEl.focus(); return; }
      if (price <= 0) { window.toast('مبلغ باید بزرگ‌تر از صفر باشد', 'x'); priceEl.focus(); return; }
      addCustomLine(title, price, qty);
      close();
    };
    backdrop.querySelector('#ci-add').addEventListener('click', submit);
    const onKey = (e) => {
      if (e.key === 'Escape') close();
      if (e.key === 'Enter' && (e.target === nameEl || e.target === priceEl || e.target === qtyEl)) submit();
    };
    document.addEventListener('keydown', onKey);
    setTimeout(() => nameEl.focus(), 100);
  }

  function addCustomLine(title, price, qty) {
    let inv = state.activeId ? C().invoice(state.activeId) : null;
    if (!inv || inv.state === 'settled' || inv.state === 'void' || inv.state === 'refunded') {
      newBlankInvoice();
      inv = C().invoice(state.activeId);
    }
    // Custom lines have no catalog ref; kind:'custom' keeps them distinct.
    inv.lines.push({ ref: null, kind: 'custom', title, qty, price, discPct: 0, custom: true });
    renderInvoice();
    renderSidebar();
    window.toast(`«${title}» به فاکتور افزوده شد`);
  }

  function bindCatalogGlobal() {
    // close handlers
    $$('#cat-drawer [data-close]').forEach(el => el.addEventListener('click', closeCatalog));

    // search
    $('#cat-search').addEventListener('input', (e) => {
      state.catQuery = e.target.value;
      state.catHl = 0;
      renderCatalog();
    });

    // keyboard nav inside search
    $('#cat-search').addEventListener('keydown', (e) => {
      if (!state.catOpen) return;
      const max = state.catList.length;
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        state.catHl = Math.min(max - 1, state.catHl + 1);
        updateHl();
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        state.catHl = Math.max(0, state.catHl - 1);
        updateHl();
      } else if (e.key === 'Enter') {
        e.preventDefault();
        const x = state.catList[state.catHl];
        if (x) addToInvoice(x.id, x.kind);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        closeCatalog();
      }
    });

    // global Ctrl+K / Cmd+K
    document.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault();
        if (state.catOpen) closeCatalog(); else openCatalog();
      }
      if (e.key === 'Escape' && state.catOpen) {
        closeCatalog();
      }
    });
  }

  /* ======================================================
     CUSTOMER PICKER
     ====================================================== */
  function openPicker() {
    if (state.pickerOpen) return;
    state.pickerOpen = true;
    state.pickerQuery = '';
    const owners = window.OWNERS || [];
    const backdrop = document.createElement('div');
    backdrop.className = 'sw-modal-backdrop';
    backdrop.innerHTML = `
      <div class="sw-modal" role="dialog">
        <div class="sw-modal-head">
          <h3>انتخاب مشتری</h3>
          <div class="sub">مشتری با پرونده را انتخاب کنید یا مشتری گذری ثبت نمایید.</div>
        </div>
        <div class="sw-modal-search">
          <input id="sw-pick-search" placeholder="جستجو در نام، شمارهٔ پرونده یا موبایل..." autofocus/>
        </div>
        <div class="sw-modal-body" id="sw-pick-body"></div>
      </div>
    `;
    document.body.appendChild(backdrop);
    const close = () => { backdrop.remove(); state.pickerOpen = false; };
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });

    const draw = () => {
      const q = state.pickerQuery.trim().toLowerCase();
      let list = owners;
      if (q) {
        list = owners.filter(o => (`${o.first} ${o.last} ${o.file} ${o.mobile1}`).toLowerCase().indexOf(q) >= 0);
      }
      const rows = list.slice(0, 20).map(o => {
        const pet = (window.PETS || []).find(p => p.ownerId === o.id);
        return `
          <div class="sw-owner-row" data-oid="${o.id}">
            <div class="avatar-pet ${o.tone || 'ph'}">${(o.first || '?').slice(0,1)}</div>
            <div class="info">
              <div class="name">${o.first} ${o.last}${pet ? ` · ${pet.name}` : ''}</div>
              <div class="sub">
                <span class="code">${o.file}</span>
                ${o.mobile1 ? `<span>${toFa(o.mobile1)}</span>` : ''}
                ${o.balance > 0 ? `<span class="bal">بدهی ${money(o.balance)}</span>` : ''}
              </div>
            </div>
            <div class="chev">${Icons.chevronLeft || '‹'}</div>
          </div>
        `;
      }).join('');
      $('#sw-pick-body').innerHTML = `
        <div class="sw-owner-none" id="sw-pick-guest">
          ${Icons.plus || '+'} ثبت به عنوان مشتری گذری (بدون پرونده)
        </div>
        ${rows || '<div style="padding:20px;text-align:center;color:var(--muted);font-size:12.5px">موردی یافت نشد</div>'}
      `;
      $$('#sw-pick-body .sw-owner-row').forEach(r => {
        r.addEventListener('click', () => selectOwner(r.dataset.oid, close));
      });
      $('#sw-pick-guest') && $('#sw-pick-guest').addEventListener('click', () => selectGuest(close));
    };
    $('#sw-pick-search').addEventListener('input', (e) => { state.pickerQuery = e.target.value; draw(); });
    draw();
  }

  function selectOwner(oid, close) {
    const inv = C().invoice(state.activeId); if (!inv) return;
    const o = (window.OWNERS || []).find(x => x.id === oid);
    if (!o) return;
    const pet = (window.PETS || []).find(p => p.ownerId === oid);
    inv.customer = {
      owner: `${o.first} ${o.last}`,
      pet: pet ? pet.name : '',
      phone: o.mobile1 || '',
    };
    close();
    renderInvoice();
    renderSidebar();
    window.toast(`مشتری «${inv.customer.owner}» انتخاب شد`);
  }
  function selectGuest(close) {
    const inv = C().invoice(state.activeId); if (!inv) return;
    inv.customer = { owner: '', pet: '', phone: '' };
    close();
    renderInvoice();
    renderSidebar();
  }

  /* ======================================================
     MINI KPIs (inline, discreet)
     ====================================================== */
  function renderKPIs() {
    const inv = C().INVOICES;
    const open = inv.filter(i => i.state === 'draft' || i.state === 'partial');
    const settled = inv.filter(i => i.state === 'settled');
    const sumDue = open.reduce((a, i) => a + (i.due || 0), 0);
    const sumPaid = settled.reduce((a, i) => a + (i.paid || 0), 0);
    const readyCt = readyEncounters().length;

    const items = [
      {
        lbl: 'تسویه‌شدهٔ امروز',
        val: money(sumPaid), u: 'تومان',
      },
      {
        lbl: 'مطالبات باز',
        val: money(sumDue), u: 'تومان',
        badge: open.length > 0 ? { text: toFa(open.length), tone: '' } : null,
      },
      {
        lbl: 'ویزیت آماده',
        val: toFa(readyCt), u: readyCt > 0 ? 'مورد' : '',
        badge: readyCt > 0 ? { text: 'در انتظار', tone: 'info' } : null,
      },
    ];

    $('#sw-kpi').innerHTML = items.map(s => `
      <div class="sw-mini-kpi">
        <div class="lbl">
          ${s.lbl}
          ${s.badge ? `<span class="badge ${s.badge.tone}">${s.badge.text}</span>` : ''}
        </div>
        <div class="val">${s.val}${s.u ? `<span class="u">${s.u}</span>` : ''}</div>
      </div>
    `).join('');
  }

  /* ======================================================
     BOOT
     ====================================================== */
  window.initSalePage = function () {
    renderKPIs();
    renderSidebar();

    // Search binding
    $('#sw-search').addEventListener('input', (e) => {
      state.query = e.target.value; renderSidebar();
    });

    renderInvoice();

    // Global buttons
    $('#sw-new-invoice') && $('#sw-new-invoice').addEventListener('click', () => newBlankInvoice());
    $('#sw-print-day')   && $('#sw-print-day').addEventListener('click', () => window.toast('گزارش روز به چاپگر ارسال شد (نمونه طراحی)'));

    // Catalog wiring
    bindCatalogGlobal();
  };
})();
