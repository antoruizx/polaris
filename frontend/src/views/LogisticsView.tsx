import { useCallback, useEffect, useMemo, useState } from "react";
import { api, Compartment, Operation, Pick, Product } from "../api";
import { useHubEvent, useOnReconnect } from "../realtime";
import { hhmm, Pill, secs, stockPill, useAction, useToast } from "../ui";

const normalize = (s: string) =>
  s
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase();

const isOpen = (p: Pick) => p.status === "lit" || p.status === "queued";

export function LogisticsView() {
  const [products, setProducts] = useState<Product[]>([]);
  const [compartments, setCompartments] = useState<Compartment[]>([]);
  const [picks, setPicks] = useState<Pick[]>([]);
  const [ops, setOps] = useState<Operation[]>([]);
  const [query, setQuery] = useState("");
  const [qty, setQty] = useState(1);
  const [flash, setFlash] = useState<string | null>(null);
  const [now, setNow] = useState(Date.now());
  const run = useAction();
  const toast = useToast();

  const loadAll = useCallback(async () => {
    try {
      const [p, c, o, ops] = await Promise.all([api.products(), api.compartments(), api.openPicks(), api.operations()]);
      setProducts(p);
      setCompartments(c);
      setPicks(o);
      setOps(ops);
    } catch {
      // el banner de conexión ya avisa
    }
  }, []);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);
  useOnReconnect(loadAll);

  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 100);
    return () => window.clearInterval(t);
  }, []);

  // ---------- Tiempo real ----------

  useHubEvent<Pick>("PickChanged", (pick) => {
    setPicks((list) => {
      const rest = list.filter((p) => p.id !== pick.id);
      return isOpen(pick) ? [...rest, pick].sort((a, b) => a.createdAt.localeCompare(b.createdAt)) : rest;
    });

    if (pick.status === "lit") setQty(1);

    if (pick.status === "confirmed") {
      setFlash(pick.compartmentCode);
      window.setTimeout(() => setFlash((f) => (f === pick.compartmentCode ? null : f)), 1600);
      toast(
        `Retiro registrado: ${pick.quantity} × ${pick.productName}` +
          (pick.durationMs != null ? ` · ${secs(pick.durationMs)}` : "") +
          (pick.source === "button" ? " · botón físico" : "") +
          ` · quedan ${pick.remainingStock}`,
        "ok",
      );
      void api.operations().then(setOps).catch(() => {});
    }
    if (pick.status === "expired") toast(`Se apagó la luz de ${pick.compartmentCode}: nadie confirmó a tiempo.`, "info");
  });

  useHubEvent<Product>("StockChanged", (product) => {
    setProducts((list) => list.map((p) => (p.id === product.id ? product : p)));
    void api.compartments().then(setCompartments).catch(() => {});
    void api.operations().then(setOps).catch(() => {});
  });

  // ---------- Derivados ----------

  const results = useMemo(() => {
    const q = normalize(query.trim());
    return q ? products.filter((p) => normalize(`${p.name} ${p.sku} ${p.category}`).includes(q)) : products;
  }, [products, query]);

  const lit = picks.find((p) => p.status === "lit") ?? null;
  const queued = picks.filter((p) => p.status === "queued");
  const levels = useMemo(() => {
    const byLevel = new Map<number, Compartment[]>();
    for (const c of compartments) byLevel.set(c.level, [...(byLevel.get(c.level) ?? []), c]);
    return [...byLevel.entries()].sort(([a], [b]) => a - b);
  }, [compartments]);
  const maxStock = Math.max(1, ...products.map((p) => p.stock));

  // ---------- Acciones ----------

  // Aplica la respuesta de la API sin esperar al evento de SignalR (si llega después, es idempotente).
  const applyPick = (pick?: Pick) => {
    if (!pick) return;
    setPicks((list) => {
      const rest = list.filter((p) => p.id !== pick.id);
      return isOpen(pick) ? [...rest, pick].sort((a, b) => a.createdAt.localeCompare(b.createdAt)) : rest;
    });
  };

  const locate = (sku: string) =>
    run(
      async () => {
        const pick = await api.startPick(sku, 1);
        applyPick(pick);
        return pick;
      },
      (pick) =>
        pick.status === "queued"
          ? `${pick.productName} quedó en cola: hay otro retiro encendido.`
          : `Luz encendida en ${pick.compartmentCode}.`,
    );

  const onCellClick = (c: Compartment) => {
    if (!c.productSku) {
      toast("Ese compartimento está vacío.", "info");
      return;
    }
    void locate(c.productSku);
  };

  const elapsed = lit?.litAt ? now - new Date(lit.litAt).getTime() : 0;
  const litStock = lit ? products.find((p) => p.sku === lit.productSku)?.stock ?? lit.remainingStock : 0;

  return (
    <section className="view">
      <div className="sec-head">
        <div>
          <p className="eyebrow">Módulo principal</p>
          <h2 className="sec">Buscar un producto</h2>
          <p className="lede">
            Buscá el producto, se enciende la luz de su compartimento, retirás y confirmás con el botón. Si la
            estantería ya tiene una luz encendida, el pedido espera su turno.
          </p>
        </div>
      </div>

      <div className="log-grid">
        <div className="panel">
          <h3>Buscador</h3>
          <p className="sub">Por nombre, código o categoría.</p>
          <div className="search">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <circle cx="11" cy="11" r="7" />
              <path d="m20 20-3.5-3.5" />
            </svg>
            <input
              type="search"
              placeholder="Ej: pilas, cinta, PL-014…"
              autoComplete="off"
              aria-label="Buscar producto"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && results[0]) void locate(results[0].sku);
              }}
            />
          </div>
          <ul className="results">
            {results.length === 0 && <li className="empty">No hay productos que coincidan con “{query}”.</li>}
            {results.map((p) => (
              <li key={p.id}>
                <button className={`res${lit?.productSku === p.sku ? " sel" : ""}`} onClick={() => void locate(p.sku)}>
                  <span className="n">{p.name}</span>
                  <span className="loc">{p.locations[0]?.compartmentCode ?? "—"}</span>
                  <span className="m">
                    {p.sku} · {p.category}
                  </span>
                  <Pill kind={p.stockState}>{p.stock} u.</Pill>
                </button>
              </li>
            ))}
          </ul>

          <div className={`active-card${lit ? " on" : ""}`}>
            {!lit ? (
              <div className="idle">
                <svg viewBox="0 0 100 100" aria-hidden="true">
                  <path
                    fill="#f4d088"
                    d="M50 2 C53 36 64 47 98 50 C64 53 53 64 50 98 C47 64 36 53 2 50 C36 47 47 36 50 2Z"
                  />
                </svg>
                <span>Elegí un producto de la lista: se enciende la luz de su compartimento en la estantería.</span>
              </div>
            ) : (
              <>
                <div className="ac-top">
                  <div>
                    <Pill kind="gold">LED #{lit.ledIndex} encendido</Pill>
                    <div className="ac-name">{lit.productName}</div>
                    <div className="ac-meta">
                      Compartimento {lit.compartmentCode} · stock {litStock}
                    </div>
                  </div>
                  <div className="timer">{secs(elapsed)}</div>
                </div>
                <div className="ac-row">
                  <div className="qty">
                    <button aria-label="Menos" onClick={() => setQty((q) => Math.max(1, q - 1))}>
                      −
                    </button>
                    <span>{qty}</span>
                    <button aria-label="Más" onClick={() => setQty((q) => Math.min(Math.max(litStock, 1), q + 1))}>
                      +
                    </button>
                  </div>
                  <button className="btn primary grow" onClick={() => void run(() => api.confirmPick(lit.id, qty)).then(applyPick)}>
                    Confirmar retiro
                  </button>
                  <button className="btn" onClick={() => void run(() => api.cancelPick(lit.id)).then(applyPick)}>
                    Apagar
                  </button>
                </div>
              </>
            )}
          </div>

          {queued.length > 0 && (
            <div className="queue">
              <h4>En cola ({queued.length})</h4>
              <ol>
                {queued.map((p) => (
                  <li key={p.id}>
                    <span>
                      {p.productName} <small>{p.compartmentCode}</small>
                    </span>
                    <button className="btn sm" onClick={() => void run(() => api.cancelPick(p.id)).then(applyPick)}>
                      Quitar
                    </button>
                  </li>
                ))}
              </ol>
            </div>
          )}
        </div>

        <div className={`rig${lit || flash ? " focus" : ""}`}>
          <div className="rig-head">
            <h3>
              Estantería B <Pill kind="gold">{compartments.length} compartimentos · 1 LED c/u</Pill>
            </h3>
            <span className="legend">
              <span>
                <i style={{ background: "var(--gold)" }} />
                Encendido
              </span>
              <span>
                <i style={{ background: "var(--free)" }} />
                Retiro confirmado
              </span>
            </span>
          </div>
          <div className="levels">
            {levels.map(([level, cells]) => (
              <div className="shelf" key={level}>
                <div className="shelf-h">
                  <span>NIVEL {String(level).padStart(2, "0")}</span>
                  <span>
                    {cells[0]?.code}…{cells[cells.length - 1]?.code}
                  </span>
                </div>
                <div className="cells">
                  {cells.map((c) => {
                    const cls = lit?.compartmentCode === c.code ? " lit" : flash === c.code ? " done" : "";
                    return (
                      <button
                        key={c.code}
                        className={`cell${cls}${c.productSku ? "" : " vacant"}`}
                        aria-label={`Compartimento ${c.code}: ${c.productName ?? "vacío"}`}
                        onClick={() => onCellClick(c)}
                      >
                        <span className="led" />
                        <span className="code">{c.code}</span>
                        <span className="pn">{c.productName ?? "vacío"}</span>
                      </button>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
          <div className="rig-foot">
            <span className={`hw-ind${lit ? " ready" : ""}`} aria-hidden="true" />
            <span>
              {lit
                ? `Retirá ${qty} u. de ${lit.compartmentCode} y apretá el botón físico de la estantería (o “Confirmar retiro”).`
                : "El botón físico de la estantería se habilita cuando hay un compartimento encendido."}
            </span>
          </div>
        </div>
      </div>

      <div className="two">
        <div className="panel">
          <h3>Inventario</h3>
          <p className="sub">Stock por producto. Debajo del mínimo se marca como bajo.</p>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th>Producto</th>
                  <th>Compartimento</th>
                  <th>Stock</th>
                  <th>Estado</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {products.map((p) => (
                  <tr key={p.id}>
                    <td>
                      <div className="strong">{p.name}</div>
                      <div className="m small">
                        {p.sku} · {p.category}
                      </div>
                    </td>
                    <td className="m">{p.locations[0]?.compartmentCode ?? "—"}</td>
                    <td>
                      <div className="stockbar">
                        <span className="b">
                          <i
                            style={{
                              width: `${Math.round((p.stock / maxStock) * 100)}%`,
                              background:
                                p.stockState === "ok" ? "var(--muted)" : p.stockState === "low" ? "var(--warn)" : "var(--busy)",
                            }}
                          />
                        </span>
                        <span className="v">{p.stock}</span>
                      </div>
                    </td>
                    <td>{stockPill(p.stockState)}</td>
                    <td className="nowrap">
                      <button className="btn sm" onClick={() => void locate(p.sku)} disabled={p.stock === 0}>
                        Ubicar
                      </button>{" "}
                      <button
                        className="btn sm"
                        title="Reponer 10 unidades"
                        onClick={() => void run(() => api.restock(p.sku, 10), (r) => `Repuesto: +10 ${r.name}`)}
                      >
                        +10
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div className="panel">
          <h3>Registro de operaciones</h3>
          <p className="sub">Cada retiro guarda cuánto tardó, desde que se encendió la luz hasta la confirmación.</p>
          <ul className="ops">
            {ops.length === 0 && <li className="empty">Todavía no hay operaciones.</li>}
            {ops.map((o) => (
              <li key={o.id}>
                <span className="t">{hhmm(o.at)}</span>
                <span className="d">
                  <Pill kind={o.type === "withdrawal" ? "gold" : "in"}>{o.type === "withdrawal" ? "Retiro" : "Reposición"}</Pill>{" "}
                  {o.quantity} × {o.productName}
                  <small>
                    {o.compartmentCode} · stock {o.type === "withdrawal" ? "−" : "+"}
                    {o.quantity}
                  </small>
                </span>
                <span className="s">{o.durationMs != null ? secs(o.durationMs) : "—"}</span>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}
