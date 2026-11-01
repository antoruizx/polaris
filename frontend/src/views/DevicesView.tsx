import { useCallback, useEffect, useState } from "react";
import { api, Device, Traffic } from "../api";
import { useHubEvent, useOnReconnect } from "../realtime";
import { hhmmss, Pill } from "../ui";

const ENDPOINTS: [string, string, string][] = [
  ["GET", "/api/logistics/products?q=", "Web"],
  ["POST", "/api/logistics/picks", "Web"],
  ["POST", "/api/logistics/picks/{id}/confirm", "Web"],
  ["GET", "/api/parking/spots", "Web"],
  ["POST", "/api/parking/arrivals", "Web / sensor entrada"],
  ["POST", "/api/devices/{id}/events", "ESP32"],
  ["GET", "/api/devices/{id}/desired-state", "ESP32 (cada 250 ms)"],
];

export function DevicesView() {
  const [devices, setDevices] = useState<Device[]>([]);
  const [traffic, setTraffic] = useState<(Traffic & { n: number })[]>([]);
  const [filter, setFilter] = useState<"all" | "web" | "devices">("all");
  const [paused, setPaused] = useState(false);

  const load = useCallback(async () => {
    try {
      setDevices(await api.devices());
    } catch {
      // el banner de conexión ya avisa
    }
  }, []);

  useEffect(() => {
    void load();
    const t = window.setInterval(() => void load(), 5000); // refresca "última señal"
    return () => window.clearInterval(t);
  }, [load]);
  useOnReconnect(load);

  useHubEvent<Device>("DeviceStatusChanged", (d) =>
    setDevices((list) => (list.some((x) => x.id === d.id) ? list.map((x) => (x.id === d.id ? d : x)) : [...list, d])),
  );

  useHubEvent<Traffic>("DeviceTraffic", (t) => {
    if (paused) return;
    setTraffic((list) => [...list, { ...t, n: Date.now() + Math.random() }].slice(-80));
  });

  const rows = traffic.filter((t) => filter === "all" || (filter === "web" ? t.source === "web" : t.source !== "web"));

  return (
    <section className="view">
      <div className="sec-head">
        <div>
          <p className="eyebrow">Cómo se conecta todo</p>
          <h2 className="sec">Dispositivos y API</h2>
          <p className="lede">
            Los ESP32 informan eventos (sensores, botón) y piden cada 250 ms el estado de sus luces. El simulador usa
            exactamente el mismo protocolo.
          </p>
        </div>
      </div>

      <div className="arch">
        <div className="node">
          <h4>
            Web app <Pill kind="ok">online</Pill>
          </h4>
          <p>React + TypeScript + SignalR.</p>
        </div>
        <div className="wire">
          <span className="lab">REST + SignalR</span>
        </div>
        <div className="node">
          <h4>
            API Polaris <Pill kind="ok">:5080</Pill>
          </h4>
          <p>ASP.NET Core + PostgreSQL.</p>
        </div>
        <div className="wire">
          <span className="lab">HTTP + JSON</span>
        </div>
        <div className="stack">
          {devices.map((d) => (
            <div className="node" key={d.id}>
              <h4>
                {d.name} {d.online ? <Pill kind="ok">online</Pill> : <Pill kind="out">offline</Pill>}
              </h4>
              <div className="kv">
                {d.id}
                {d.ip ? ` · ${d.ip}` : ""}
                {d.firmware ? ` · fw ${d.firmware}` : ""}
                {d.rssi != null ? ` · ${d.rssi} dBm` : ""}
              </div>
              <div className="kv">última señal: {d.lastSeenAt ? hhmmss(d.lastSeenAt) : "nunca"}</div>
            </div>
          ))}
          {devices.length === 0 && <div className="node">Sin dispositivos registrados.</div>}
        </div>
      </div>

      <div className="two" style={{ marginTop: 0 }}>
        <div className="panel">
          <div className="ch-bar">
            <div>
              <h3>Consola de tráfico</h3>
              <p className="sub" style={{ margin: 0 }}>
                Pedidos a la API en vivo (sin las consultas repetidas de las placas).
              </p>
            </div>
            <div className="filters">
              {(["all", "web", "devices"] as const).map((f) => (
                <button key={f} className="fbtn" aria-pressed={filter === f} onClick={() => setFilter(f)}>
                  {f === "all" ? "Todos" : f === "web" ? "Web" : "ESP32"}
                </button>
              ))}
              <button className="fbtn" aria-pressed={paused} onClick={() => setPaused((p) => !p)}>
                {paused ? "Reanudar" : "Pausar"}
              </button>
            </div>
          </div>
          <div className="console">
            {rows.length === 0 && <div className="ln">Esperando tráfico…</div>}
            {rows.map((l) => (
              <div className="ln" key={l.n}>
                <span className="ts">{hhmmss(l.at)}</span>
                <span className={`src ${l.source === "web" ? "web" : l.source.includes("park") ? "park" : "log"}`}>
                  {l.source}
                </span>
                <span className="meth">{l.method}</span>
                <span>{l.path}</span>
                <span className="st">{l.status}</span>
              </div>
            ))}
          </div>
        </div>
        <div className="panel">
          <h3>Contrato de la API</h3>
          <p className="sub">Documentación completa en /scalar.</p>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th />
                  <th>Endpoint</th>
                  <th>Quién lo usa</th>
                </tr>
              </thead>
              <tbody>
                {ENDPOINTS.map(([m, ep, who]) => (
                  <tr key={m + ep}>
                    <td>
                      <span className={`meth-tag ${m.toLowerCase()}`}>{m}</span>
                    </td>
                    <td>
                      <code className="ep">{ep}</code>
                    </td>
                    <td className="m">{who}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="sub" style={{ marginTop: 12 }}>
            <a href="http://localhost:5080/scalar" target="_blank" rel="noreferrer">
              Abrir la documentación interactiva →
            </a>
          </p>
        </div>
      </div>
    </section>
  );
}
