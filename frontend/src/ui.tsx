import { createContext, ReactNode, useCallback, useContext, useRef, useState } from "react";
import { ApiError } from "./api";

// ---------- Toast ----------

type ToastKind = "ok" | "error" | "info";
interface ToastState {
  text: string;
  kind: ToastKind;
  id: number;
}

const ToastContext = createContext<(text: string, kind?: ToastKind) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<ToastState | null>(null);
  const timer = useRef<number>();

  const show = useCallback((text: string, kind: ToastKind = "info") => {
    window.clearTimeout(timer.current);
    setToast({ text, kind, id: Date.now() });
    timer.current = window.setTimeout(() => setToast(null), 3200);
  }, []);

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div className={`toast${toast ? " show" : ""} ${toast?.kind ?? ""}`} role="status" aria-live="polite">
        {toast?.text}
      </div>
    </ToastContext.Provider>
  );
}

export const useToast = () => useContext(ToastContext);

/** Ejecuta una acción mostrando el error de la API como toast. */
export function useAction() {
  const toast = useToast();
  return useCallback(
    async <T,>(fn: () => Promise<T>, success?: (result: T) => string | undefined) => {
      try {
        const result = await fn();
        const msg = success?.(result);
        if (msg) toast(msg, "ok");
        return result;
      } catch (e) {
        toast(e instanceof ApiError ? e.message : "Algo salió mal.", "error");
        return undefined;
      }
    },
    [toast],
  );
}

// ---------- Formato ----------

export const hhmm = (iso: string) =>
  new Date(iso).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });

export const hhmmss = (iso: string) => new Date(iso).toLocaleTimeString("es-AR", { hour12: false });

export const secs = (ms: number) => `${(ms / 1000).toFixed(1).replace(".", ",")} s`;

export function Pill({ kind, children }: { kind: "ok" | "low" | "out" | "in" | "gold"; children: ReactNode }) {
  return <span className={`pill ${kind}`}>{children}</span>;
}

export const stockPill = (state: "ok" | "low" | "out") =>
  state === "ok" ? <Pill kind="ok">OK</Pill> : state === "low" ? <Pill kind="low">Bajo</Pill> : <Pill kind="out">Sin stock</Pill>;
