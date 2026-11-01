// Estacionamiento 3D: las plazas con su luz, la barrera de la entrada y los autos que entran, estacionan y salen.
import * as THREE from "three";
import type { CSS2DObject } from "three/examples/jsm/renderers/CSS2DRenderer.js";
import type { Reservation, Spot } from "../api";
import { makeCar } from "./models";
import { box, C, glow, label, Stage, std, View } from "./stage";

const SPOT_W = 2.8;
const SPOT_D = 5;
const SPOT_Z = -2.5; // centro de la plaza
const IN_LANE = 2.3;
const OUT_LANE = 0.75;
const GATE_X = 8.6;
const FAR_X = 34;

const CAR_COLORS = [0xc9d1e6, 0xe2574c, 0x3b6fd8, 0xf2c14e, 0x8a93a8, 0x5aa36b, 0xa36bff];

export const PARKING_VIEWS = {
  general: { pos: [-1, 9.5, 13], target: [1.5, 0, -0.5] } as View,
  entrance: { pos: [16, 3.2, 7], target: [6, 0.8, 0] } as View,
  top: { pos: [1.01, 20, 1], target: [1, 0, -0.5] } as View,
};

const STATE_COLOR: Record<Spot["state"], number> = {
  free: C.free,
  occupied: C.busy,
  reserved: C.guide,
  outOfService: 0x5b6488,
};

const STATE_TEXT: Record<Spot["state"], string> = {
  free: "LIBRE",
  occupied: "OCUPADA",
  reserved: "RESERVADA",
  outOfService: "FUERA DE SERVICIO",
};

interface Seg {
  curve: THREE.Curve<THREE.Vector3>;
  reverse: boolean;
  speed: number;
}

interface Car {
  mesh: THREE.Group;
  mode: "waiting" | "arriving" | "parked" | "leaving";
  resId?: string;
  spot?: string;
  path: Seg[];
  seg: number;
  dist: number;
  fade: number;
}

interface SpotView {
  data: Spot;
  x: number;
  glow: THREE.MeshBasicMaterial;
  strip: THREE.MeshStandardMaterial;
  lamp: THREE.MeshStandardMaterial;
  tag: CSS2DObject;
}

const v = (x: number, z: number) => new THREE.Vector3(x, 0, z);
const curve = (...pts: THREE.Vector3[]) => (pts.length === 2 ? new THREE.LineCurve3(pts[0], pts[1]) : new THREE.CatmullRomCurve3(pts, false, "centripetal"));

export class ParkingScene {
  readonly stage: Stage;
  /** El auto terminó de estacionar en la plaza: hay que avisarle a la API (como lo haría el sensor). */
  onParked: (code: string) => void = () => {};
  onSpotClick: (code: string) => void = () => {};

  private spots = new Map<string, SpotView>();
  private cars: Car[] = [];
  private colorSeq = 0;
  private barrier: THREE.Group;
  private sign: CSS2DObject;
  private signMat: THREE.MeshStandardMaterial;
  private chevrons: THREE.Mesh[] = [];
  private guideX: number | null = null;

  constructor(host: HTMLElement) {
    this.stage = new Stage(host, PARKING_VIEWS.general);
    this.stage.addFloor(20);
    this.stage.onPick = (key) => key.startsWith("spot:") && this.onSpotClick(key.slice(5));
    const s = this.stage.scene;

    // Calle y pasillo.
    const asphalt = new THREE.Mesh(new THREE.PlaneGeometry(FAR_X + 10, 3.4), std(0x0c1124, { roughness: 0.95 }));
    asphalt.rotation.x = -Math.PI / 2;
    asphalt.position.set(FAR_X / 2 - 5, 0.006, (IN_LANE + OUT_LANE) / 2);
    asphalt.receiveShadow = true;
    s.add(asphalt);
    for (let x = -6; x < FAR_X; x += 1.6) s.add(box(0.7, 0.01, 0.06, glow(0x3a4a80, 1), x, 0.012, (IN_LANE + OUT_LANE) / 2, false));

    // Flechas que guían al auto hasta su plaza.
    const shape = new THREE.Shape();
    shape.moveTo(0.35, 0.45);
    shape.lineTo(-0.15, 0);
    shape.lineTo(0.35, -0.45);
    shape.lineTo(0.15, -0.45);
    shape.lineTo(-0.35, 0);
    shape.lineTo(0.15, 0.45);
    const chevGeo = new THREE.ShapeGeometry(shape);
    for (let x = GATE_X - 0.5; x > -7; x -= 0.9) {
      const m = new THREE.Mesh(chevGeo, new THREE.MeshBasicMaterial({ color: C.guide, transparent: true, opacity: 0, depthWrite: false }));
      m.rotation.x = -Math.PI / 2;
      m.position.set(x, 0.02, IN_LANE);
      s.add(m);
      this.chevrons.push(m);
    }

    // Barrera y cartel de la entrada.
    const post = std(C.steel, { metalness: 0.7, roughness: 0.3 });
    s.add(box(0.35, 1.1, 0.35, post, GATE_X, 0.55, IN_LANE + 1.25));
    this.barrier = new THREE.Group();
    this.barrier.position.set(GATE_X, 1.0, IN_LANE + 1.25);
    const arm = box(0.12, 0.12, 3.2, std(0xf2f4ff), 0, 0, -1.6);
    this.barrier.add(arm);
    for (let i = 0; i < 4; i++) this.barrier.add(box(0.13, 0.13, 0.35, glow(C.busy, 1.8), 0, 0, -0.4 - i * 0.8, false));
    s.add(this.barrier);

    s.add(box(0.18, 3.2, 0.18, post, GATE_X + 0.6, 1.6, -0.6));
    this.signMat = glow(C.free, 0.6);
    s.add(box(1.9, 1.0, 0.12, std(0x070b1a), GATE_X + 0.6, 3.3, -0.6));
    s.add(box(1.94, 1.04, 0.08, this.signMat, GATE_X + 0.6, 3.3, -0.66, false));
    this.sign = label("sign3d", "");
    this.sign.position.set(GATE_X + 0.6, 3.3, -0.5);
    s.add(this.sign);
    const entry = label("lbl3d", "ENTRADA ▸ sensor");
    entry.position.set(GATE_X + 1.6, 0.2, IN_LANE + 1.6);
    s.add(entry);

    this.stage.tick((dt, t) => this.update(dt, t));
  }

  private spotX(order: number, total: number) {
    // order 1 = la más cercana a la entrada (a la derecha).
    return ((total + 1) / 2 - order) * SPOT_W + 0.5;
  }

  private buildSpots(list: Spot[]) {
    const s = this.stage.scene;
    const line = glow(0x7d8bc4, 1.3);
    const total = list.length;
    const left = this.spotX(total, total) - SPOT_W / 2;
    const right = this.spotX(1, total) + SPOT_W / 2;

    // Pared trasera, cordón y columnas con anillos de neón.
    s.add(box(right - left + 1, 0.3, 0.3, std(0x111a38), (left + right) / 2, 0.15, SPOT_Z - SPOT_D / 2 - 0.5));
    s.add(box(right - left + 1, 0.03, 0.04, glow(C.gold, 2), (left + right) / 2, 0.32, SPOT_Z - SPOT_D / 2 - 0.34, false));
    for (const x of [left - 0.4, right + 0.4]) {
      s.add(box(0.4, 4, 0.4, std(0x1a2340, { metalness: 0.6 }), x, 2, SPOT_Z - SPOT_D / 2 - 0.5));
      s.add(box(0.46, 0.06, 0.46, glow(C.guide, 2.2), x, 3.2, SPOT_Z - SPOT_D / 2 - 0.5, false));
    }

    for (let i = 0; i <= total; i++) {
      const x = left + i * SPOT_W;
      s.add(box(0.08, 0.012, SPOT_D, line, x, 0.012, SPOT_Z, false));
    }

    for (const spot of list) {
      const x = this.spotX(spot.order, total);
      const glowMat = new THREE.MeshBasicMaterial({ color: C.free, transparent: true, opacity: 0.08, depthWrite: false });
      const pad = new THREE.Mesh(new THREE.PlaneGeometry(SPOT_W - 0.25, SPOT_D - 0.2), glowMat);
      pad.rotation.x = -Math.PI / 2;
      pad.position.set(x, 0.01, SPOT_Z);
      pad.userData.pick = `spot:${spot.code}`;
      s.add(pad);

      const strip = glow(C.free, 2.5);
      s.add(box(SPOT_W - 0.4, 0.03, 0.1, strip, x, 0.02, SPOT_Z + SPOT_D / 2 - 0.08, false));

      // Poste con la luz de la plaza.
      const z = SPOT_Z - SPOT_D / 2 - 0.15;
      s.add(box(0.14, 1.2, 0.14, std(C.steel, { metalness: 0.7 }), x, 0.6, z));
      const lamp = glow(C.free, 3);
      const bulb = new THREE.Mesh(new THREE.SphereGeometry(0.17, 18, 14), lamp);
      bulb.position.set(x, 1.32, z);
      s.add(bulb);

      const tag = label("lbl3d spot", "");
      tag.position.set(x, 1.9, z);
      s.add(tag);

      this.spots.set(spot.code, { data: spot, x, glow: glowMat, strip, lamp, tag });
    }
  }

  // ---------- Sincronización con la API ----------

  sync(list: Spot[], queue: Reservation[]) {
    if (this.spots.size === 0 && list.length > 0) this.buildSpots(list);

    for (const spot of list) {
      const view = this.spots.get(spot.code);
      if (!view) continue;
      view.data = spot;
      view.tag.element.textContent = `${spot.code} · ${STATE_TEXT[spot.state]}${spot.reservation && spot.state === "reserved" ? ` · ${spot.reservation.label}` : ""}`;
      view.tag.element.dataset.state = spot.state;
    }

    const free = list.filter((s) => s.state === "free").length;
    const waiting = queue.filter((r) => r.status === "waiting");
    const full = list.length > 0 && (free === 0 || waiting.length > 0);
    this.sign.element.innerHTML = full ? "<small>LUGARES</small>COMPLETO" : `<small>LUGARES LIBRES</small>${String(free).padStart(2, "0")}`;
    this.sign.element.classList.toggle("full", full);
    this.signMat.emissive.setHex(full ? C.busy : C.free);

    // Autos esperando afuera, en fila.
    waiting.forEach((r, i) => {
      let car = this.cars.find((c) => c.resId === r.id);
      const slot = v(GATE_X + 3 + i * 4.6, IN_LANE);
      if (!car) {
        car = this.addCar("waiting", v(FAR_X, IN_LANE), -Math.PI / 2);
        car.resId = r.id;
      }
      if (car.mode === "waiting") this.drive(car, [{ curve: curve(car.mesh.position.clone(), slot), reverse: false, speed: 6 }]);
    });

    // Autos con plaza asignada: entran y van hasta su lugar.
    for (const r of queue) {
      if (r.status !== "assigned" || !r.spotCode) continue;
      const spot = this.spots.get(r.spotCode);
      if (!spot || spot.data.state === "occupied") continue;
      let car = this.cars.find((c) => c.resId === r.id);
      if (car && car.mode !== "waiting") continue;
      if (!car) {
        car = this.addCar("arriving", v(FAR_X, IN_LANE), -Math.PI / 2);
        car.resId = r.id;
      }
      car.mode = "arriving";
      car.spot = r.spotCode;
      const sx = spot.x;
      this.drive(car, [
        { curve: curve(car.mesh.position.clone(), v(sx + 3.4, IN_LANE)), reverse: false, speed: 7 },
        { curve: curve(v(sx + 3.4, IN_LANE), v(sx + 1.4, IN_LANE - 0.4), v(sx + 0.15, SPOT_Z + SPOT_D / 2 - 0.3), v(sx, SPOT_Z)), reverse: false, speed: 3 },
      ]);
    }

    // Autos que se fueron de la fila sin estacionar.
    for (const car of this.cars) {
      if (!car.resId || (car.mode !== "waiting" && car.mode !== "arriving")) continue;
      if (queue.some((r) => r.id === car.resId)) continue;
      const target = car.spot ? this.spots.get(car.spot) : undefined;
      if (car.mode === "arriving" && target?.data.state === "occupied") continue; // ya estacionó (lo vio el sensor)
      car.mode = "leaving";
      car.fade = 0.0001;
    }

    // Plazas ocupadas sin auto (ocupadas por el sensor real o al cargar la página) y plazas liberadas.
    for (const spot of this.spots.values()) {
      const here = this.cars.filter((c) => c.spot === spot.data.code && (c.mode === "parked" || c.mode === "arriving"));
      if (spot.data.state === "occupied" && here.length === 0) {
        const car = this.addCar("parked", v(spot.x, SPOT_Z), Math.PI);
        car.spot = spot.data.code;
        car.mesh.userData.pick = `spot:${spot.data.code}`;
      }
      if (spot.data.state === "free") {
        for (const car of here.filter((c) => c.mode === "parked")) this.leave(car, spot.x);
      }
    }
  }

  private addCar(mode: Car["mode"], at: THREE.Vector3, heading: number): Car {
    const mesh = makeCar(CAR_COLORS[this.colorSeq++ % CAR_COLORS.length]);
    mesh.position.copy(at);
    mesh.rotation.y = heading;
    this.stage.scene.add(mesh);
    const car: Car = { mesh, mode, path: [], seg: 0, dist: 0, fade: 0 };
    this.cars.push(car);
    return car;
  }

  private drive(car: Car, path: Seg[]) {
    car.path = path;
    car.seg = 0;
    car.dist = 0;
  }

  /** Sale marcha atrás al pasillo y se va por la salida. */
  private leave(car: Car, sx: number) {
    car.mode = "leaving";
    car.mesh.userData.pick = undefined;
    this.drive(car, [
      { curve: curve(v(sx, SPOT_Z), v(sx, SPOT_Z + SPOT_D / 2 - 0.4), v(sx - 1.0, OUT_LANE + 0.2), v(sx - 2.6, OUT_LANE)), reverse: true, speed: 2.6 },
      { curve: curve(v(sx - 2.6, OUT_LANE), v(sx, OUT_LANE), v(FAR_X, OUT_LANE)), reverse: false, speed: 7 },
    ]);
  }

  // ---------- Animación ----------

  private update(dt: number, t: number) {
    for (const car of [...this.cars]) {
      if (car.fade > 0) {
        car.fade += dt * 1.8;
        car.mesh.scale.setScalar(Math.max(0.01, 1 - car.fade));
        if (car.fade >= 1) this.remove(car);
        continue;
      }

      const seg = car.path[car.seg];
      if (!seg) continue;
      const len = seg.curve.getLength();
      const isLast = car.seg === car.path.length - 1;
      const left = len - car.dist;
      const speed = isLast ? Math.max(0.6, Math.min(seg.speed, left * 2.2)) : seg.speed;
      car.dist = Math.min(len, car.dist + speed * dt);
      const u = len === 0 ? 1 : car.dist / len;
      car.mesh.position.copy(seg.curve.getPointAt(u));
      const tan = seg.curve.getTangentAt(Math.min(u, 0.999));
      if (seg.reverse) tan.negate();
      if (tan.lengthSq() > 1e-6) car.mesh.rotation.y = Math.atan2(tan.x, tan.z);

      if (car.dist >= len - 1e-4) {
        car.seg++;
        car.dist = 0;
        if (car.seg >= car.path.length) this.arrived(car);
      }
    }

    // Barrera: se levanta si hay un auto cerca.
    const near = this.cars.some((c) => c.mode !== "parked" && Math.abs(c.mesh.position.x - GATE_X) < 3.2 && c.path[c.seg]);
    const goal = near ? Math.PI / 2.2 : 0;
    this.barrier.rotation.x += (goal - this.barrier.rotation.x) * (1 - Math.exp(-dt * 6));

    // Luces de las plazas.
    const blink = 0.5 + 0.5 * Math.sin(t * 7);
    this.guideX = null;
    for (const spot of this.spots.values()) {
      const color = STATE_COLOR[spot.data.state];
      const reserved = spot.data.state === "reserved";
      spot.glow.color.setHex(color);
      spot.glow.opacity = reserved ? 0.06 + blink * 0.16 : 0.08;
      spot.strip.emissive.setHex(color);
      spot.strip.emissiveIntensity = reserved ? 1 + blink * 3 : 2.5;
      spot.lamp.emissive.setHex(color);
      spot.lamp.emissiveIntensity = reserved ? 0.8 + blink * 4 : 3;
      if (reserved) this.guideX = Math.min(this.guideX ?? Infinity, spot.x);
    }

    // Flechas animadas desde la entrada hasta la plaza reservada.
    this.chevrons.forEach((m, i) => {
      const mat = m.material as THREE.MeshBasicMaterial;
      const on = this.guideX !== null && m.position.x > this.guideX + 2;
      const wave = 0.5 + 0.5 * Math.sin(t * 6 + i * 0.7);
      mat.opacity = on ? 0.15 + wave * 0.65 : Math.max(0, mat.opacity - dt * 2);
    });
  }

  private arrived(car: Car) {
    car.path = [];
    if (car.mode === "arriving" && car.spot) {
      car.mode = "parked";
      car.mesh.userData.pick = `spot:${car.spot}`;
      this.onParked(car.spot);
    } else if (car.mode === "leaving") {
      this.remove(car);
    }
  }

  private remove(car: Car) {
    this.stage.scene.remove(car.mesh);
    car.mesh.traverse((o) => {
      if (o instanceof THREE.Mesh) {
        o.geometry.dispose();
        (o.material as THREE.Material).dispose();
      }
    });
    this.cars = this.cars.filter((c) => c !== car);
  }

  dispose() {
    this.stage.dispose();
  }
}
