// Depósito 3D: la estantería B con un LED por compartimento y el operario que va a buscar los productos.
import * as THREE from "three";
import { CSS2DObject } from "three/examples/jsm/renderers/CSS2DRenderer.js";
import type { Compartment } from "../api";
import { makeWorker, WorkerModel } from "./models";
import { box, C, glow, label, lerpAngle, Stage, std, View } from "./stage";

const SLOT_W = 1.05;
const LEVEL_H = 0.85;
const BASE_Y = 0.32;
const RACK_Z = -3;
const HOME = new THREE.Vector3(3.3, 0, 0.9);
const HOME_HEADING = Math.PI / 2; // mira a la consola
const DESK_X = 4.5;
const DESK_DROP = new THREE.Vector3(DESK_X - 0.2, 0.93, 0.6);

const CATEGORY_COLORS: Record<string, number> = {
  Electricidad: 0xf2c14e,
  Ferretería: 0x8a93a8,
  Herramientas: 0xe2574c,
  Seguridad: 0x5aa36b,
  Embalaje: 0xc9a27a,
  Oficina: 0x3b6fd8,
  Tecnología: 0xa36bff,
};

export const WAREHOUSE_VIEWS = {
  general: { pos: [1.2, 4.3, 8.8], target: [1, 1.1, -1.2] } as View,
  rack: { pos: [0, 2.2, 3.6], target: [0, 1.3, -3] } as View,
  top: { pos: [0.01, 15, 1.5], target: [0, 0, -0.5] } as View,
};

export interface PickTask {
  pickId: string;
  code: string;
  productName: string;
}

type Phase = "idle" | "toShelf" | "reach" | "toBase" | "deliver" | "return";

interface Cell {
  data: Compartment;
  led: THREE.MeshStandardMaterial;
  crate: THREE.Mesh | null;
  pos: THREE.Vector3; // frente del compartimento, a la altura del estante
}

export class WarehouseScene {
  readonly stage: Stage;
  onDelivered: (pickId: string) => void = () => {};
  onCellClick: (code: string) => void = () => {};
  onStatus: (text: string) => void = () => {};

  private rack = new THREE.Group();
  private cells = new Map<string, Cell>();
  private lit: string | null = null;
  private flash: string | null = null;
  private beam: THREE.Mesh;
  private beamLight = new THREE.PointLight(C.gold, 0, 4, 1.5);
  private litLabel: CSS2DObject;

  private worker: WorkerModel;
  private bubble: CSS2DObject;
  private pos = HOME.clone();
  private heading = HOME_HEADING;
  private walk = 0;
  private phase: Phase = "idle";
  private timer = 0;
  private task: PickTask | null = null;
  private pending: PickTask[] = [];
  private delivered = false;
  private deskCrate: THREE.Mesh;
  private deskTimer = 0;

  constructor(host: HTMLElement) {
    this.stage = new Stage(host, WAREHOUSE_VIEWS.general);
    const s = this.stage.scene;
    this.stage.addFloor(14);
    this.stage.onPick = (key) => key.startsWith("cell:") && this.onCellClick(key.slice(5));

    this.buildRoom();
    s.add(this.rack);

    // Haz de luz que baja sobre el compartimento encendido.
    this.beam = new THREE.Mesh(
      new THREE.CylinderGeometry(0.42, 0.42, 1, 24, 1, true),
      new THREE.MeshBasicMaterial({ color: C.gold, transparent: true, opacity: 0, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide }),
    );
    s.add(this.beam, this.beamLight);
    this.litLabel = label("lbl3d lit");
    this.litLabel.visible = false;
    s.add(this.litLabel);

    this.worker = makeWorker();
    s.add(this.worker.root);
    this.bubble = label("bubble3d", "Listo para el próximo pedido");
    this.bubble.position.set(0, 2.45, 0);
    this.worker.root.add(this.bubble);

    this.deskCrate = box(0.36, 0.28, 0.3, std(C.gold), DESK_DROP.x, DESK_DROP.y + 0.14, DESK_DROP.z);
    this.deskCrate.visible = false;
    s.add(this.deskCrate);

    this.stage.tick((dt, t) => this.update(dt, t));
    this.placeWorker();
  }

  private buildRoom() {
    const s = this.stage.scene;
    // Pared del fondo con línea dorada.
    s.add(box(16, 4.2, 0.2, std(0x0d1430, { metalness: 0.4 }), 0, 2.1, -4.1));
    s.add(box(16, 0.05, 0.05, glow(C.gold, 2.2), 0, 3.6, -3.98, false));
    s.add(box(16, 0.03, 0.05, glow(C.guide, 1.4), 0, 0.15, -3.98, false));
    const title = label("lbl3d title", "ESTANTERÍA B");
    title.position.set(0, 3.95, -3.9);
    s.add(title);

    // Consola de despacho donde el operario entrega.
    const steel = std(C.steel, { metalness: 0.7, roughness: 0.35 });
    s.add(box(0.8, 0.9, 2.2, steel, DESK_X, 0.45, 0.9));
    s.add(box(0.82, 0.03, 2.22, glow(C.guide, 0.9), DESK_X, 0.91, 0.9, false));
    const screen = new THREE.Mesh(
      new THREE.PlaneGeometry(1.3, 0.7),
      new THREE.MeshBasicMaterial({ color: C.guide, transparent: true, opacity: 0.22, side: THREE.DoubleSide }),
    );
    screen.position.set(DESK_X + 0.2, 1.5, 1.3);
    screen.rotation.y = -Math.PI / 2;
    s.add(screen);
    const desk = label("lbl3d", "DESPACHO");
    desk.position.set(DESK_X + 0.2, 1.95, 1.3);
    s.add(desk);
  }

  /** Arma (o rearma) la estantería con los compartimentos que devuelve la API. */
  setCompartments(list: Compartment[], categoryBySku: Map<string, string>) {
    const s = this.stage.scene;
    s.remove(this.rack);
    this.rack.traverse((o) => {
      if (o instanceof THREE.Mesh) o.geometry.dispose();
      if (o instanceof CSS2DObject) o.element.remove();
    });
    this.rack = new THREE.Group();
    s.add(this.rack);
    this.cells.clear();
    if (list.length === 0) return;

    const slots = Math.max(...list.map((c) => c.slot));
    const levels = Math.max(...list.map((c) => c.level));
    const width = slots * SLOT_W + 0.2;
    const frame = std(0x26315a, { metalness: 0.75, roughness: 0.3 });
    const shelfMat = std(0x18213f, { metalness: 0.6, roughness: 0.4 });
    const edge = glow(C.guide, 0.9);

    for (let i = 0; i <= slots; i += slots / 2) {
      const x = -width / 2 + (i / slots) * (width - 0.1) + 0.05;
      for (const z of [RACK_Z - 0.35, RACK_Z + 0.35]) this.rack.add(box(0.07, levels * LEVEL_H + 0.5, 0.07, frame, x, (levels * LEVEL_H + 0.5) / 2, z));
    }

    for (let lv = 1; lv <= levels + 1; lv++) {
      const y = BASE_Y + (lv - 1) * LEVEL_H;
      this.rack.add(box(width, 0.05, 0.78, shelfMat, 0, y, RACK_Z));
      this.rack.add(box(width, 0.02, 0.02, edge, 0, y - 0.035, RACK_Z + 0.4, false));
      if (lv <= levels) {
        const tag = label("lbl3d dim", `NIVEL ${String(lv).padStart(2, "0")}`);
        tag.position.set(-width / 2 - 0.55, y + 0.3, RACK_Z + 0.3);
        this.rack.add(tag);
      }
    }

    for (const c of list) {
      const x = (c.slot - (slots + 1) / 2) * SLOT_W;
      const y = BASE_Y + (c.level - 1) * LEVEL_H;
      const led = glow(0x1a2552, 1);
      this.rack.add(box(0.62, 0.05, 0.04, led, x, y + LEVEL_H - 0.07, RACK_Z + 0.4, false));

      let crate: THREE.Mesh | null = null;
      if (c.productSku) {
        const h = 0.18 + Math.min(1, c.quantity / 40) * 0.32;
        crate = box(0.66, h, 0.5, std(CATEGORY_COLORS[categoryBySku.get(c.productSku) ?? ""] ?? C.gold, { roughness: 0.6 }), x, y + 0.025 + h / 2, RACK_Z + 0.02);
        this.rack.add(crate);
      }

      // Volumen invisible para poder hacer clic en el compartimento (aunque esté vacío).
      const hit = new THREE.Mesh(new THREE.BoxGeometry(SLOT_W * 0.95, LEVEL_H * 0.9, 0.8), new THREE.MeshBasicMaterial({ visible: false }));
      hit.position.set(x, y + LEVEL_H / 2, RACK_Z);
      hit.userData.pick = `cell:${c.code}`;
      this.rack.add(hit);

      this.cells.set(c.code, { data: c, led, crate, pos: new THREE.Vector3(x, y, RACK_Z + 0.4) });
    }
  }

  setLit(code: string | null, flash: string | null) {
    this.lit = code;
    this.flash = flash;
    const cell = code ? this.cells.get(code) : undefined;
    this.litLabel.visible = !!cell;
    if (cell) {
      this.litLabel.element.textContent = `${cell.data.code} · ${cell.data.productName ?? ""}`;
      this.litLabel.position.set(cell.pos.x, cell.pos.y + LEVEL_H + 0.1, cell.pos.z + 0.2);
    }
  }

  // ---------- Operario ----------

  dispatch(task: PickTask) {
    if (this.task?.pickId === task.pickId && this.phase !== "return") return;
    if (this.pending.some((p) => p.pickId === task.pickId)) return;
    if (this.phase === "idle" || this.phase === "return") this.start(task);
    else this.pending.push(task);
  }

  /** El pedido se cerró por otro lado (botón físico, cancelado, vencido). */
  abort(pickId: string) {
    this.pending = this.pending.filter((p) => p.pickId !== pickId);
    if (this.task?.pickId !== pickId) return;
    if (this.phase === "toShelf" || this.phase === "reach" || this.phase === "toBase") {
      this.phase = "return";
      this.worker.crate.visible = false;
      this.say("Ese pedido ya se cerró, vuelvo a la base");
    }
  }

  private start(task: PickTask) {
    if (!this.cells.has(task.code)) return;
    this.task = task;
    this.phase = "toShelf";
    this.delivered = false;
    this.say(`¡Voy a ${task.code} por ${task.productName}!`);
  }

  private say(text: string) {
    this.bubble.element.textContent = text;
    this.bubble.element.classList.remove("pop");
    void this.bubble.element.offsetWidth;
    this.bubble.element.classList.add("pop");
    this.onStatus(text);
  }

  get workerPosition() {
    return this.pos.clone().add(new THREE.Vector3(0, 1.2, 0));
  }

  /** Camina hacia el destino. Devuelve true al llegar. */
  private walkTo(target: THREE.Vector3, dt: number) {
    const d = new THREE.Vector3(target.x - this.pos.x, 0, target.z - this.pos.z);
    const dist = d.length();
    if (dist < 0.03) {
      this.walk = 0;
      return true;
    }
    const step = Math.min(dist, 1.9 * dt);
    this.pos.addScaledVector(d.normalize(), step);
    this.heading = lerpAngle(this.heading, Math.atan2(d.x, d.z), 1 - Math.exp(-dt * 12));
    this.walk += dt * 10;
    return false;
  }

  private update(dt: number, t: number) {
    const w = this.worker;
    const carrying = w.crate.visible;
    let armTarget = carrying ? -1.15 : 0;
    this.timer += dt;

    switch (this.phase) {
      case "idle": {
        this.heading = lerpAngle(this.heading, HOME_HEADING, 1 - Math.exp(-dt * 5));
        const next = this.pending.shift();
        if (next) this.start(next);
        break;
      }
      case "toShelf": {
        const cell = this.cells.get(this.task!.code)!;
        if (this.walkTo(new THREE.Vector3(cell.pos.x, 0, RACK_Z + 1.0), dt)) {
          this.phase = "reach";
          this.timer = 0;
        }
        break;
      }
      case "reach": {
        const cell = this.cells.get(this.task!.code)!;
        this.heading = lerpAngle(this.heading, Math.PI, 1 - Math.exp(-dt * 10));
        const lv = cell.data.level;
        const up = lv <= 1 ? -0.75 : lv === 2 ? -1.6 : -2.7;
        armTarget = this.timer < 0.7 ? up : -1.15;
        if (this.timer > 0.7 && !w.crate.visible) {
          (w.crate.material as THREE.MeshStandardMaterial).color.copy(
            (cell.crate?.material as THREE.MeshStandardMaterial | undefined)?.color ?? new THREE.Color(C.gold),
          );
          w.crate.visible = true;
          this.say(`¡Lo tengo! Vuelvo con ${this.task!.productName}`);
        }
        if (this.timer > 1.3) this.phase = "toBase";
        break;
      }
      case "toBase":
        if (this.walkTo(HOME, dt)) {
          this.phase = "deliver";
          this.timer = 0;
        }
        break;
      case "deliver":
        this.heading = lerpAngle(this.heading, HOME_HEADING, 1 - Math.exp(-dt * 10));
        armTarget = -1.4;
        if (this.timer > 0.45 && !this.delivered) {
          this.delivered = true;
          w.crate.visible = false;
          (this.deskCrate.material as THREE.MeshStandardMaterial).color.copy((w.crate.material as THREE.MeshStandardMaterial).color);
          this.deskCrate.visible = true;
          this.deskTimer = 0;
          this.say(`Acá está: ${this.task!.productName} ✔`);
          this.onDelivered(this.task!.pickId);
        }
        if (this.timer > 1.2) {
          this.phase = "idle";
          this.task = null;
        }
        break;
      case "return":
        if (this.walkTo(HOME, dt)) {
          this.phase = "idle";
          this.task = null;
          this.say("Listo para el próximo pedido");
        }
        break;
    }

    // Pose: piernas y brazos.
    const swing = this.walk > 0 ? Math.sin(this.walk) * 0.6 : 0;
    const k = 1 - Math.exp(-dt * 12);
    w.legL.rotation.x += (swing - w.legL.rotation.x) * k;
    w.legR.rotation.x += (-swing - w.legR.rotation.x) * k;
    const armSwing = armTarget === 0 ? -swing * 0.8 : 0;
    w.armL.rotation.x += (armTarget + armSwing - w.armL.rotation.x) * k;
    w.armR.rotation.x += (armTarget - armSwing - w.armR.rotation.x) * k;
    this.placeWorker(this.walk > 0 ? Math.abs(Math.sin(this.walk)) * 0.05 : Math.sin(t * 2) * 0.008);

    // LEDs.
    const pulse = 0.5 + 0.5 * Math.sin(t * 6);
    for (const [code, cell] of this.cells) {
      if (code === this.lit) {
        cell.led.emissive.setHex(C.gold);
        cell.led.emissiveIntensity = 2.5 + pulse * 3;
      } else if (code === this.flash) {
        cell.led.emissive.setHex(C.free);
        cell.led.emissiveIntensity = 4;
      } else {
        cell.led.emissive.setHex(0x1a2552);
        cell.led.emissiveIntensity = 1;
      }
    }

    const cell = this.lit ? this.cells.get(this.lit) : undefined;
    const beamMat = this.beam.material as THREE.MeshBasicMaterial;
    if (cell) {
      const top = 4;
      const bottom = cell.pos.y + 0.03;
      this.beam.scale.y = top - bottom;
      this.beam.position.set(cell.pos.x, (top + bottom) / 2, RACK_Z + 0.05);
      beamMat.opacity = 0.07 + pulse * 0.06;
      this.beamLight.position.set(cell.pos.x, cell.pos.y + 0.5, RACK_Z + 0.8);
      this.beamLight.intensity = 3 + pulse * 3;
    } else {
      beamMat.opacity = 0;
      this.beamLight.intensity = 0;
    }

    if (this.deskCrate.visible && (this.deskTimer += dt) > 3) this.deskCrate.visible = false;
  }

  private placeWorker(bob = 0) {
    this.worker.root.position.set(this.pos.x, bob, this.pos.z);
    this.worker.root.rotation.y = this.heading;
  }

  dispose() {
    this.stage.dispose();
  }
}
