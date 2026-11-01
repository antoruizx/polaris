import { useEffect, useState } from "react";
import { useConnectionStatus } from "./realtime";
import { ToastProvider } from "./ui";
import { LogisticsView } from "./views/LogisticsView";
import { ParkingView } from "./views/ParkingView";
import { DevicesView } from "./views/DevicesView";

const VIEWS = [
  { id: "logistica", label: "Logística", k: "01" },
  { id: "parking", label: "Estacionamiento", k: "02" },
  { id: "dispositivos", label: "Dispositivos", k: "03" },
] as const;
type ViewId = (typeof VIEWS)[number]["id"];

const initialView = (): ViewId => {
  const h = window.location.hash.replace("#", "");
  return (VIEWS.find((v) => v.id === h)?.id ?? "logistica") as ViewId;
};

function Countdown() {
  const event = new Date(2026, 10, 7);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const days = Math.ceil((event.getTime() - today.getTime()) / 864e5);
  const text = days > 1 ? `faltan ${days} días` : days === 1 ? "¡es mañana!" : days === 0 ? "¡es hoy!" : "realizado";
  return <span>{text}</span>;
}

function Logo() {
  return (
    <h1 className="logo" aria-label="Polaris">
      P
      <svg viewBox="0 0 100 100" aria-hidden="true">
        <defs>
          <radialGradient id="sg" cx="50%" cy="50%" r="50%">
            <stop offset="0" stopColor="#fff7e2" />
            <stop offset=".55" stopColor="#f4d088" />
            <stop offset="1" stopColor="#d9a94a" />
          </radialGradient>
        </defs>
        <path fill="url(#sg)" d="M50 2 C53 36 64 47 98 50 C64 53 53 64 50 98 C47 64 36 53 2 50 C36 47 47 36 50 2Z" />
      </svg>
      LARIS
    </h1>
  );
}

export default function App() {
  const [view, setView] = useState<ViewId>(initialView);
  const status = useConnectionStatus();

  useEffect(() => {
    try {
      history.replaceState(null, "", `#${view}`);
    } catch {
      // sin historial (vista previa)
    }
  }, [view]);

  const statusText =
    status === "online"
      ? "Conectado en tiempo real"
      : status === "reconnecting"
        ? "Reconectando…"
        : status === "connecting"
          ? "Conectando…"
          : "Sin conexión con la API";

  return (
    <ToastProvider>
      <div className="wrap">
        <header className="top">
          <div className="brand">
            <Logo />
            <span className={`conn ${status}`}>
              <span className="live-dot" />
              {statusText}
            </span>
          </div>
          <p className="tag">
            Sistema inteligente de localización de productos y gestión de espacios mediante iluminación conectada. La
            luz señala el lugar exacto.
          </p>
          <div className="chips">
            <span className="chip">
              Módulo principal <b>Logística</b>
            </span>
            <span className="chip">
              Módulo secundario <b>Estacionamiento</b>
            </span>
            <span className="chip">
              Hardware <b>ESP32 + LEDs + sensores</b>
            </span>
            <span className="chip gold">
              FAPPE <b>sáb 7 nov</b> · <Countdown />
            </span>
          </div>
        </header>

        {status === "offline" && (
          <div className="banner">
            No hay conexión con la API. Abrí <code>iniciar-polaris.bat</code> y esperá a que diga “Now listening”. La
            página se reconecta sola.
          </div>
        )}

        <nav className="tabs" role="tablist" aria-label="Secciones">
          {VIEWS.map((v) => (
            <button
              key={v.id}
              className="tab"
              role="tab"
              aria-selected={view === v.id}
              onClick={() => setView(v.id)}
            >
              <span className="k">{v.k}</span>
              {v.label}
            </button>
          ))}
        </nav>

        {view === "logistica" && <LogisticsView />}
        {view === "parking" && <ParkingView />}
        {view === "dispositivos" && <DevicesView />}

        <footer>
          <span>Polaris · FAPPE 2026</span>
          <span>Datos reales desde la API · tiempo real por SignalR</span>
        </footer>
      </div>
    </ToastProvider>
  );
}
