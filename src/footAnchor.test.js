import assert from 'node:assert/strict'
import test from 'node:test'
import {
  clampFootAnchor,
  formatTpsheetNumber,
  toExportedFootAnchor,
} from './footAnchor.js'

test('converts a top-left output pixel anchor into a Unity pivot', () => {
  const result = toExportedFootAnchor(
    { x: 413, y: 411 },
    { x: 314, y: 67, width: 198, height: 365 },
    0.5,
    99,
    183,
  )

  assert.deepEqual(result.pixel, { x: 49.5, y: 172 })
  assert.deepEqual(result.pivot, { x: 0.5, y: 0.060109 })
})

test('clamps a foot anchor to the exported crop rectangle', () => {
  assert.deepEqual(
    clampFootAnchor(
      { x: 900, y: -10 },
      { x: 100, y: 50, width: 200, height: 300 },
    ),
    { x: 300, y: 50 },
  )
})

test('formats sheet numbers without locale or redundant zeros', () => {
  assert.equal(formatTpsheetNumber(0.060109), '0.060109')
  assert.equal(formatTpsheetNumber(0.5), '0.5')
  assert.equal(formatTpsheetNumber(172, 3), '172')
})
