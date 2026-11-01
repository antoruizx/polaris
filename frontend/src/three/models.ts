// Modelos low-poly: el operario del depósito y los autos del estacionamiento. Todos miran hacia +z.
import * as THREE from "three";
import { box, C, glow, std } from "./stage";

export interface WorkerModel {
  root: THREE.Group;
  legL: THREE.Group;
  legR: THREE.Group;
  armL: THREE.Group;
  armR: THREE.Group;
  crate: THREE.Mesh;
}

export function makeWorker(): WorkerModel {
  const root = new THREE.Group();
  const suit = std(0x223066);
  const vest = std(0xff8a2a, { roughness: 0.5 });
  const skin = std(0xf1c7a0, { roughness: 0.8 });
  const dark = std(0x0e1428);

  const limb = (x: number, y: number, w: number, h: number, mat: THREE.Material, end?: THREE.Mesh) => {
    const pivot = new THREE.Group();
    pivot.position.set(x, y, 0);
    pivot.add(box(w, h, w + 0.02, mat, 0, -h / 2, 0));
    if (end) pivot.add(end);
    root.add(pivot);
    return pivot;
  };

  const legL = limb(-0.13, 0.9, 0.18, 0.82, suit, box(0.2, 0.12, 0.3, dark, 0, -0.84, 0.04));
  const legR = limb(0.13, 0.9, 0.18, 0.82, suit, box(0.2, 0.12, 0.3, dark, 0, -0.84, 0.04));

  root.add(box(0.5, 0.64, 0.3, vest, 0, 1.24, 0));
  root.add(box(0.52, 0.05, 0.32, glow(C.gold, 2.4), 0, 1.12, 0, false)); // franja reflectiva
  root.add(box(0.52, 0.05, 0.32, glow(C.gold, 2.4), 0, 1.36, 0, false));

  const hand = () => {
    const h = new THREE.Mesh(new THREE.SphereGeometry(0.08, 12, 10), skin);
    h.position.y = -0.62;
    h.castShadow = true;
    return h;
  };
  const armL = limb(-0.33, 1.52, 0.14, 0.58, suit, hand());
  const armR = limb(0.33, 1.52, 0.14, 0.58, suit, hand());

  const head = new THREE.Mesh(new THREE.SphereGeometry(0.2, 20, 16), skin);
  head.position.y = 1.78;
  head.castShadow = true;
  root.add(head);

  const helmet = new THREE.Mesh(new THREE.SphereGeometry(0.218, 20, 12, 0, Math.PI * 2, 0, Math.PI / 2), std(C.gold, { metalness: 0.5, roughness: 0.3 }));
  helmet.position.y = 1.8;
  helmet.castShadow = true;
  root.add(helmet);
  root.add(box(0.3, 0.06, 0.06, glow(C.guide, 3), 0, 1.79, 0.18, false)); // visor

  // Caja que lleva en las manos (aparece cuando retira el producto).
  const crate = box(0.36, 0.28, 0.3, std(C.gold), 0, 1.0, 0.42);
  crate.visible = false;
  root.add(crate);

  return { root, legL, legR, armL, armR, crate };
}

export function makeCar(color: number): THREE.Group {
  const car = new THREE.Group();
  const paint = std(color, { metalness: 0.65, roughness: 0.3 });
  const glass = std(0x0b1226, { metalness: 0.9, roughness: 0.08 });
  const tire = std(0x0a0d18, { roughness: 0.9 });

  car.add(box(1.7, 0.5, 3.6, paint, 0, 0.5, 0));
  car.add(box(1.5, 0.12, 3.2, paint, 0, 0.8, -0.05));
  car.add(box(1.42, 0.42, 1.8, glass, 0, 1.06, -0.2));
  car.add(box(1.72, 0.04, 3.3, glow(C.guide, 1.6), 0, 0.26, 0, false)); // luz de neón inferior

  const wheelGeo = new THREE.CylinderGeometry(0.34, 0.34, 0.26, 20);
  for (const x of [-0.8, 0.8]) {
    for (const z of [-1.15, 1.15]) {
      const w = new THREE.Mesh(wheelGeo, tire);
      w.rotation.z = Math.PI / 2;
      w.position.set(x, 0.34, z);
      w.castShadow = true;
      car.add(w);
    }
  }

  car.add(box(0.38, 0.1, 0.04, glow(0xdff4ff, 3.2), -0.55, 0.6, 1.81, false));
  car.add(box(0.38, 0.1, 0.04, glow(0xdff4ff, 3.2), 0.55, 0.6, 1.81, false));
  car.add(box(0.42, 0.08, 0.04, glow(0xff2a40, 2.6), -0.55, 0.62, -1.81, false));
  car.add(box(0.42, 0.08, 0.04, glow(0xff2a40, 2.6), 0.55, 0.62, -1.81, false));
  return car;
}
