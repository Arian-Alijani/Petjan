/* Inventory mock data — Persian veterinary clinic
 * All values in-memory only. Mutations vanish on reload (per L3 in CLAUDE.md).
 */
(function () {
  // --- Categories ---------------------------------------------------------
  const CATS = [
    { id: 'med',     label: 'دارو',       tone: 'teal',   icon: 'flask'  },
    { id: 'petshop', label: 'پت‌شاپ',     tone: 'peach',  icon: 'bone'   },
    { id: 'vaccine', label: 'واکسن',      tone: 'lilac',  icon: 'syringe'},
    { id: 'food',    label: 'غذا',        tone: 'amber',  icon: 'bone'   },
    { id: 'tool',    label: 'ابزار درمانی', tone: 'blue', icon: 'tools'  },
    { id: 'care',    label: 'بهداشتی',    tone: 'green',  icon: 'bath'   },
  ];

  // --- Suppliers ----------------------------------------------------------
  const SUPPLIERS = [
    { id: 'sup1', name: 'پخش دارویی سپهر',   contact: 'آقای رحیمی',    phone: '۰۹۱۲۳۴۵۶۷۸۹', city: 'تهران',   balance: -18400000, orders: 24, tag: 'دارو و واکسن' },
    { id: 'sup2', name: 'پت شاپ رویال',      contact: 'خانم موسوی',    phone: '۰۹۱۲۹۹۸۸۷۷۶', city: 'کرج',     balance: 0,         orders: 18, tag: 'پت‌شاپ و غذا' },
    { id: 'sup3', name: 'تجهیزات بهدام',      contact: 'آقای ثابتی',    phone: '۰۹۱۲۴۴۵۵۶۶۷', city: 'اصفهان',  balance: -3200000,  orders: 7,  tag: 'ابزار درمانی' },
    { id: 'sup4', name: 'داروگر پارس',       contact: 'آقای نوروزی',   phone: '۰۹۱۲۰۰۰۱۱۲۲', city: 'تهران',   balance: 850000,    orders: 12, tag: 'دارو' },
    { id: 'sup5', name: 'شرکت نیک‌ذخیره',    contact: 'خانم اسدی',     phone: '۰۹۱۲۷۷۸۸۹۹۰', city: 'مشهد',    balance: 0,         orders: 5,  tag: 'مکمل و ویتامین' },
  ];

  // --- Products with variants --------------------------------------------
  // variant.stock is the source of truth. Product totals are derived.
  const PRODUCTS = [
    {
      id: 'p1', code: 'AN-1082',
      name: 'قرص ضد انگل دورامکتین',
      brand: 'BioVet',
      cat: 'med',
      icon: '💊', tone: 'a',
      description: 'قرص خوراکی ضد انگل داخلی و خارجی برای سگ و گربه.',
      attribute: { name: 'دوز', options: ['۵ mg', '۱۰ mg', '۲۰ mg'] },
      variants: [
        { id: 'v1a', label: '۵ mg',  sku: 'AN-1082-05', stock: 42, reorder: 15, cost: 95000,  price: 130000, expiry: '۱۴۰۶/۰۳/۱۵' },
        { id: 'v1b', label: '۱۰ mg', sku: 'AN-1082-10', stock: 3,  reorder: 10, cost: 120000, price: 160000, expiry: '۱۴۰۶/۰۳/۱۵' },
        { id: 'v1c', label: '۲۰ mg', sku: 'AN-1082-20', stock: 12, reorder: 8,  cost: 180000, price: 240000, expiry: '۱۴۰۶/۰۳/۱۵' },
      ],
    },
    {
      id: 'p2', code: 'RC-1147',
      name: 'غذای گربه رویال کنین',
      brand: 'Royal Canin',
      cat: 'food',
      icon: '🥫', tone: 'b',
      description: 'غذای خشک استریل ویژه گربه‌های بالغ عقیم‌شده.',
      attribute: { name: 'وزن', options: ['۲ کیلوگرم', '۴ کیلوگرم', '۸ کیلوگرم', '۱۵ کیلوگرم'] },
      variants: [
        { id: 'v2a', label: '۲ کیلوگرم',  sku: 'RC-1147-2',  stock: 24, reorder: 10, cost: 620000,  price: 780000,  expiry: '۱۴۰۶/۰۸/۲۰' },
        { id: 'v2b', label: '۴ کیلوگرم',  sku: 'RC-1147-4',  stock: 42, reorder: 15, cost: 980000,  price: 1250000, expiry: '۱۴۰۶/۰۸/۲۰' },
        { id: 'v2c', label: '۸ کیلوگرم',  sku: 'RC-1147-8',  stock: 18, reorder: 8,  cost: 1780000, price: 2250000, expiry: '۱۴۰۶/۰۸/۲۰' },
        { id: 'v2d', label: '۱۵ کیلوگرم', sku: 'RC-1147-15', stock: 6,  reorder: 5,  cost: 3050000, price: 3800000, expiry: '۱۴۰۶/۰۸/۲۰' },
      ],
    },
    {
      id: 'p3', code: 'VC-1205',
      name: 'واکسن هاری',
      brand: 'Rabisin',
      cat: 'vaccine',
      icon: '💉', tone: 'c',
      description: 'واکسن غیرفعال ضد هاری، تک‌دوز داخل عضلانی.',
      attribute: null, // no variants — single item
      variants: [
        { id: 'v3a', label: 'تک‌دوز ۱ml', sku: 'VC-1205', stock: 4, reorder: 15, cost: 320000, price: 480000, expiry: '۱۴۰۵/۱۱/۱۸' },
      ],
    },
    {
      id: 'p4', code: 'SH-1318',
      name: 'شامپو ضد کنه و شپش',
      brand: 'CleanPet',
      cat: 'care',
      icon: '🧴', tone: 'd',
      description: 'شامپو موضعی برای از بین بردن انگل خارجی.',
      attribute: { name: 'حجم', options: ['۲۵۰ml', '۵۰۰ml', '۱ لیتر'] },
      variants: [
        { id: 'v4a', label: '۲۵۰ml', sku: 'SH-1318-250', stock: 18, reorder: 10, cost: 280000, price: 420000, expiry: '۱۴۰۷/۰۲/۰۵' },
        { id: 'v4b', label: '۵۰۰ml', sku: 'SH-1318-500', stock: 22, reorder: 8,  cost: 490000, price: 720000, expiry: '۱۴۰۷/۰۲/۰۵' },
        { id: 'v4c', label: '۱ لیتر', sku: 'SH-1318-1L',  stock: 5,  reorder: 6,  cost: 820000, price: 1180000, expiry: '۱۴۰۷/۰۲/۰۵' },
      ],
    },
    {
      id: 'p5', code: 'AM-1406',
      name: 'شربت آنتی‌بیوتیک آموکسی‌سیلین',
      brand: 'Pharma',
      cat: 'med',
      icon: '🧪', tone: 'e',
      description: 'آنتی‌بیوتیک وسیع‌الطیف خوراکی.',
      attribute: { name: 'حجم', options: ['۶۰ml', '۱۲۰ml'] },
      variants: [
        { id: 'v5a', label: '۶۰ml',  sku: 'AM-1406-60',  stock: 14, reorder: 10, cost: 180000, price: 260000, expiry: '۱۴۰۶/۰۵/۱۰' },
        { id: 'v5b', label: '۱۲۰ml', sku: 'AM-1406-120', stock: 5,  reorder: 12, cost: 210000, price: 280000, expiry: '۱۴۰۶/۰۵/۱۰' },
      ],
    },
    {
      id: 'p6', code: 'TY-1520',
      name: 'اسباب‌بازی توپ لاستیکی',
      brand: 'Kong',
      cat: 'petshop',
      icon: '🎾', tone: 'f',
      description: 'توپ مقاوم برای بازی و آموزش.',
      attribute: { name: 'سایز', options: ['کوچک', 'متوسط', 'بزرگ'] },
      variants: [
        { id: 'v6a', label: 'کوچک',  sku: 'TY-1520-S', stock: 34, reorder: 15, cost: 85000,  price: 140000, expiry: null },
        { id: 'v6b', label: 'متوسط', sku: 'TY-1520-M', stock: 28, reorder: 12, cost: 120000, price: 190000, expiry: null },
        { id: 'v6c', label: 'بزرگ',  sku: 'TY-1520-L', stock: 15, reorder: 10, cost: 165000, price: 260000, expiry: null },
      ],
    },
    {
      id: 'p7', code: 'SR-1670',
      name: 'سرنگ یک‌بار مصرف',
      brand: 'MediTools',
      cat: 'tool',
      icon: '🩺', tone: 'a',
      description: 'سرنگ استریل با سرسوزن جداشونده.',
      attribute: { name: 'حجم', options: ['۱ml', '۲.۵ml', '۵ml', '۱۰ml'] },
      variants: [
        { id: 'v7a', label: '۱ml',   sku: 'SR-1670-1',  stock: 240, reorder: 100, cost: 4500, price: 8000,  expiry: '۱۴۰۸/۰۱/۰۱' },
        { id: 'v7b', label: '۲.۵ml', sku: 'SR-1670-25', stock: 180, reorder: 80,  cost: 5200, price: 9500,  expiry: '۱۴۰۸/۰۱/۰۱' },
        { id: 'v7c', label: '۵ml',   sku: 'SR-1670-5',  stock: 90,  reorder: 60,  cost: 6000, price: 11000, expiry: '۱۴۰۸/۰۱/۰۱' },
        { id: 'v7d', label: '۱۰ml',  sku: 'SR-1670-10', stock: 42,  reorder: 40,  cost: 7800, price: 14000, expiry: '۱۴۰۸/۰۱/۰۱' },
      ],
    },
    {
      id: 'p8', code: 'VT-1811',
      name: 'ویتامین B کمپلکس',
      brand: 'VetLab',
      cat: 'med',
      icon: '💊', tone: 'b',
      description: 'مکمل ویتامین B برای تقویت اشتها و سلامت پوست.',
      attribute: null,
      variants: [
        { id: 'v8a', label: '۳۰ عددی', sku: 'VT-1811', stock: 68, reorder: 20, cost: 145000, price: 210000, expiry: '۱۴۰۷/۰۶/۲۰' },
      ],
    },
  ];

  // --- Purchase Orders ---------------------------------------------------
  const POS = [
    {
      id: 'po1', code: 'PO-14052001',
      supplier: 'sup1',
      date: '۱۴۰۵/۰۸/۰۲',
      status: 'received', // draft | ordered | received | partial
      note: 'خرید ماهانه دارو',
      items: [
        { productId: 'p1', variantId: 'v1b', qty: 20, cost: 118000 },
        { productId: 'p3', variantId: 'v3a', qty: 15, cost: 315000 },
        { productId: 'p5', variantId: 'v5b', qty: 10, cost: 205000 },
      ],
    },
    {
      id: 'po2', code: 'PO-14052008',
      supplier: 'sup2',
      date: '۱۴۰۵/۰۸/۰۹',
      status: 'ordered',
      note: 'تأمین غذای گربه',
      items: [
        { productId: 'p2', variantId: 'v2b', qty: 30, cost: 970000 },
        { productId: 'p2', variantId: 'v2c', qty: 12, cost: 1760000 },
      ],
    },
    {
      id: 'po3', code: 'PO-14052012',
      supplier: 'sup3',
      date: '۱۴۰۵/۰۸/۱۳',
      status: 'partial',
      note: '',
      items: [
        { productId: 'p7', variantId: 'v7a', qty: 500, cost: 4300 },
        { productId: 'p7', variantId: 'v7c', qty: 200, cost: 5800 },
      ],
    },
    {
      id: 'po4', code: 'PO-14052015',
      supplier: 'sup4',
      date: '۱۴۰۵/۰۸/۱۶',
      status: 'draft',
      note: 'در انتظار تأیید مدیر',
      items: [
        { productId: 'p5', variantId: 'v5a', qty: 15, cost: 175000 },
        { productId: 'p8', variantId: 'v8a', qty: 20, cost: 140000 },
      ],
    },
    {
      id: 'po5', code: 'PO-14052018',
      supplier: 'sup1',
      date: '۱۴۰۵/۰۸/۱۹',
      status: 'ordered',
      note: 'واکسن‌های فصلی',
      items: [
        { productId: 'p3', variantId: 'v3a', qty: 40, cost: 310000 },
      ],
    },
  ];

  // --- Stock movement / history log --------------------------------------
  // Chronological ledger of every stock change for a product. Newest first.
  // kind: 'in' (increase) | 'out' (decrease) | 'adjust' (manual correction)
  // Each entry references a product (and optionally a variant) plus a source
  // and the actor who performed it — this powers the "تاریخچه" tab.
  const MOVES = [
    // p1 — قرص ضد انگل دورامکتین
    { id: 'm1',  productId: 'p1', variantId: 'v1b', kind: 'in',     qty: 20, source: 'خرید PO-14052001', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۰۲', time: '۰۹:۱۲' },
    { id: 'm2',  productId: 'p1', variantId: 'v1a', kind: 'out',    qty: 3,  source: 'فروش صندوق INV-1058', by: 'صندوق‌دار', date: '۱۴۰۵/۰۸/۰۵', time: '۱۱:۴۰' },
    { id: 'm3',  productId: 'p1', variantId: 'v1a', kind: 'out',    qty: 2,  source: 'مصرف در ویزیت #۲۲۰۴', by: 'دکتر مرادی', date: '۱۴۰۵/۰۸/۰۷', time: '۱۶:۰۵' },
    { id: 'm4',  productId: 'p1', variantId: 'v1b', kind: 'adjust', qty: -4, source: 'اصلاح شمارش انبار', by: 'مدیر انبار', date: '۱۴۰۵/۰۸/۱۰', time: '۱۸:۳۰', note: 'مغایرت در انبارگردانی' },
    { id: 'm5',  productId: 'p1', variantId: 'v1c', kind: 'in',     qty: 12, source: 'خرید PO-14052033', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۱۴', time: '۱۰:۲۰' },

    // p2 — غذای گربه رویال کنین
    { id: 'm6',  productId: 'p2', variantId: 'v2b', kind: 'in',     qty: 30, source: 'خرید PO-14052008', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۰۹', time: '۱۳:۱۵' },
    { id: 'm7',  productId: 'p2', variantId: 'v2a', kind: 'out',    qty: 6,  source: 'فروش صندوق INV-1071', by: 'صندوق‌دار', date: '۱۴۰۵/۰۸/۱۱', time: '۰۹:۵۰' },
    { id: 'm8',  productId: 'p2', variantId: 'v2d', kind: 'out',    qty: 1,  source: 'فروش صندوق INV-1080', by: 'صندوق‌دار', date: '۱۴۰۵/۰۸/۱۲', time: '۱۷:۲۵' },

    // p3 — واکسن هاری (single)
    { id: 'm9',  productId: 'p3', variantId: 'v3a', kind: 'in',     qty: 15, source: 'خرید PO-14052001', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۰۲', time: '۰۹:۱۲' },
    { id: 'm10', productId: 'p3', variantId: 'v3a', kind: 'out',    qty: 8,  source: 'واکسیناسیون گروهی', by: 'دکتر رحیمی', date: '۱۴۰۵/۰۸/۰۶', time: '۱۴:۰۰' },
    { id: 'm11', productId: 'p3', variantId: 'v3a', kind: 'out',    qty: 3,  source: 'مصرف در ویزیت #۲۲۳۰', by: 'دکتر مرادی', date: '۱۴۰۵/۰۸/۱۳', time: '۱۰:۳۰' },

    // p5 — شربت آموکسی‌سیلین
    { id: 'm12', productId: 'p5', variantId: 'v5b', kind: 'in',     qty: 10, source: 'خرید PO-14052001', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۰۲', time: '۰۹:۱۲' },
    { id: 'm13', productId: 'p5', variantId: 'v5a', kind: 'out',    qty: 4,  source: 'فروش صندوق INV-1065', by: 'صندوق‌دار', date: '۱۴۰۵/۰۸/۰۸', time: '۱۲:۱۰' },

    // p7 — سرنگ یک‌بار مصرف
    { id: 'm14', productId: 'p7', variantId: 'v7a', kind: 'in',     qty: 500, source: 'خرید PO-14052012', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۱۳', time: '۰۸:۳۰' },
    { id: 'm15', productId: 'p7', variantId: 'v7c', kind: 'in',     qty: 200, source: 'خرید PO-14052012', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۱۳', time: '۰۸:۳۰' },
    { id: 'm16', productId: 'p7', variantId: 'v7a', kind: 'out',    qty: 40,  source: 'مصرف درمانگاه', by: 'پرستار', date: '۱۴۰۵/۰۸/۱۵', time: '۱۹:۰۰' },

    // p8 — ویتامین B کمپلکس (single)
    { id: 'm17', productId: 'p8', variantId: 'v8a', kind: 'in',     qty: 20, source: 'خرید PO-14052015', by: 'انبار مرکزی', date: '۱۴۰۵/۰۸/۱۶', time: '۱۱:۰۰' },
    { id: 'm18', productId: 'p8', variantId: 'v8a', kind: 'out',    qty: 5,  source: 'فروش صندوق INV-1090', by: 'صندوق‌دار', date: '۱۴۰۵/۰۸/۱۸', time: '۱۵:۴۵' },
  ];

  // --- helpers -----------------------------------------------------------
  const catOf = id => CATS.find(c => c.id === id) || CATS[0];
  const supOf = id => SUPPLIERS.find(s => s.id === id) || {};
  const totalStock = p => p.variants.reduce((s, v) => s + v.stock, 0);
  const totalValue = p => p.variants.reduce((s, v) => s + v.stock * v.cost, 0);
  const minMaxPrice = p => {
    const ps = p.variants.map(v => v.price);
    return { min: Math.min(...ps), max: Math.max(...ps) };
  };
  const stockStatus = p => {
    const lowVariants = p.variants.filter(v => v.stock <= v.reorder);
    if (lowVariants.length === p.variants.length) return 'out';
    if (lowVariants.length > 0) return 'low';
    return 'ok';
  };
  const nearExpiryCount = () => {
    // In this mock, treat any 1405 variant as near expiry.
    let n = 0;
    PRODUCTS.forEach(p => p.variants.forEach(v => {
      if (v.expiry && /^۱۴۰[۴-۵]/.test(v.expiry)) n++;
    }));
    return n;
  };
  const lowStockCount = () => {
    let n = 0;
    PRODUCTS.forEach(p => p.variants.forEach(v => { if (v.stock <= v.reorder) n++; }));
    return n;
  };
  const inventoryValue = () => PRODUCTS.reduce((s, p) => s + totalValue(p), 0);

  // Movement helpers
  const movesOf = pid => MOVES.filter(m => m.productId === pid);
  const variantLabel = (pid, vid) => {
    const p = PRODUCTS.find(x => x.id === pid);
    const v = p && p.variants.find(x => x.id === vid);
    return v ? v.label : '';
  };
  // Log a new movement at the top of the ledger (in-memory only).
  const logMove = (pid, entry) => {
    MOVES.unshift(Object.assign({
      id: 'm_' + Math.random().toString(36).slice(2, 8),
      productId: pid,
      date: '۱۴۰۵/۰۸/۲۰', time: '—',
      by: 'شما', source: 'ثبت دستی',
    }, entry));
  };

  const formatIRR = n => {
    if (n == null) return '—';
    if (Math.abs(n) >= 1000000) return (n / 1000000).toFixed(1).replace(/\.0$/, '') + ' م';
    return n.toLocaleString('fa-IR');
  };
  const formatFull = n => (n == null ? '۰' : n.toLocaleString('fa-IR'));

  // Expose
  window.INV = {
    CATS, SUPPLIERS, PRODUCTS, POS, MOVES,
    catOf, supOf,
    totalStock, totalValue, minMaxPrice, stockStatus,
    nearExpiryCount, lowStockCount, inventoryValue,
    movesOf, variantLabel, logMove,
    formatIRR, formatFull,
  };
})();
