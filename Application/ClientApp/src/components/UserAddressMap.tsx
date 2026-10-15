import { useEffect, useRef } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';

export interface UserMapPoint {
  userID: number;
  name: string;
  department: string;
  addressType: string;
  addressLine: string;
  country?: string;
  latitude: number | null;
  longitude: number | null;
  hasPhoto?: boolean;
  plotNote?: string | null;
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function initialsFor(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '?';
  }
  if (parts.length === 1) {
    return parts[0]!.slice(0, 2).toUpperCase();
  }
  return `${parts[0]![0] ?? ''}${parts[parts.length - 1]![0] ?? ''}`.toUpperCase();
}

const PIN = 42;
const PIN_GAP = 12;
const MIN_PIN_DISTANCE = PIN + PIN_GAP;

function markerIcon(point: UserMapPoint, offset?: { dx: number; dy: number }): L.DivIcon {
  const initials = escapeHtml(initialsFor(point.name));
  const photo = point.hasPhoto
    ? `<img alt="" src="/api/admin/users/${point.userID}/photo" />`
    : `<span class="admin-user-map-pin-fallback">${initials}</span>`;
  const pin = `<div class="admin-user-map-pin" title="${escapeHtml(point.name)}">${photo}</div>`;
  if (!offset) {
    return L.divIcon({
      className: 'leaflet-div-icon admin-user-map-marker',
      html: pin,
      iconSize: [PIN, PIN],
      iconAnchor: [PIN / 2, PIN / 2],
      popupAnchor: [0, -20],
    });
  }

  const shift = Math.hypot(offset.dx, offset.dy);
  if (shift < PIN / 2 + 4) {
    return L.divIcon({
      className: 'leaflet-div-icon admin-user-map-marker',
      html: pin,
      iconSize: [PIN, PIN],
      iconAnchor: [PIN / 2 - offset.dx, PIN / 2 - offset.dy],
      popupAnchor: [offset.dx, offset.dy - 20],
    });
  }

  const { dx, dy } = offset;
  const length = Math.hypot(dx, dy) || 1;
  const ux = dx / length;
  const uy = dy / length;
  const pad = 30;
  const size = Math.ceil((length + pad) * 2);
  const center = size / 2;
  const startX = center + dx - ux * 24;
  const startY = center + dy - uy * 24;
  const markerId = `admin-map-arrow-${point.userID}-${Math.round(dx)}-${Math.round(dy)}`;
  const arrow = `
    <svg class="admin-user-map-arrow" width="${size}" height="${size}" aria-hidden="true">
      <defs>
        <marker id="${markerId}" markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto">
          <path d="M0,0 L7,3.5 L0,7 Z" fill="#16324f" />
        </marker>
      </defs>
      <line x1="${startX}" y1="${startY}" x2="${center}" y2="${center}" stroke="#ffffff" stroke-width="5" />
      <line x1="${startX}" y1="${startY}" x2="${center}" y2="${center}" stroke="#16324f" stroke-width="2" marker-end="url(#${markerId})" />
    </svg>`;
  return L.divIcon({
    className: 'leaflet-div-icon admin-user-map-marker admin-user-map-marker--callout',
    html: `<div class="admin-user-map-callout" style="width:${size}px;height:${size}px">${arrow}<div class="admin-user-map-pin" style="left:${center + dx - 21}px;top:${center + dy - 21}px" title="${escapeHtml(point.name)}">${photo}</div></div>`,
    iconSize: [size, size],
    iconAnchor: [center, center],
    popupAnchor: [dx, dy - 24],
  });
}

const US_BOXES: Array<[number, number, number, number]> = [
  [24.4, 49.5, -125, -66.9],
  [51, 71.5, -170, -129],
  [18.8, 22.3, -160.3, -154.7],
  [17.8, 18.6, -67.3, -65.2],
];

const US_MAX_BOUNDS = L.latLngBounds([15, -170], [72, -66]);
const US_VIEW: L.LatLngExpression = [39.8, -98.6];
const US_FRAME = L.latLngBounds([24.4, -125], [49.4, -66.9]);

function isUsCountry(country: string): boolean {
  const value = country.trim().toLowerCase();
  return value === '' || value === 'us' || value === 'usa' || value === 'united states' || value === 'united states of america';
}

function isInUsBox(latitude: number, longitude: number): boolean {
  return US_BOXES.some(
    ([south, north, west, east]) => latitude >= south && latitude <= north && longitude >= west && longitude <= east,
  );
}

export function isUnitedStatesPoint(point: UserMapPoint): boolean {
  const country = (point.country ?? '').trim();
  if (country && !isUsCountry(country)) {
    return false;
  }
  if (isUsCountry(country) && country.trim() !== '') {
    return true;
  }
  if (point.latitude == null || point.longitude == null) {
    return true;
  }
  return isInUsBox(point.latitude, point.longitude);
}

export function regionLabel(point: UserMapPoint): string {
  const country = (point.country ?? '').trim();
  return country || 'Outside the United States';
}

function popupHtml(point: UserMapPoint): string {
  const lines = [
    point.name,
    point.department ? point.department : '',
    point.addressType,
    point.addressLine,
  ].filter((line) => line.trim() !== '');
  return lines.map(escapeHtml).join('<br>');
}

/** Keeps every photo a fixed pixel gap apart, including people at different addresses. */
function layoutPins(points: UserMapPoint[], map: L.Map): Array<{ point: UserMapPoint; offset: { dx: number; dy: number } | null }> {
  const nodes = points
    .filter((point) => point.latitude != null && point.longitude != null)
    .map((point) => {
      const projected = map.latLngToContainerPoint([point.latitude!, point.longitude!]);
      return { point, x: projected.x, y: projected.y, ax: projected.x, ay: projected.y };
    });

  const buckets = new Map<string, typeof nodes>();
  for (const node of nodes) {
    const key = `${Math.round(node.ax)}:${Math.round(node.ay)}`;
    const list = buckets.get(key) ?? [];
    list.push(node);
    buckets.set(key, list);
  }
  for (const group of buckets.values()) {
    if (group.length < 2) {
      continue;
    }
    const radius = MIN_PIN_DISTANCE / (2 * Math.sin(Math.PI / group.length));
    group.forEach((node, index) => {
      const angle = (index / group.length) * Math.PI * 2 - Math.PI / 2;
      node.x = node.ax + Math.cos(angle) * radius;
      node.y = node.ay + Math.sin(angle) * radius;
    });
  }

  const size = map.getSize();
  const margin = PIN / 2 + 4;
  for (let pass = 0; pass < 180; pass += 1) {
    let moved = false;
    for (let i = 0; i < nodes.length; i += 1) {
      for (let j = i + 1; j < nodes.length; j += 1) {
        const left = nodes[i]!;
        const right = nodes[j]!;
        let dx = right.x - left.x;
        let dy = right.y - left.y;
        let distance = Math.hypot(dx, dy);
        if (distance < 0.01) {
          const angle = (i * 2.399963 + j) % (Math.PI * 2);
          dx = Math.cos(angle);
          dy = Math.sin(angle);
          distance = 1;
        }
        if (distance >= MIN_PIN_DISTANCE) {
          continue;
        }
        const push = (MIN_PIN_DISTANCE - distance) / 2;
        const ux = dx / distance;
        const uy = dy / distance;
        left.x -= ux * push;
        left.y -= uy * push;
        right.x += ux * push;
        right.y += uy * push;
        moved = true;
      }
    }
    for (const node of nodes) {
      const nextX = Math.min(size.x - margin, Math.max(margin, node.x));
      const nextY = Math.min(size.y - margin, Math.max(margin, node.y));
      if (nextX !== node.x || nextY !== node.y) {
        node.x = nextX;
        node.y = nextY;
        moved = true;
      }
    }
    if (!moved) {
      break;
    }
  }

  return nodes.map((node) => {
    const dx = node.x - node.ax;
    const dy = node.y - node.ay;
    return {
      point: node.point,
      offset: Math.hypot(dx, dy) < 1 ? null : { dx, dy },
    };
  });
}

function fitPadding(points: UserMapPoint[]): number {
  const counts = new Map<string, number>();
  for (const point of points) {
    if (point.latitude == null || point.longitude == null) {
      continue;
    }
    const key = `${point.latitude.toFixed(5)},${point.longitude.toFixed(5)}`;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  let radius = PIN / 2 + 16;
  for (const count of counts.values()) {
    if (count < 2) {
      continue;
    }
    radius = Math.max(radius, MIN_PIN_DISTANCE / (2 * Math.sin(Math.PI / count)) + PIN / 2 + 12);
  }
  return radius;
}

function pinsInsideMap(points: UserMapPoint[], map: L.Map): boolean {
  const size = map.getSize();
  if (size.x < 40 || size.y < 40) {
    return true;
  }
  const margin = 4;
  return layoutPins(points, map).every(({ point, offset }) => {
    const origin = map.latLngToContainerPoint([point.latitude!, point.longitude!]);
    const x = origin.x + (offset?.dx ?? 0);
    const y = origin.y + (offset?.dy ?? 0);
    const reach = PIN / 2;
    return x - reach >= margin && y - reach >= margin && x + reach <= size.x - margin && y + reach <= size.y - margin;
  });
}

export default function UserAddressMap({
  points,
  focus = 'region',
  label,
}: {
  points: UserMapPoint[];
  focus?: 'us' | 'region';
  label: string;
}) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const layerRef = useRef<L.LayerGroup | null>(null);

  useEffect(() => {
    const host = hostRef.current;
    if (!host || mapRef.current) {
      return;
    }

    const map = L.map(host, {
      preferCanvas: true,
      zoomSnap: 0.25,
      minZoom: 2,
      maxZoom: 18,
      maxBounds: focus === 'us' ? US_MAX_BOUNDS : undefined,
      maxBoundsViscosity: focus === 'us' ? 1 : 0,
    });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 18,
    }).addTo(map);
    map.setView(focus === 'us' ? US_VIEW : [20, 0], focus === 'us' ? 4 : 2);
    layerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;

    return () => {
      map.remove();
      mapRef.current = null;
      layerRef.current = null;
    };
  }, [focus]);

  useEffect(() => {
    const map = mapRef.current;
    const layer = layerRef.current;
    if (!map || !layer) {
      return;
    }

    const draw = () => {
      layer.clearLayers();
      const markers = layoutPins(points, map);
      const seenAnchors = new Set<string>();
      markers.forEach(({ point, offset }) => {
        const shifted = offset != null && Math.hypot(offset.dx, offset.dy) >= PIN / 2 + 4;
        if (shifted) {
          const key = `${point.latitude!.toFixed(5)},${point.longitude!.toFixed(5)}`;
          if (!seenAnchors.has(key)) {
            seenAnchors.add(key);
            L.circleMarker([point.latitude!, point.longitude!], {
              radius: 4,
              weight: 2,
              color: '#16324f',
              fillColor: '#ffffff',
              fillOpacity: 1,
            }).addTo(layer);
          }
        }
        L.marker([point.latitude!, point.longitude!], {
          icon: markerIcon(point, offset ?? undefined),
          zIndexOffset: 400,
        })
          .bindPopup(popupHtml(point))
          .addTo(layer);
      });
    };

    const frame = window.setTimeout(() => {
      map.invalidateSize();
      const located = points.filter((point) => point.latitude != null && point.longitude != null);
      const bounds = located.map((point) => [point.latitude!, point.longitude!] as L.LatLngExpression);
      const padding = fitPadding(located);
      if (bounds.length === 1) {
        map.setView(bounds[0]!, 11, { animate: false });
      } else if (bounds.length > 1) {
        map.fitBounds(bounds, { padding: [padding, padding], animate: false });
      } else if (focus === 'us') {
        map.fitBounds(US_FRAME, { padding: [24, 24], animate: false });
      }
      draw();
      let guard = 0;
      while (guard < 10 && !pinsInsideMap(points, map) && map.getZoom() > map.getMinZoom()) {
        map.setZoom(map.getZoom() - 0.5, { animate: false });
        guard += 1;
      }
      draw();
    }, 0);
    map.on('zoomend', draw);
    return () => {
      window.clearTimeout(frame);
      map.off('zoomend', draw);
    };
  }, [points, focus]);

  return (
    <div
      ref={hostRef}
      className="admin-user-map"
      role="region"
      aria-label={label}
    />
  );
}
