import { ReactNode, useEffect, useMemo, useRef, useState } from "react";
import type { Compartment, Pick, Product, Reservation, Spot } from "../api";
import { ParkingScene, PARKING_VIEWS } from "./ParkingScene";
import type { View } from "./stage";
import { WarehouseScene, WAREHOUSE_VIEWS } from "./WarehouseScene";

/** Monta una escena de Three.js dentro de un div y la destruye al desmontar. */
function useScene<T extends { dispose(): void }>(create: (host: HTMLDivElement) => T) {
  const host = useRef<HTMLDivElement>(null);
  const [scene, setScene] = useState<T | null>(null);
  useEffect(() => {
    if (!host.current) return;
    let s: T;
    try {
      s = create(host.current);
    } catch {
      return; // sin WebGL: la vista sigue funcionando con los paneles
    }
    setScene(s);
    return () => {
      s.dispose();
      setScene(null);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return { host, scene };
}

function CamBar({ views, active, onPick, extra }: { views: [string, string][]; active: string; onPick: (id: string) => void; extra?: ReactNode }) {
  return (
    <div className="cam-bar">
      {views.map(([id, text]) => (
        <button key={id} className={`cam${active === id ? " on" : ""}`} onClick={() => onPick(id)}>
          {text}
        </button>
      ))}
      {extra}
    </div>
  );
}

// ---------- Depósito ----------

interface WarehouseProps {
  compartments: Compartment[];
  products: Product[];
  lit: Pick | null;
  flash: string | null;
  onDelivered: (pickId: string) => void;
  onCellClick: (code: string) => void;
  onStatus: (text: string) => void;
}

export function Warehouse3D(props: WarehouseProps) {
  const { host, scene } = useScene((el) => new WarehouseScene(el));
  const [cam, setCam] = useState("general");
  const [status, setStatus] = useState("Listo para el próximo pedido");
  const cb = useRef(props);
  cb.current = props;

  const categories = useMemo(() => new Map(props.products.map((p) => [p.sku, p.category])), [props.products]);

  useEffect(() => {
    if (!scene) return;
    scene.onDelivered = (id) => cb.current.onDelivered(id);
    scene.onCellClick = (code) => cb.current.onCellClick(code);
    scene.onStatus = (text) => {
      setStatus(text);
      cb.current.onStatus(text);
    };
  }, [scene]);

  useEffect(() => {
    scene?.setCompartments(props.compartments, categories);
    scene?.setLit(props.lit?.compartmentCode ?? null, props.flash);
  }, [scene, props.compartments, categories]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    scene?.setLit(props.lit?.compartmentCode ?? null, props.flash);
  }, [scene, props.lit?.compartmentCode, props.flash]); // eslint-disable-line react-hooks/exhaustive-deps

  // Cada pedido encendido manda al operario a buscarlo; si se cierra por otro lado, vuelve.
  const lit = props.lit;
  useEffect(() => {
    if (!scene || !lit) return;
    scene.dispatch({ pickId: lit.id, code: lit.compartmentCode, productName: lit.productName });
    return () => scene.abort(lit.id);
  }, [scene, lit?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const pickCam = (id: string) => {
    setCam(id);
    if (!scene) return;
    if (id === "follow") {
      const w = scene.workerPosition;
      scene.stage.goTo({ pos: [w.x + 3, 3.4, w.z + 5], target: w.toArray() as View["target"] }, () => scene.workerPosition);
    } else scene.stage.goTo(WAREHOUSE_VIEWS[id as keyof typeof WAREHOUSE_VIEWS]);
  };

  return (
    <div className="stage3d">
      <div ref={host} className="stage-host" />
      <CamBar
        views={[
          ["general", "Vista general"],
          ["follow", "Seguir operario"],
          ["rack", "Estantería"],
          ["top", "Desde arriba"],
        ]}
        active={cam}
        onPick={pickCam}
      />
      <div className="hud-status">
        <span className={`hud-dot${props.lit ? " busy" : ""}`} />
        <b>Operario</b> {status}
      </div>
      <div className="hud-hint">Arrastrá para girar · rueda para acercar · clic en un compartimento para pedirlo</div>
    </div>
  );
}

// ---------- Estacionamiento ----------

interface ParkingProps {
  spots: Spot[];
  queue: Reservation[];
  onParked: (code: string) => void;
  onSpotClick: (code: string) => void;
}

export function Parking3D(props: ParkingProps) {
  const { host, scene } = useScene((el) => new ParkingScene(el));
  const [cam, setCam] = useState("general");
  const cb = useRef(props);
  cb.current = props;

  useEffect(() => {
    if (!scene) return;
    scene.onParked = (code) => cb.current.onParked(code);
    scene.onSpotClick = (code) => cb.current.onSpotClick(code);
  }, [scene]);

  useEffect(() => {
    scene?.sync(props.spots, props.queue);
  }, [scene, props.spots, props.queue]);

  return (
    <div className="stage3d">
      <div ref={host} className="stage-host" />
      <CamBar
        views={[
          ["general", "Vista general"],
          ["entrance", "Entrada"],
          ["top", "Desde arriba"],
        ]}
        active={cam}
        onPick={(id) => {
          setCam(id);
          scene?.stage.goTo(PARKING_VIEWS[id as keyof typeof PARKING_VIEWS]);
        }}
      />
      <div className="hud-hint">Arrastrá para girar · clic en un auto estacionado para que salga</div>
    </div>
  );
}
