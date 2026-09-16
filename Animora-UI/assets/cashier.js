/* =========================================================
   وت‌پلاس · صندوق و مالی (فاز ۷)
   assets/cashier.js — لیست فاکتور + ویرایش + پرداخت + ابطال/عودت
   Static UI prototype (L1–L3): state lives in CLINIC.INVOICES / CLINIC.ENCOUNTERS.
   اعتبارسنجی‌ها همه از FLOW عبور می‌کنند.
   ========================================================= */
(function () {
  'use strict';

  const $ = (s, r=document) => r.querySelector(s);
  const $$ = (s, r=document) => Array.from(r.querySelectorAll(s));
  const C = () => window.CLINIC;

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

  const money = (n) => (window.money ? window.money(n) : (window.toFa ? window.toFa(n) : n) + ' ریال');
  const toFa = (v) => (window.toFa ? window.toFa(v) : String(v));

  const STATE_META = {
    draft: { label: 'پیش‌نویس', tone: 'info' },
    partial: { label: 'تسویه جزئی', tone: 'warn' },
    settled: { label: 'تسویه شد', tone: 'ok' },
    void: { label: 'ابطال', tone: 'danger' },
    refunded: { label: 'عودت', tone: 'mute' },
  };
  const METHOD_META = {
    cash: 'نقدی', card: 'کارت', cheque: 'چک', credit: 'نسیه', mixed: 'ترکیبی',
  };

  /* ---------------- state ---------------- */
  const state = {
    tab: 'open',
    query: '',
    activeId: null,
    payment: { method: 'cash', amount: 0, ref: '', note: '' },
    refundAmt: 0,
  };

  /* ---------------- selection helpers ---------------- */
  function tabMatches(inv) {
    if (state.tab === 'all') return true;
    if (state.tab === 'open')   return inv.state === 'draft' || inv.state === 'partial';
    if (state.tab === 'settled')return inv.state === 'settled';
    if (state.tab === 'void')   return inv.state === 'void' || inv.state === 'refunded';
    return true;
  }

  /* ---------------- render: stats ---------------- */
  function renderStats() {
    const all = C().INVOICES;
    const open = all.filter(i => i.state === 'draft' || i.state === 'partial');
    const settled = all.filter(i => i.state === 'settled');
    const sumDue = open.reduce((a, i) => a + (i.due || 0), 0);
    const sumPaid = settled.reduce((a, i) => a + (i.paid || 0), 0);
    const sumToday = all.filter(i => i.payments && i.payments.length).reduce((a, i) => a + i.payments.reduce((b, p) => b + (p.amount > 0 ? p.amount : 0), 0), 0);
    const items = [
      { tone:'warn',  icon:'wallet',     label:'مطالبات باز',       val: sumDue,     foot: 'جمع ماندهٔ فاکتورهای پیش‌نویس/جزئی' },
      { tone:'ok',    icon:'check',      label:'تسویه‌های امروز',    val: sumPaid,    foot: 'مجموع فاکتورهای تسویه‌شده' },
      { tone:'info',  icon:'bank',       label:'جریان نقدی روز',    val: sumToday,   foot: 'مجموع پرداخت‌های ثبت‌شده' },
      { tone:'lilac', icon:'receipt',    label:'تعداد فاکتورها',    val: all.length, foot: 'پیش‌نویس / جزئی / تسویه / ابطال / مسترد' },
    ];
    $('#cs-stats').innerHTML = items.map(s => `
      <div class="stat cs-stat">
        <div class="stat-head">
          <div class="stat-icon ${s.tone}">${window.Icons[s.icon] || window.Icons.paw}</div>
          <div class="stat-delta">${s.foot}</div>
        </div>
        <div class="stat-value">${typeof s.val === 'number' && s.val > 1000 ? money(s.val) : toFa(s.val)}</div>
        <div class="stat-label">${s.label}</div>
      </div>
    `).join('');
  }

  /* ---------------- render: tabs counters ---------------- */
  function renderTabs() {
    const all = C().INVOICES;
    $('#ct-open').textContent    = toFa(all.filter(i => i.state === 'draft' || i.state === 'partial').length);
    $('#ct-settled').textContent = toFa(all.filter(i => i.state === 'settled').length);
    $('#ct-void').textContent    = toFa(all.filter(i => i.state === 'void' || i.state === 'refunded').length);
    $('#ct-all').textContent     = toFa(all.length);
  }

  /* ---------------- render: invoice list ---------------- */
  function renderList() {
    let rows = C().INVOICES.slice();
    rows = rows.filter(tabMatches);
    const q = state.query.trim();
    if (q) {
      rows = rows.filter(i =>
        i.id.includes(q) ||
        (i.customer.owner || '').includes(q) ||
        (i.customer.pet || '').includes(q)
      );
    }
    // Sort: open first, then by date desc, id desc
    rows.sort((a, b) => {
      const sa = ({draft:0,partial:1,settled:2,void:3,refunded:4})[a.state];
      const sb = ({draft:0,partial:1,settled:2,void:3,refunded:4})[b.state];
      if (sa !== sb) return sa - sb;
      return b.id.localeCompare(a.id);
    });

    const host = $('#cs-list');
    if (!rows.length) {
      host.innerHTML = `<div class="cs-empty"><div class="ico">${window.Icons.inbox || window.Icons.receipt}</div>هیچ فاکتوری در این تب یافت نشد.</div>`;
      return;
    }
    host.innerHTML = rows.map(inv => {
      const m = STATE_META[inv.state] || STATE_META.draft;
      const onCls = (inv.id === state.activeId) ? 'on' : '';
      const dueCls = (inv.state === 'settled' || inv.state === 'void' || inv.state === 'refunded') ? ' muted' : (inv.due > 0 ? '' : ' settled');
      return `
        <div class="cs-inv-row ${onCls}" data-inv="${inv.id}">
          <div style="display:grid; place-items:center; color: var(--muted)">${window.Icons.receipt || ''}</div>
          <div class="cust">
            <div class="name">${inv.customer.pet || '—'} <span style="color:var(--muted-2); font-weight:700; font-size: 11px">· ${inv.customer.owner || '—'}</span></div>
            <div class="pet"><span class="id">${inv.id}</span> · ${inv.date || ''}</div>
          </div>
          <div class="nums" style="text-align:start">
            <span class="pill pill-${m.tone}"><b>${m.label}</b></span>
          </div>
          <div class="nums">
            <div class="total">${money(inv.total || 0)}</div>
            <div class="due ${dueCls}">${(inv.state === 'settled') ? 'تسویه' : ((inv.state === 'void' || inv.state === 'refunded') ? (inv.state === 'void' ? 'ابطال‌شده' : 'عودت داده شد') : (inv.paid > 0 ? `پرداخت: ${money(inv.paid)}` : 'پرداخت نشده'))}</div>
          </div>
          <div class="nums" style="text-align:end">
            <div class="total ${dueCls.replace('muted','')}">${(inv.state === 'settled') ? '—' : money(inv.due || 0)}</div>
            <div class="lbl" style="font-size:11px; color: var(--muted)">مانده</div>
          </div>
        </div>`;
    }).join('');

    host.querySelectorAll('.cs-inv-row').forEach(r => {
      r.addEventListener('click', () => {
        state.activeId = r.dataset.inv;
        state.payment = { method: 'cash', amount: 0, ref: '', note: '' };
        state.refundAmt = 0;
        renderList();
        renderDetail();
      });
    });
  }

  /* ---------------- render: detail ---------------- */
  function renderDetail() {
    const head = $('#cs-detail-head');
    const body = $('#cs-detail-body');
    const foot = $('#cs-footbar');

    const inv = state.activeId ? C().invoice(state.activeId) : null;
    if (!inv) {
      head.innerHTML = `
        <div class="row">
          <div class="ico" style="width:48px;height:48px;border-radius:14px;background:var(--brand-50);color:var(--brand-700);display:grid;place-items:center">${window.Icons.receipt || ''}</div>
          <div style="flex:1; min-width:0">
            <h3>یک فاکتور انتخاب کنید</h3>
            <div class="cust" style="color:var(--muted); font-size:13px">از فهرست سمت چپ، فاکتور مورد نظر را برای مشاهده و پرداخت انتخاب کنید.</div>
          </div>
          <span class="rc-live">صندوق فعال</span>
        </div>`;
      body.innerHTML = `<div class="cs-empty"><div class="ico">${window.Icons.inbox || window.Icons.file}</div>هنوز فاکتوری باز نشده است.</div>`;
      foot.innerHTML = `<span class="muted" style="font-size:12px;color:var(--muted)">میان‌برها: <span class="kbd" style="padding:1px 6px;background:var(--bg-2);border-radius:6px">f</span> جستجو · <span class="kbd" style="padding:1px 6px;background:var(--bg-2);border-radius:6px">p</span> چاپ</span>`;
      return;
    }

    const m = STATE_META[inv.state] || STATE_META.draft;
    head.innerHTML = `
      <div class="row">
        <div style="display:flex; align-items:center; gap:12px; flex:1; min-width:0">
          <div style="width:48px;height:48px;border-radius:14px;background:linear-gradient(180deg, var(--brand-600) 0%, var(--brand-700) 100%); color:#fff; display:grid; place-items:center; box-shadow: 0 8px 22px rgba(13,148,136,.30)">
            ${window.Icons.receipt || ''}
          </div>
          <div style="min-width:0">
            <h3>${inv.customer.pet || 'فاکتور'} <span class="id">${inv.id}</span></h3>
            <div class="cust">مالک: <b>${inv.customer.owner || '—'}</b> · ${inv.customer.phone || '—'} · ${inv.date || ''}</div>
          </div>
        </div>
        <div class="pillbox">
          <span class="pill pill-${m.tone}"><b>${m.label}</b></span>
          <span class="pill pill-mute"><b>${inv.lines.length} ردیف</b></span>
        </div>
      </div>`;
    body.innerHTML = renderLines(inv) + renderTotals(inv) + renderPaymentPanel(inv) + renderPaymentHistory(inv);
    foot.innerHTML = renderActions(inv);
    bindDetail(inv);
  }

  function renderLines(inv) {
    const editable = (inv.state === 'draft' || inv.state === 'partial');
    const insOptions = C().INSURERS;
    const lines = inv.lines.map((l, i) => {
      const sum = l.qty * l.price * (100 - (l.discPct || 0)) / 100;
      return `
        <div class="cs-line" data-line="${i}">
          <div class="tag ${l.kind === 'item' ? 'item' : ''}">${l.kind === 'svc' ? 'خدمت' : 'کالا'}</div>
          <div class="ttl">${l.title}</div>
          <input type="number" min="1" value="${l.qty}" ${editable ? '' : 'disabled'} data-act="qty"/>
          <input type="number" min="0" max="100" value="${l.discPct || 0}" ${editable ? '' : 'disabled'} data-act="disc" title="تخفیف درصدی"/>
          <div class="sub">${money(sum)}</div>
          <button class="rm" data-act="rm" ${editable ? '' : 'style="visibility:hidden"'}>${window.Icons.x || ''}</button>
        </div>`;
    }).join('');
    return `
      <div class="cs-lines">
        ${lines || '<div class="cs-empty">این فاکتور بدون ردیف است.</div>'}
        ${editable ? `
          <div class="cs-line" style="grid-template-columns: 26px 1fr auto; background: var(--bg-2); border-style: dashed;">
            <div class="tag" style="background:#fff">+</div>
            <select id="cs-add-line" style="border:1px solid var(--line); border-radius: 8px; padding: 6px; font:inherit; background:#fff">
              <option value="">افزودن ردیف…</option>
              ${C().SERVICES.map(s => `<option value="svc:${s.id}" data-price="${s.price}">خدمت · ${s.title} · ${money(s.price)}</option>`).join('')}
              ${C().ITEMS.map(it => `<option value="item:${it.id}" data-price="${it.price}">کالا · ${it.title} · ${money(it.price)}</option>`).join('')}
            </select>
            <button class="btn btn-outline" id="cs-add-line-btn">${window.Icons.plus || ''} افزودن</button>
          </div>` : ''}
      </div>
      ${editable ? `
        <div class="cs-discount">
          <div class="field">
            <label>تخفیف سراسری</label>
            <div style="display:flex; gap:8px">
              <select class="selectbox" id="cs-disc-kind">
                <option value="pct" ${inv.discount && inv.discount.kind === 'pct' ? 'selected' : ''}>درصد (%)</option>
                <option value="amt" ${inv.discount && inv.discount.kind === 'amt' ? 'selected' : ''}>مبلغ (ریال)</option>
              </select>
              <input class="input" type="number" min="0" id="cs-disc-val" value="${inv.discount ? inv.discount.value : 0}"/>
            </div>
          </div>
          <div class="field">
            <label>بیمه</label>
            <select class="selectbox" id="cs-ins">
              <option value="" ${!inv.insurance ? 'selected' : ''}>بدون بیمه</option>
              ${insOptions.map(o => `<option value="${o.id}" ${inv.insurance && inv.insurance.org === o.id ? 'selected' : ''}>${o.name} (${toFa(o.pct)}٪)</option>`).join('')}
            </select>
          </div>
        </div>` : ''}
    `;
  }

  function renderTotals(inv) {
    const c = window.FLOW.calcInvoice(inv);
    return `
      <div class="cs-totals">
        <div class="row"><div class="lbl">جمع اقلام</div><div class="val">${money(c.base)}</div></div>
        <div class="row"><div class="lbl">تخفیف</div><div class="val">–${money(c.discountAmt)}</div></div>
        <div class="row"><div class="lbl">پس از تخفیف</div><div class="val">${money(c.afterDiscount)}</div></div>
        <div class="row"><div class="lbl">مالیات خدمات (۹٪)</div><div class="val">+${money(c.tax)}</div></div>
        <div class="row"><div class="lbl">سهم بیمه</div><div class="val">–${money(c.insShare)}</div></div>
        <div class="row tot"><div class="lbl">قابل پرداخت</div><div class="val">${money(c.total)}</div></div>
        <div class="paid">
          <div class="lbl">پرداخت‌شده</div>
          <div class="val">${money(inv.paid || 0)}</div>
        </div>
        ${inv.due > 0 ? `<div class="paid" style="border-top: 0; padding-top: 4px;"><div class="lbl" style="color: var(--danger); font-weight: 800">مانده</div><div class="val" style="color: var(--danger)">${money(inv.due)}</div></div>` : ''}
      </div>`;
  }

  function renderPaymentPanel(inv) {
    if (!['draft','partial'].includes(inv.state)) return '';

    const presets = [50000, 100000, 200000, 500000, 1000000];
    return `
      <div class="cs-payment">
        <div class="cs-method-row" id="cs-methods">
          ${[{k:'cash',l:'نقدی',i:'wallet'},{k:'card',l:'کارت',i:'card'},{k:'cheque',l:'چک',i:'cheque'},{k:'credit',l:'نسیه',i:'hourglass'},{k:'mixed',l:'ترکیبی',i:'layers'}].map(m => `
            <span class="chip ${state.payment.method === m.k ? 'on' : ''}" data-method="${m.k}">${window.Icons[m.i] || ''} ${m.l}</span>
          `).join('')}
        </div>
        <div>
          <label>مبلغ پرداخت (ریال)</label>
          <input class="input" type="number" min="0" id="cs-amount" value="${state.payment.amount || ''}" placeholder="${inv.due}"/>
          <div class="cs-pay-presets">
            ${presets.map(p => `<button data-preset="${p}">${money(p)}</button>`).join('')}
            <button data-preset="${inv.due}">پرداخت کامل (${money(inv.due)})</button>
          </div>
        </div>
        <div>
          <label>شمارهٔ مرجع (اختیاری)</label>
          <input class="input" type="text" id="cs-ref" value="${state.payment.ref || ''}" placeholder="مثلاً: شماره چک / تراکنش"/>
        </div>
        <div>
          <label>یادداشت</label>
          <input class="input" type="text" id="cs-note" value="${state.payment.note || ''}" placeholder="مثلاً: پیش‌پرداخت، ضمانت…"/>
        </div>
      </div>`;
  }

  function renderPaymentHistory(inv) {
    if (!inv.payments || !inv.payments.length) return '';
    const items = inv.payments.map(p => `
      <div class="cs-pay-history item ${p.amount < 0 ? 'neg' : ''}">
        <div class="m">${METHOD_META[p.method] || p.method}</div>
        <div>
          <div>${p.ref ? ' ref: ' + p.ref : ''}${p.note ? ' · ' + p.note : ''}</div>
          <div class="at">${p.at || ''}</div>
        </div>
        <div class="a">${p.amount > 0 ? '+' : ''}${money(p.amount)}</div>
      </div>
    `).join('');
    return `
      <div class="cs-pay-history">
        <div class="head"><h4>سابقهٔ پرداخت</h4><span class="pill pill-mute">${toFa(inv.payments.length)} مورد</span></div>
        ${items}
      </div>`;
  }

  function renderActions(inv) {
    const editable = ['draft','partial'].includes(inv.state);
    const paidEnough = inv.paid >= inv.total;
    return `
      <div style="display:flex; gap: 8px; flex-wrap: wrap;">
        <button class="btn btn-ghost" data-action="refresh">${window.Icons.refresh || ''} باز محاسبه</button>
        ${editable ? `<button class="btn btn-outline" data-action="void">${window.Icons.trash || ''} ابطال فاکتور</button>` : ''}
        ${(inv.state === 'partial' || inv.state === 'settled') ? `
          <div style="display:flex; gap:6px; align-items:center">
            <button class="btn btn-danger-ghost" data-action="refund">${window.Icons.arrowDownLeft || ''} عودت</button>
            <input type="number" min="0" max="${inv.paid}" placeholder="مبلغ عودت" value="${state.refundAmt || ''}" id="cs-refund-amt" style="width:140px; padding:8px 10px; border:1px solid var(--line); border-radius:10px"/>
          </div>
        ` : ''}
      </div>
      <div>
        ${editable ? `<button class="btn btn-primary" data-action="pay" ${paidEnough ? 'disabled' : ''}>${window.Icons.wallet || ''} ثبت پرداخت</button>` : ''}
        <button class="btn btn-outline" data-action="print">${window.Icons.printer || ''} چاپ</button>
      </div>`;
  }

  /* ---------------- binding ---------------- */
  function bindDetail(inv) {
    const editable = ['draft','partial'].includes(inv.state);
    // editable line fields
    $('#cs-detail-body').querySelectorAll('.cs-line').forEach(row => {
      const i = parseInt(row.dataset.line || '-1', 10);
      if (i < 0) return; // add-line row
      row.querySelectorAll('input[data-act]').forEach(inp => {
        inp.addEventListener('input', () => {
          const act = inp.dataset.act;
          if (act === 'qty')  inv.lines[i].qty = Math.max(1, parseInt(inp.value, 10) || 1);
          if (act === 'disc') inv.lines[i].discPct = Math.min(100, Math.max(0, parseInt(inp.value, 10) || 0));
          Object.assign(inv, window.FLOW.calcInvoice(inv));
          rerenderInline(i, inv);
        });
      });
      row.querySelector('[data-act="rm"]')?.addEventListener('click', () => {
        inv.lines.splice(i, 1); Object.assign(inv, window.FLOW.calcInvoice(inv));
        renderDetail(); renderStats(); renderList(); renderTabs();
      });
    });

    if (editable) {
      $('#cs-disc-kind')?.addEventListener('change', (e) => {
        inv.discount = inv.discount || { kind: 'pct', value: 0 };
        inv.discount.kind = e.target.value;
        Object.assign(inv, window.FLOW.calcInvoice(inv));
        rerenderTotalsOnly(inv);
      });
      $('#cs-disc-val')?.addEventListener('input', (e) => {
        inv.discount = inv.discount || { kind: 'pct', value: 0 };
        inv.discount.value = Math.max(0, Math.round(Number(e.target.value) || 0));
        Object.assign(inv, window.FLOW.calcInvoice(inv));
        rerenderTotalsOnly(inv);
      });
      $('#cs-ins')?.addEventListener('change', (e) => {
        inv.insurance = e.target.value ? { org: e.target.value } : null;
        Object.assign(inv, window.FLOW.calcInvoice(inv));
        rerenderTotalsOnly(inv);
      });

      $('#cs-add-line-btn')?.addEventListener('click', () => {
        const sel = $('#cs-add-line');
        const v = sel.value; if (!v) return;
        const [kind, id] = v.split(':');
        const cat = (kind === 'svc' ? C().SERVICES : C().ITEMS).find(x => x.id === id);
        if (!cat) return;
        inv.lines.push({ ref: id, kind, title: cat.title, qty: 1, price: cat.price, discPct: 0 });
        Object.assign(inv, window.FLOW.calcInvoice(inv));
        renderDetail(); renderStats(); renderList();
      });
    }

    // payment panel
    if (['draft','partial'].includes(inv.state)) {
      $$('#cs-methods .chip').forEach(c => {
        c.addEventListener('click', () => {
          state.payment.method = c.dataset.method;
          $$('#cs-methods .chip').forEach(x => x.classList.remove('on'));
          c.classList.add('on');
        });
      });
      $('#cs-amount')?.addEventListener('input', (e) => state.payment.amount = Math.max(0, Math.round(Number(e.target.value) || 0)));
      $('#cs-ref')?.addEventListener('input', (e) => state.payment.ref = e.target.value);
      $('#cs-note')?.addEventListener('input', (e) => state.payment.note = e.target.value);
      $$('.cs-pay-presets button').forEach(b => {
        b.addEventListener('click', () => {
          state.payment.amount = parseInt(b.dataset.preset, 10) || 0;
          $('#cs-amount').value = state.payment.amount;
        });
      });
    }

    // footer actions
    const foot = $('#cs-footbar');
    foot.querySelectorAll('[data-action]').forEach(b => {
      b.addEventListener('click', () => {
        const a = b.dataset.action;
        if (a === 'refresh') {
          Object.assign(inv, window.FLOW.calcInvoice(inv));
          renderDetail(); renderStats(); renderList();
        }
        if (a === 'void')   doVoid(inv);
        if (a === 'pay')    doPay(inv);
        if (a === 'refund') doRefund(inv);
        if (a === 'print')  doPrint();
      });
    });
  }

  function rerenderInline(idx, inv) {
    const row = $(`.cs-line[data-line="${idx}"] .sub`);
    if (!row) return;
    const l = inv.lines[idx];
    row.textContent = money(l.qty * l.price * (100 - (l.discPct || 0)) / 100);
    rerenderTotalsOnly(inv);
  }

  function rerenderTotalsOnly(inv) {
    const c = window.FLOW.calcInvoice(inv);
    const box = $('.cs-totals');
    if (!box) return;
    box.outerHTML = renderTotals(inv);
  }

  /* ---------------- actions ---------------- */
  function doPay(inv) {
    const payAmount = (state.payment.amount && state.payment.amount > 0) ? state.payment.amount : (inv.due || 0);
    const payload = Object.assign({}, state.payment, { amount: payAmount });
    const r = window.FLOW.applyPayment(inv, payload);
    if (!r.ok) { toast(r.error, 'x'); return; }
    const verb = inv.state === 'settled' ? 'تسویه شد' : 'پرداخت ثبت شد';
    toast(`${verb} (نمونه طراحی) · ${money(payAmount)}`, 'check');
    state.payment = { method: state.payment.method, amount: 0, ref: '', note: '' };
    renderAll();
  }

  function doVoid(inv) {
    const r = window.FLOW.voidInvoice(inv);
    if (!r.ok) { toast(r.error, 'x'); return; }
    toast('فاکتور ابطال شد (نمونه طراحی)', 'trash');
    renderAll();
  }

  function doRefund(inv) {
    const amt = parseInt(($('#cs-refund-amt')?.value || 0), 10);
    const r = window.FLOW.refund(inv, amt || (inv.paid || 0));
    if (!r.ok) { toast(r.error, 'x'); return; }
    toast(`عودت ثبت شد (نمونه طراحی) · ${money(amt || inv.paid)}`, 'arrowUpRight');
    renderAll();
  }

  function doPrint() {
    toast('گزینهٔ چاپ در نسخهٔ بعدی فعال می‌شود (نمونه طراحی)', 'bulb');
  }

  /* ---------------- render all ---------------- */
  function renderAll() {
    renderStats();
    renderTabs();
    renderList();
    renderDetail();
  }

  /* ---------------- bind topbar ---------------- */
  function bindTopbar() {
    $$('#cs-tabs button').forEach(b => b.addEventListener('click', () => {
      $$('#cs-tabs button').forEach(x => x.classList.remove('on'));
      b.classList.add('on');
      state.tab = b.dataset.tab;
      state.activeId = null;
      renderList();
      renderDetail();
    }));
    $('#cs-search').addEventListener('input', (e) => {
      state.query = e.target.value || '';
      renderList();
    });
    $('#cs-print').addEventListener('click', doPrint);
    $('#cs-export').addEventListener('click', () => toast('خروجی اکسل به‌زودی (نمونه طراحی)', 'bulb'));
  }

  /* ---------------- deep-link #inv-<id> ---------------- */
  function interpretHash() {
    const h = (location.hash || '').replace(/^#/, '');
    const m = h.match(/^inv-(.+)$/i);
    if (!m) return;
    const id = m[1].toUpperCase();
    const inv = C().invoice(id);
    if (!inv) return;
    state.activeId = inv.id;
    renderList();
    renderDetail();
  }

  /* ---------------- boot ---------------- */
  window.initCashierPage = function () {
    if (!window.FLOW || !window.CLINIC) {
      console.error('[cashier] FLOW/CLINIC not loaded');
      return;
    }
    bindTopbar();
    renderAll();
    interpretHash();
    window.addEventListener('hashchange', interpretHash);
  };
})();
