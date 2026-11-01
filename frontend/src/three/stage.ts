// Motor común de las escenas 3D: renderer, cámara con órbita, brillo (bloom), etiquetas HTML y clics.
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { CSS2DObject, CSS2DRenderer } from "three/examples/jsm/renderers/CSS2DRenderer.js";
import { EffectComposer } from "three/examples/jsm/postprocessing/EffectComposer.js";
import { RenderPass } from "three/examples/jsm/postprocessing/RenderPass.js";
import { UnrealBloomPass } from "three/examples/jsm/postprocessing/UnrealBloomPass.js";
import { OutputPass } from "three/examples/jsm/postprocessing/OutputPass.js";
import { RoomEnvironment } from "three/examples/jsm/environments/RoomEnvironment.js";

export const C = {
  bg: 0x060a18,
  floor: 0x0a1026,
  gold: 0xf4d088,
  free: 0x3ddc8f,
  busy: 0xff5b6b,
  guide: 0x5ec8ff,
  line: 0x2f3b66,
  steel: 0x1a2340,
};

export interface View {
  pos: [number, number, number];
  target: [number, number, number];
}

export class Stage {
  readonly scene = new THREE.Scene();
  readonly camera: THREE.PerspectiveCamera;
  readonly renderer: THREE.WebGLRenderer;
  readonly controls: OrbitControls;
  /** Si está definido, la cámara sigue a ese punto (manteniendo la distancia que eligió el usuario). */
  follow: (() => THREE.Vector3) | null = null;
  onPick: ((key: string) => void) | null = null;

  private labels = new CSS2DRenderer();
  private composer: EffectComposer;
  private bloom: UnrealBloomPass;
  private ro: ResizeObserver;
  private raf = 0;
  private clock = new THREE.Clock();
  private tickers: ((dt: number, t: number) => void)[] = [];
  private tween: {
    p0: THREE.Vector3;
    p1: THREE.Vector3;
    t0: THREE.Vector3;
    t1: THREE.Vector3;
    k: number;
    then: (() => THREE.Vector3) | null;
  } | null = null;
  private down: { x: number; y: number } | null = null;

  constructor(
    private host: HTMLElement,
    view: View,
  ) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: "high-performance" });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.domElement.className = "stage-canvas";
    host.appendChild(this.renderer.domElement);

    this.labels.domElement.className = "stage-labels";
    host.appendChild(this.labels.domElement);

    this.scene.background = new THREE.Color(C.bg);
    this.scene.fog = new THREE.Fog(C.bg, 28, 70);
    const pmrem = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    this.scene.environmentIntensity = 0.35;
    pmrem.dispose();

    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 200);
    this.camera.position.set(...view.pos);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.target.set(...view.target);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI * 0.47;
    this.controls.minDistance = 3;
    this.controls.maxDistance = 40;
    this.controls.addEventListener("start", () => (this.tween = null));

    this.composer = new EffectComposer(this.renderer);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.bloom = new UnrealBloomPass(new THREE.Vector2(1, 1), 0.55, 0.5, 0.82);
    this.composer.addPass(this.bloom);
    this.composer.addPass(new OutputPass());

    this.addLights();

    const el = this.renderer.domElement;
    el.addEventListener("pointerdown", this.onDown);
    el.addEventListener("pointerup", this.onUp);

    this.ro = new ResizeObserver(() => this.resize());
    this.ro.observe(host);
    this.resize();
    this.loop();
  }

  private addLights() {
    this.scene.add(new THREE.HemisphereLight(0x8fa6ff, 0x0a0f22, 0.9));
    const sun = new THREE.DirectionalLight(0xdfe6ff, 1.6);
    sun.position.set(6, 14, 8);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    const s = sun.shadow.camera;
    s.left = -16;
    s.right = 16;
    s.top = 12;
    s.bottom = -12;
    s.far = 50;
    sun.shadow.bias = -0.0005;
    this.scene.add(sun);
  }

  /** Piso oscuro con grilla de neón. */
  addFloor(size: number) {
    const floor = new THREE.Mesh(
      new THREE.PlaneGeometry(size * 3, size * 3),
      new THREE.MeshStandardMaterial({ color: C.floor, roughness: 0.85, metalness: 0.2 }),
    );
    floor.rotation.x = -Math.PI / 2;
    floor.receiveShadow = true;
    this.scene.add(floor);

    const grid = new THREE.GridHelper(size * 2, size * 2, 0x2a3f8a, 0x152048);
    grid.position.y = 0.002;
    (grid.material as THREE.Material).transparent = true;
    (grid.material as THREE.Material).opacity = 0.55;
    this.scene.add(grid);
  }

  tick(fn: (dt: number, t: number) => void) {
    this.tickers.push(fn);
  }

  /** Mueve la cámara suavemente a una vista; si se pasa "follow", al llegar empieza a seguir ese punto. */
  goTo(view: View, follow: (() => THREE.Vector3) | null = null) {
    this.follow = null;
    this.tween = {
      p0: this.camera.position.clone(),
      p1: new THREE.Vector3(...view.pos),
      t0: this.controls.target.clone(),
      t1: new THREE.Vector3(...view.target),
      k: 0,
      then: follow,
    };
  }

  private resize() {
    const w = Math.max(1, this.host.clientWidth);
    const h = Math.max(1, this.host.clientHeight);
    this.renderer.setSize(w, h);
    this.composer.setSize(w, h);
    this.bloom.setSize(w, h);
    this.labels.setSize(w, h);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  private loop = () => {
    this.raf = requestAnimationFrame(this.loop);
    const dt = Math.min(this.clock.getDelta(), 0.05);
    const t = this.clock.elapsedTime;

    for (const fn of this.tickers) fn(dt, t);

    if (this.tween) {
      const tw = this.tween;
      tw.k = Math.min(1, tw.k + dt / 1.1);
      const e = tw.k < 0.5 ? 4 * tw.k ** 3 : 1 - (-2 * tw.k + 2) ** 3 / 2;
      this.camera.position.lerpVectors(tw.p0, tw.p1, e);
      this.controls.target.lerpVectors(tw.t0, tw.t1, e);
      if (tw.k >= 1) {
        this.tween = null;
        this.follow = tw.then;
      }
    } else if (this.follow) {
      const goal = this.follow();
      const next = this.controls.target.clone().lerp(goal, 1 - Math.exp(-dt * 4));
      this.camera.position.add(next.clone().sub(this.controls.target));
      this.controls.target.copy(next);
    }

    this.controls.update();
    this.composer.render();
    this.labels.render(this.scene, this.camera);
  };

  private onDown = (e: PointerEvent) => {
    this.down = { x: e.clientX, y: e.clientY };
  };

  private onUp = (e: PointerEvent) => {
    if (!this.down || !this.onPick) return;
    const moved = Math.hypot(e.clientX - this.down.x, e.clientY - this.down.y);
    this.down = null;
    if (moved > 6) return; // fue un arrastre de cámara

    const rect = this.renderer.domElement.getBoundingClientRect();
    const ndc = new THREE.Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
    const ray = new THREE.Raycaster();
    ray.setFromCamera(ndc, this.camera);
    for (const hit of ray.intersectObjects(this.scene.children, true)) {
      let o: THREE.Object3D | null = hit.object;
      while (o && !o.userData.pick) o = o.parent;
      if (o) {
        this.onPick(o.userData.pick as string);
        return;
      }
    }
  };

  dispose() {
    cancelAnimationFrame(this.raf);
    this.ro.disconnect();
    this.controls.dispose();
    this.renderer.domElement.removeEventListener("pointerdown", this.onDown);
    this.renderer.domElement.removeEventListener("pointerup", this.onUp);
    this.scene.traverse((o) => {
      if (o instanceof THREE.Mesh) {
        o.geometry.dispose();
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach((m) => m.dispose());
      }
      if (o instanceof CSS2DObject) o.element.remove();
    });
    this.composer.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
    this.labels.domElement.remove();
  }
}

// ---------- Ayudas ----------

export function label(className: string, text = ""): CSS2DObject {
  const div = document.createElement("div");
  div.className = className;
  div.textContent = text;
  const obj = new CSS2DObject(div);
  return obj;
}

export const std = (color: number, extra: THREE.MeshStandardMaterialParameters = {}) =>
  new THREE.MeshStandardMaterial({ color, roughness: 0.55, metalness: 0.15, ...extra });

export const glow = (color: number, intensity = 2) =>
  new THREE.MeshStandardMaterial({ color: 0x000000, emissive: color, emissiveIntensity: intensity, roughness: 0.4 });

export function box(w: number, h: number, d: number, mat: THREE.Material, x = 0, y = 0, z = 0, shadow = true) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
  m.position.set(x, y, z);
  m.castShadow = shadow;
  m.receiveShadow = shadow;
  return m;
}

/** Ángulo más corto entre a y b, interpolado. */
export function lerpAngle(a: number, b: number, k: number) {
  let d = ((b - a + Math.PI) % (Math.PI * 2)) - Math.PI;
  if (d < -Math.PI) d += Math.PI * 2;
  return a + d * k;
}

export const easeOut = (k: number) => 1 - (1 - k) ** 3;
