const DEFAULT_PRECISION = 6

export function clampFootAnchor(anchor, crop) {
  const x = Number(anchor?.x)
  const y = Number(anchor?.y)
  return {
    x: Math.max(crop.x, Math.min(crop.x + crop.width, Number.isFinite(x) ? x : crop.x + crop.width / 2)),
    y: Math.max(crop.y, Math.min(crop.y + crop.height, Number.isFinite(y) ? y : crop.y + crop.height)),
  }
}

export function toExportedFootAnchor(
  anchor,
  crop,
  scale,
  outputWidth,
  outputHeight,
) {
  if (!anchor || outputWidth <= 0 || outputHeight <= 0) return null
  const pixelX = (Number(anchor.x) - crop.x) * scale
  const pixelY = (Number(anchor.y) - crop.y) * scale
  return {
    pixel: {
      x: roundNumber(pixelX, 3),
      y: roundNumber(pixelY, 3),
    },
    pivot: {
      x: roundNumber(pixelX / outputWidth, DEFAULT_PRECISION),
      y: roundNumber((outputHeight - pixelY) / outputHeight, DEFAULT_PRECISION),
    },
  }
}

export function formatTpsheetNumber(value, precision = DEFAULT_PRECISION) {
  const numeric = Number(value)
  if (!Number.isFinite(numeric)) return '0'
  return roundNumber(numeric, precision)
    .toFixed(precision)
    .replace(/\.?0+$/, '')
}

function roundNumber(value, precision) {
  const factor = 10 ** precision
  return Math.round((value + Number.EPSILON) * factor) / factor
}
