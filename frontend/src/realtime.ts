import { useEffect, useRef, useState } from "react";
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

export type ConnectionStatus = "connecting" | "online" | "reconnecting" | "offline";

const GROUPS = ["logistics", "parking", "devices"];

let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;
const statusListeners = new Set<(s: ConnectionStatus) => void>();
let currentStatus: ConnectionStatus = "connecting";

function setStatus(s: ConnectionStatus) {
  currentStatus = s;
  statusListeners.forEach((l) => l(s));
}

async function subscribeAll(conn: HubConnection) {
  for (const g of GROUPS) await conn.invoke("Subscribe", g);
}

/** Una sola conexión SignalR para toda la app; se reconecta sola y se vuelve a suscribir. */
function getConnection(): HubConnection {
  if (connection) return connection;

  connection = new HubConnectionBuilder()
    .withUrl("/hubs/polaris")
    .withAutomaticReconnect([0, 1000, 2000, 5000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build();

  // Handlers vacíos para que SignalR no avise "No client method" cuando la pestaña que escucha no está abierta.
  for (const e of ["PickChanged", "StockChanged", "SpotChanged", "ReservationChanged", "DeviceStatusChanged", "DeviceTraffic"]) {
    connection.on(e, () => {});
  }

  connection.onreconnecting(() => setStatus("reconnecting"));
  connection.onreconnected(async () => {
    await subscribeAll(connection!);
    setStatus("online");
  });
  connection.onclose(() => {
    setStatus("offline");
    starting = null;
    setTimeout(() => void start(), 3000); // reintenta para siempre: la demo no puede quedar colgada
  });

  return connection;
}

async function start() {
  const conn = getConnection();
  if (conn.state !== HubConnectionState.Disconnected) return;
  if (starting) return starting;

  setStatus("connecting");
  starting = conn
    .start()
    .then(() => subscribeAll(conn))
    .then(() => setStatus("online"))
    .catch(() => {
      setStatus("offline");
      starting = null;
      setTimeout(() => void start(), 3000);
    });
  return starting;
}

export function useConnectionStatus(): ConnectionStatus {
  const [status, setLocal] = useState<ConnectionStatus>(currentStatus);
  useEffect(() => {
    statusListeners.add(setLocal);
    void start();
    return () => {
      statusListeners.delete(setLocal);
    };
  }, []);
  return status;
}

/** Escucha un evento del servidor (PickChanged, SpotChanged, …) mientras el componente está montado. */
export function useHubEvent<T>(event: string, handler: (payload: T) => void) {
  const ref = useRef(handler);
  ref.current = handler;

  useEffect(() => {
    const conn = getConnection();
    const listener = (payload: T) => ref.current(payload);
    conn.on(event, listener);
    void start();
    return () => conn.off(event, listener);
  }, [event]);
}

/** Avisa cada vez que la conexión vuelve: sirve para recargar datos que pudieron cambiar mientras estuvo caída. */
export function useOnReconnect(callback: () => void) {
  const ref = useRef(callback);
  ref.current = callback;
  useEffect(() => {
    const listener = (s: ConnectionStatus) => {
      if (s === "online") ref.current();
    };
    statusListeners.add(listener);
    return () => {
      statusListeners.delete(listener);
    };
  }, []);
}
