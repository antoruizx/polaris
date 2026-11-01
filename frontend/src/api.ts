// Tipos y llamadas a la API de Polaris. Los nombres siguen el JSON del backend (camelCase).
// La API omite los campos null, por eso muchos son opcionales.

export type StockState = "ok" | "low" | "out";

export interface ProductLocation {
  compartmentCode: string;
  quantity: number;
}

export interface Product {
  id: string;
  sku: string;
  name: string;
  category: string;
  stock: number;
  minStock: number;
  stockState: StockState;
  locations: ProductLocation[];
}

export interface Compartment {
  code: string;
  rack: string;
  level: number;
  slot: number;
  deviceId: string;
  ledIndex: number;
  productSku?: string;
  productName?: string;
  quantity: number;
}

export type PickStatus = "queued" | "lit" | "confirmed" | "cancelled" | "expired";

export interface Pick {
  id: string;
  productSku: string;
  productName: string;
  compartmentCode: string;
  deviceId: string;
  ledIndex: number;
  quantity: number;
  status: PickStatus;
  createdAt: string;
  litAt?: string;
  closedAt?: string;
  source?: "web" | "button";
  durationMs?: number;
  remainingStock: number;
}

export interface Operation {
  id: string;
  at: string;
  type: "withdrawal" | "restock";
  productSku: string;
  productName: string;
  compartmentCode: string;
  quantity: number;
  durationMs?: number;
}

export type ReservationStatus = "waiting" | "assigned" | "fulfilled" | "expired" | "cancelled";

export interface Reservation {
  id: string;
  number: number;
  label: string;
  status: ReservationStatus;
  spotCode?: string;
  createdAt: string;
  assignedAt?: string;
  expiresAt?: string;
  closedAt?: string;
}

export type SpotState = "free" | "occupied" | "reserved" | "outOfService";

export interface Spot {
  code: string;
  row: number;
  order: number;
  state: SpotState;
  ledIndex: number;
  sensorChannel: number;
  distanceCm?: number;
  lastChangeAt?: string;
  reservation?: Reservation;
}

export interface ParkingMetrics {
  date: string;
  total: number;
  occupied: number;
  reserved: number;
  free: number;
  waiting: number;
  occupancyPercent: number;
  arrivalsToday: number;
  entriesToday: number;
  exitsToday: number;
}

export interface Device {
  id: string;
  kind: "logistics" | "parking";
  name: string;
  online: boolean;
  lastSeenAt?: string;
  firmware?: string;
  ip?: string;
  rssi?: number;
}

export interface Traffic {
  at: string;
  source: string;
  method: string;
  path: string;
  status: number;
  summary?: string;
}

/** Error con el "code" estable que devuelve la API (ProblemDetails). */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly code: string | undefined,
    public readonly status: number,
  ) {
    super(message);
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      method,
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError("No se pudo conectar con la API. ¿Está corriendo iniciar-polaris.bat?", "network", 0);
  }

  if (!res.ok) {
    let message = `Error ${res.status}`;
    let code: string | undefined;
    try {
      const problem = await res.json();
      message = problem.title ?? message;
      code = problem.code;
    } catch {
      // respuesta sin cuerpo JSON
    }
    throw new ApiError(message, code, res.status);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export const api = {
  products: (q?: string) =>
    request<Product[]>("GET", `/api/logistics/products${q ? `?q=${encodeURIComponent(q)}` : ""}`),
  compartments: () => request<Compartment[]>("GET", "/api/logistics/compartments"),
  openPicks: () => request<Pick[]>("GET", "/api/logistics/picks/open"),
  startPick: (productSku: string, quantity: number) =>
    request<Pick>("POST", "/api/logistics/picks", { productSku, quantity }),
  confirmPick: (id: string, quantity?: number) =>
    request<Pick>("POST", `/api/logistics/picks/${id}/confirm`, { quantity }),
  cancelPick: (id: string) => request<Pick>("POST", `/api/logistics/picks/${id}/cancel`),
  restock: (productSku: string, quantity: number) =>
    request<Product>("POST", "/api/logistics/restocks", { productSku, quantity }),
  operations: (limit = 30) => request<Operation[]>("GET", `/api/logistics/operations?limit=${limit}`),

  spots: () => request<Spot[]>("GET", "/api/parking/spots"),
  queue: () => request<Reservation[]>("GET", "/api/parking/reservations"),
  arrive: () => request<Reservation>("POST", "/api/parking/arrivals"),
  cancelReservation: (id: string) => request<Reservation>("DELETE", `/api/parking/reservations/${id}`),
  metrics: () => request<ParkingMetrics>("GET", "/api/parking/metrics"),

  devices: () => request<Device[]>("GET", "/api/devices"),
};
