import { useCallback, useEffect, useRef, useState } from "react";
import { api, ParkingMetrics, Reservation, Spot } from "../api";
import { useHubEvent, useOnReconnect } from "../realtime";
import { Parking3D } from "../three/Scene3D";
import { hhmmss, Pill, useAction, useToast } from "../ui";

interface LogEntry {
  id: number;
  at: string;
  text: string;
  kind: "ok" | "out" | "in" | "gold";
}

const STATE_LABEL: Record<Spot["state"], string> = {
  free: "libre",
  occupied: "ocupada",
  reserved: "reservada",
  outOfService: "fuera de servicio",
};

let logSeq = 0;

export function ParkingView() {
  const [spots, setSpots] = useState<Spot[]>([]);
  const spotsRef = useRef<Spot[]>([]);
  spotsRef.current = spots;
  const [queue, setQueue] = useState<Reservation[]>([]);
  const [metrics, setMetrics] = useState<ParkingMetrics | null>(null);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [now, setNow] = useState(Date.now());
  const run = useAction();
  const toast = useToast();

  const loadAll = useCallback(async () => {
    try {
      const [s, q, m] = await Promise.all([api.spots(), api.queue(), api.metrics()]);
      setSpots(s);
      setQueue(q);
      setMetrics(m);
    } catch {
      // el banner de conexión ya avisa
    }
  }, []);

  useEffect(() => {
    void loadAll();
  }, [loadAll]);
  useOnReconnect(loadAll);

  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, []);

  const addLog = (text: string, kind: LogEntry["kind"]) =>
    setLog((l) => [{ id: ++logSeq, at: new Date().toISOString(), text, kind }, ...l].slice(0, 12));

  const refreshMetrics = () => void api.metrics().then(setMetrics).catch(() => {});

  // ---------- Tiempo real ----------

  useHubEvent<Spot>("SpotChanged", (spot) => {
    const prev = spotsRef.current.find((s) => s.code === spot.code);
    if (prev && prev.state !== spot.state) {
      if (spot.state === "occupied") addLog(`${spot.code} ocupada${spot.distanceCm != null ? ` · ${spot.distanceCm} cm` : ""}`, "out");
      else if (spot.state === "free" && prev.state === "occupied") addLog(`${spot.code} liberada`, "ok");
    }
    setSpots((list) => list.map((s) => (s.code === spot.code ? spot : s)));
    refreshMetrics();
  });

  useHubEvent<Reservation>("ReservationChanged", (r) => {
    setQueue((list) => {
      const rest = list.filter((x) => x.id !== r.id);
      const open = r.status === "waiting" || r.status === "assigned";
      return open ? [...rest, r].sort((a, b) => a.createdAt.localeCompare(b.createdAt)) : rest;
    });

    if (r.status === "waiting") addLog(`${r.label} llegó: estacionamiento completo, espera turno`, "gold");
    if (r.status === "assigned") addLog(`${r.label} → guiado a ${r.spotCode}`, "in");
    if (r.status === "fulfilled") addLog(`${r.label} estacionó en ${r.spotCode ?? "su plaza"}`, "ok");
    if (r.status === "expired") addLog(`${r.label} no llegó a ${r.spotCode}: reserva vencida`, "out");
    if (r.status === "cancelled") addLog(`${r.label} salió de la fila`, "out");
    refreshMetrics();
  });

  // ---------- Derivados ----------

  const waiting = queue.filter((r) => r.status === "waiting");
  const assigned = queue.filter((r) => r.status === "assigned");
  const free = spots.filter((s) => s.state === "free").length;
  const full = spots.length > 0 && (free === 0 || waiting.length > 0);
  const guiding = assigned[assigned.length - 1];

  // El auto 3D llegó a su plaza: se avisa a la API como si fuera el sensor ultrasónico.
  const onParked = (code: string) => void run(() => api.setOccupancy(code, true));

  const releaseSpot = (code: string) => {
    const spot = spotsRef.current.find((s) => s.code === code);
    if (spot?.state !== "occupied") {
      toast(`${code} está ${STATE_LABEL[spot?.state ?? "free"]}.`, "info");
      return;
    }
    void run(() => api.setOccupancy(code, false), () => `El auto de ${code} se va`);
  };
  const occupiedSpots = spots.filter((s) => s.state === "occupied");

  const remaining = (r: Reservation) => {
    if (!r.expiresAt) return "";
    const s = Math.max(0, Math.round((new Date(r.expiresAt).getTime() - now) / 1000));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
  };

  return (
    <section className="view">
      <div className="sec-head">
        <div>
          <p className="eyebrow">Módulo secundario</p>
          <h2 className="sec">Buscar un lugar</h2>
          <p className="lede">
            El sensor de la entrada detecta el auto y le reserva la plaza libre más cercana; su luz parpadea en celeste
            hasta que estaciona. Si no hay lugar, espera en la fila por orden de llegada.
          </p>
        </div>
        <span className="legend">
          <span>
            <i style={{ background: "var(--free)" }} />
            Libre
          </span>
          <span>
            <i style={{ background: "var(--busy)" }} />
            Ocupada
          </span>
          <span>
            <i style={{ background: "var(--guide)" }} />
            Reservada / guiando
          </span>
        </span>
      </div>

      <div className="park-grid">
        <Parking3D spots={spots} queue={queue} onParked={onParked} onSpotClick={releaseSpot} />

        <div>
          <div className={`sign${full ? " full" : ""}`}>
            <span className="h">LUGARES LIBRES</span>
            <span className="big">{full ? "COMPLETO" : String(free).padStart(2, "0")}</span>
            <span className="dir">{guiding?.spotCode ? `→ ${guiding.label}: dirigite a ${guiding.spotCode}` : ""}</span>
          </div>
          <div className="ctrl">
            <button
              className="btn primary"
              onClick={() =>
                void run(
                  () => api.arrive(),
                  (r) => (r.status === "assigned" ? `${r.label}: dirigite a ${r.spotCode}` : `${r.label} en la fila de espera`),
                )
              }
            >
              Simular auto en la entrada
            </button>
            {occupiedSpots.length > 0 && (
              <div className="exits">
                <span>Salida:</span>
                {occupiedSpots.map((s) => (
                  <button key={s.code} className="btn sm" onClick={() => releaseSpot(s.code)}>
                    {s.code}
                  </button>
                ))}
              </div>
            )}
          </div>
          <div className="stats">
            <div className="stat">
              <div className="l">Ocupadas</div>
              <div className="v">
                {metrics ? `${metrics.occupied}/${metrics.total}` : "—"}
              </div>
            </div>
            <div className="stat">
              <div className="l">Ocupación</div>
              <div className="v">{metrics ? `${metrics.occupancyPercent}%` : "—"}</div>
            </div>
            <div className="stat">
              <div className="l">Llegadas hoy</div>
              <div className="v">{metrics?.arrivalsToday ?? "—"}</div>
            </div>
            <div className="stat">
              <div className="l">Egresos hoy</div>
              <div className="v">{metrics?.exitsToday ?? "—"}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="two">
        <div className="panel">
          <h3>Fila por orden de llegada</h3>
          <p className="sub">Autos guiados a su plaza y autos esperando lugar.</p>
          {queue.length === 0 ? (
            <p className="empty">No hay autos en camino ni esperando.</p>
          ) : (
            <ul className="ops">
              {queue.map((r) => (
                <li key={r.id}>
                  <span className="t">{hhmmss(r.createdAt).slice(0, 5)}</span>
                  <span className="d">
                    {r.status === "assigned" ? <Pill kind="in">Guiando</Pill> : <Pill kind="gold">Esperando</Pill>}{" "}
                    {r.label}
                    <small>{r.status === "assigned" ? `plaza ${r.spotCode} · vence en ${remaining(r)}` : "espera que se libere una plaza"}</small>
                  </span>
                  <span className="s">
                    <button className="btn sm" onClick={() => void run(() => api.cancelReservation(r.id))}>
                      Quitar
                    </button>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="panel">
          <h3>Eventos de sensores</h3>
          <p className="sub">Lo que reporta el ESP32 del estacionamiento, en vivo.</p>
          <ul className="events">
            {log.length === 0 && <li className="empty">Esperando eventos…</li>}
            {log.map((e) => (
              <li key={e.id}>
                <span className="t">{hhmmss(e.at)}</span>
                <span>
                  <Pill kind={e.kind}>•</Pill> {e.text}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}
