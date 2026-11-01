/** Cielo nocturno de fondo (identidad Polaris, igual que la demo). */
export function startSky(canvas: HTMLCanvasElement | null) {
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  let stars: { x: number; y: number; r: number; p: number; s: number }[] = [];
  let w = 0;
  let h = 0;

  const size = () => {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    w = window.innerWidth;
    h = window.innerHeight;
    canvas.width = w * dpr;
    canvas.height = h * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    stars = Array.from({ length: Math.round((w * h) / 5200) }, () => ({
      x: Math.random() * w,
      y: Math.random() * h,
      r: Math.random() * 1.1 + 0.2,
      p: Math.random() * 6.28,
      s: 0.4 + Math.random() * 1.2,
    }));
  };

  const draw = (t: number) => {
    ctx.clearRect(0, 0, w, h);
    for (const s of stars) {
      const a = reduce ? 0.6 : 0.35 + 0.45 * Math.sin((t / 1000) * s.s + s.p);
      ctx.globalAlpha = Math.max(0.08, a);
      ctx.fillStyle = "#dfe6ff";
      ctx.beginPath();
      ctx.arc(s.x, s.y, s.r, 0, 6.283);
      ctx.fill();
    }
    if (!reduce) requestAnimationFrame(draw);
  };

  size();
  window.addEventListener("resize", () => {
    size();
    if (reduce) draw(0);
  });
  requestAnimationFrame(draw);
}
