/** BMS-style atmosphere (glow + grid + scan lines). */
export default function BackgroundLayer() {
  return (
    <div className="bg-layer" aria-hidden="true">
      <div className="bg-glow" />
      <div className="bg-grid" />
      <div className="bg-scan" />
    </div>
  );
}
