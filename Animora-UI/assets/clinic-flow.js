/* =========================================================
   وت‌پلاس · جریان بالینی–مالی — موتور جریان (فاز ۴)
   assets/clinic-flow.js — pure state machine + invoice math
   No DOM/CSS. Depends only on globals defined in clinic-data.js.
   ========================================================= */
(function () {
  'use strict';

  /* ---------------- enums (single reference, mirror CLINIC) ---------------- */
  const C = () => window.CLINIC; // data layer must be loaded first

  const ENUM = {
    QT: Object.freeze({
      waiting: 'waiting', called: 'called', inVisit: 'inVisit', done: 'done',
      left: 'left', noshow: 'noshow', cancel: 'cancel',
    }),
    ENC: Object.freeze({ draft: 'draft', done: 'done' }),
    INV: Object.freeze({ draft: 'draft', partial: 'partial', settled: 'settled', void: 'void', refunded: 'refunded' }),
    PAY: Object.freeze({ cash: 'cash', card: 'card', cheque: 'cheque', credit: 'credit', mixed: 'mixed' }),
  };

  /* ---------------- state machines (ARCHITECTURE §4.2–§4.4) ---------------- */
  // Can-transition tables — single source of truth for FLOW.can.
  const TICKET_EDGES = {
    waiting: ['called', 'left', 'cancel'],
    called: ['inVisit', 'noshow'],
    inVisit: ['done'],
    done: [], left: [], noshow: [], cancel: [],
  };
  const ENC_EDGES = { draft: ['done'], done: [] };
  const INV_EDGES = {
    draft: ['settled', 'partial', 'void'],
    partial: ['settled', 'void', 'refunded'],
    settled: ['refunded'],
    void: [], refunded: [],
  };

  const EDGES = { ticket: TICKET_EDGES, enc: ENC_EDGES, inv: INV_EDGES };
  const LABEL = {
    waiting: 'در انتظار', called: 'فراخوانده', inVisit: 'در ویزیت', done: 'پایان‌یافته',
    left: 'ترک صف', noshow: 'عدم مراجعه', cancel: 'لغو شده',
    draft: 'پیش‌نویس', partial: 'تسویه جزئی', settled: 'تسویه کامل', void: 'ابطال‌شده', refunded: 'مستردشده',
  };

  const kindOf = (ent) => {
    const id = String(ent && ent.id || '');
    if (id.indexOf('qt-') === 0) return 'ticket';
    if (id.indexOf('enc-') === 0) return 'enc';
    if (id.indexOf('INV-') === 0) return 'inv';
    return null;
  };

  const pad2 = (n) => String(n).padStart(2, '0');
  const nowStamp = () => {
    const d = new Date();
    return `${C().J.today()} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
  };

  /* ---------------- helpers ---------------- */
  // Line subtotal incl. optional per-row discount (discPct, defaults 0).
  const lineSub = (ln) =>
    Math.round((ln.price || 0) * (ln.qty || 1) * (100 - (Number(ln.discPct) || 0)) / 100);

  function calcInvoice(inv) {
    const lines = inv.lines || [];
    let base = 0, svcBase = 0;
    lines.forEach((ln) => {
      const sub = lineSub(ln);
      base += sub;
      if (ln.kind === 'svc') svcBase += sub;
    });
    const d = inv.discount || {};
    let discountAmt;
    if (d.kind === 'amt') discountAmt = Math.min(Math.round(d.value || 0), base);
    else discountAmt = Math.round(base * (d.value || 0) / 100);
    const afterDiscount = Math.max(0, base - discountAmt);
    // §8.2 step 4 — 9% tax on services only, after proportional discount split
    let svcAfter = 0;
    if (base > 0) svcAfter = Math.round(svcBase * afterDiscount / base);
    const tax = Math.round(svcAfter * (inv.taxPct || 0) / 100);
    // §8.2 step 5 — insurance share (constant pct of afterDiscount, D2)
    let insShare = 0;
    if (inv.insurance && inv.insurance.org) {
      const org = C().INSURERS.find((o) => o.id === inv.insurance.org);
      if (org) insShare = Math.round(afterDiscount * org.pct / 100);
    }
    const total = Math.max(0, afterDiscount + tax - insShare);
    const paid = inv.paid || 0;
    const due = Math.max(0, total - paid);
    return { base, discountAmt, afterDiscount, tax, insShare, total, due };
  }

  /* ---------------- invoice construction ---------------- */
  function buildInvoice(enc) {
    const svcs = enc.services || [], items = enc.items || [];
    if (svcs.length === 0 && items.length === 0) {
      return { ok: false, error: 'حداقل یک خدمت یا کالا ثبت کنید تا فاکتور صادر شود.' };
    }
    const lines = [];
    svcs.forEach((s) => {
      const cat = C().SERVICES.find((x) => x.id === s.sv);
      if (cat) lines.push({ ref: cat.id, kind: 'svc', title: cat.title, qty: s.qty || 1, price: cat.price, discPct: 0 });
    });
    items.forEach((it) => {
      const cat = C().ITEMS.find((x) => x.id === it.it);
      if (cat) lines.push({ ref: cat.id, kind: 'item', title: cat.title, qty: it.qty || 1, price: cat.price, discPct: 0 });
    });
    const tk = enc.ticketId ? C().ticket(enc.ticketId) : null;
    const pet = tk ? tk.pet : { owner: '', pet: '', phone: '' };
    const draft = {
      id: C().nextId('INV'), encId: enc.id, src: 'visit', date: C().today(),
      customer: { owner: pet.owner || '', pet: pet.name || '', phone: pet.phone || '' },
      lines, discount: { kind: 'pct', value: 0 }, insurance: null, taxPct: 9,
      base: 0, discountAmt: 0, afterDiscount: 0, tax: 0, insShare: 0,
      total: 0, paid: 0, due: 0, state: 'draft', payments: [],
    };
    Object.assign(draft, calcInvoice(draft)); // cache values (§8.2), paid=0 ⇒ due=total
    return { ok: true, invoice: draft };
  }

  /* ---------------- transitions ---------------- */
  function can(from, to, kind) {
    const edges = EDGES[kind || 'ticket'] || TICKET_EDGES;
    return (edges[from] || []).indexOf(to) >= 0;
  }

  const TKEY = { called: 'called', inVisit: 'entered', done: 'finished', left: 'left', noshow: 'noshow', cancel: 'cancel' };

  function transition(ent, to, payload) {
    const kind = kindOf(ent);
    if (!kind) return { ok: false, error: 'موجودیت ناشناخته است.', entity: ent };
    if (!can(ent.state, to, kind)) {
      return {
        ok: false,
        error: `گذار غیرمجاز: «${LABEL[ent.state] || ent.state}» به «${LABEL[to] || to}» مجاز نیست.`,
        entity: ent,
      };
    }
    // deeper guards
    const paid = ent.paid || 0;
    if (kind === 'inv' && to === 'settled' && paid < ent.total) {
      return { ok: false, error: 'برای تسویه کامل، مبلغ پرداخت باید به‌اندازهٔ کل فاکتور باشد.', entity: ent };
    }
    if (kind === 'inv' && to === 'partial' && !(paid > 0 && paid < ent.total)) {
      return { ok: false, error: 'تسویه جزئی فقط با پرداخت بخشی از مبلغ فاکتور ممکن است.', entity: ent };
    }
    if (kind === 'inv' && to === 'refunded' && !(ent.state === 'partial' || ent.state === 'settled')) {
      return { ok: false, error: 'عودت فقط روی فاکتور پرداخت‌شده (جزئی یا کامل) ممکن است.', entity: ent };
    }
    if (kind === 'enc' && to === 'done' && (ent.services || []).length + (ent.items || []).length === 0) {
      return { ok: false, error: 'حداقل یک خدمت یا کالا ثبت کنید تا ویزیت پایان یابد.', entity: ent };
    }
    const ts = payload && payload.at ? payload.at : nowStamp();
    ent.state = to;
    if (kind === 'ticket') {
      ent.times = ent.times || {};
      if (TKEY[to]) ent.times[TKEY[to]] = ts;
    }
    if (kind === 'enc' && to === 'done') ent.completedAt = ts;
    return { ok: true, entity: ent };
  }

  function applyPayment(inv, pay) {
    const amount = Math.round(Number(pay && pay.amount) || 0);
    if (!amount || amount <= 0) return { ok: false, error: 'مبلغ پرداخت باید بزرگ‌تر از صفر باشد.', entity: inv };
    if (inv.state === 'void' || inv.state === 'refunded') return { ok: false, error: 'این فاکتور قابلیت پرداخت ندارد.', entity: inv };
    if (inv.state === 'settled') return { ok: false, error: 'این فاکتور قبلاً تسویه شده است.', entity: inv };
    inv.paid += amount;
    inv.payments.push({
      method: pay.method || ENUM.PAY.cash, amount,
      ref: pay.ref || null, note: pay.note || '', at: pay.at || nowStamp(),
    });
    Object.assign(inv, calcInvoice(inv));
    inv.state = inv.paid >= inv.total ? ENUM.INV.settled : ENUM.INV.partial;
    return { ok: true, entity: inv };
  }

  function voidInvoice(inv) {
    if (inv.state === 'settled' || inv.state === 'refunded') {
      return { ok: false, error: 'فاکتور تسویه‌شده یا مستردشده قابل ابطال نیست.', entity: inv };
    }
    return transition(inv, ENUM.INV.void);
  }

  function refund(inv, amount) {
    const amt = Math.round(Number(amount) || 0);
    if (!(inv.state === 'partial' || inv.state === 'settled')) {
      return { ok: false, error: 'عودت فقط روی فاکتور پرداخت‌شده (جزئی یا کامل) ممکن است.', entity: inv };
    }
    if (!amt || amt <= 0 || amt > (inv.paid || 0)) {
      return { ok: false, error: 'مبلغ عودت باید بین ۱ ریال و کل مبلغ پرداخت‌شده باشد.', entity: inv };
    }
    inv.paid = Math.max(0, (inv.paid || 0) - amt);
    inv.payments.push({ method: ENUM.PAY.cash, amount: -amt, ref: null, note: 'عودت وجه', at: nowStamp() });
    inv.state = ENUM.INV.refunded;
    Object.assign(inv, calcInvoice(inv));
    return { ok: true, entity: inv };
  }

  // Queue is seeded by clinic-data.js at load; restore/return it for phase 5.
  function seedToday() {
    return C().QUEUE;
  }

  /* ---------------- export ---------------- */
  window.FLOW = {
    QT: ENUM.QT, ENC: ENUM.ENC, INV: ENUM.INV, PAY: ENUM.PAY,
    EDGES: Object.freeze(TICKET_EDGES) /* legacy: ticket edges */,
    can, transition, calcInvoice, buildInvoice, applyPayment, voidInvoice, refund, seedToday,
  };
})();
