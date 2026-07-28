<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import JSZip from 'jszip'
import UPNG from '@upng/upng-js'
import {
  PhArrowCounterClockwise,
  PhArrowsOutCardinal,
  PhCaretLeft,
  PhCaretRight,
  PhCheckerboard,
  PhCrosshairSimple,
  PhDownloadSimple,
  PhEye,
  PhEyeSlash,
  PhFolderOpen,
  PhFrameCorners,
  PhMagicWand,
  PhPause,
  PhPencilSimple,
  PhPlay,
  PhScissors,
  PhSlidersHorizontal,
  PhStack,
  PhWaveSine,
} from '@phosphor-icons/vue'
import { createBuiltInActions, createDefaultMotion, createImportedActions } from './assets'

const SOURCE_WIDTH = 836
const SOURCE_HEIGHT = 480
const MIN_CROP = 24
const DEFAULT_FPS = 12
const STORAGE_KEY = 'frame-aligner-state-v1'
const MIRROR_PAIR_CONFIGS = [
  { id: 'horizontal', label: '水平', leftId: 'left', rightId: 'right', leftLabel: '左', rightLabel: '右' },
  { id: 'up-diagonal', label: '上斜向', leftId: 'up-left', rightId: 'up-right', leftLabel: '左上', rightLabel: '右上' },
  { id: 'down-diagonal', label: '下斜向', leftId: 'down-left', rightId: 'down-right', leftLabel: '左下', rightLabel: '右下' },
]

const canvasRef = ref(null)
const fileInputRef = ref(null)
const stageRef = ref(null)
const exportPreviewCanvasRef = ref(null)
const actions = reactive(createBuiltInActions())
const activeId = ref('down')
const frameIndex = ref(0)
const motionTime = ref(0)
const isPlaying = ref(false)
const loop = ref(true)
const zoom = ref(1)
const stageBackground = ref('checker')
const showGuides = ref(true)
const analysisProgress = ref(0)
const exportProgress = ref(0)
const exportKind = ref('')
const exportScale = ref(100)
const pngCompression = ref('lossless')
const previewStatus = ref('正在生成')
const previewBytes = ref(0)
const previewOriginalBytes = ref(0)
const showOriginalPreview = ref(false)
const thinInterval = ref(2)
const statusMessage = ref('已载入 8 个方向')
const crop = reactive({ x: 314, y: 67, width: 198, height: 365 })
const imageCache = new Map()
const boundsCache = new Map()
let playbackTimer = null
let motionAnimationFrame = null
let motionLastTimestamp = null
let renderToken = 0
let previewTimer = null
let previewRenderToken = 0
let rawPreviewCanvas = null
let processedPreviewCanvas = null
let interaction = null

const activeAction = computed(() => actions.find((action) => action.id === activeId.value) || actions[0])
const activeFps = computed({
  get: () => activeAction.value?.fps || DEFAULT_FPS,
  set: (value) => {
    const numeric = Number(value)
    if (!activeAction.value || !Number.isFinite(numeric) || numeric <= 0) return
    activeAction.value.fps = Math.round(clamp(numeric, 1, 30))
  },
})
const visibleActions = computed(() => actions.filter((action) => action.visible))
const mirroredActions = computed(() => actions.filter((action) => action.mirroredFromId))
const collapsedProjects = reactive(new Set())
const collapsedCharacters = reactive(new Set())
const projectTree = computed(() => {
  const projects = new Map()

  for (const action of actions) {
    const projectId = action.projectId || 'default-project'
    const characterId = action.characterId || action.collectionId || 'default-character'
    if (!projects.has(projectId)) {
      projects.set(projectId, {
        id: projectId,
        name: action.projectName || '默认项目',
        characters: new Map(),
      })
    }
    const project = projects.get(projectId)
    if (!project.characters.has(characterId)) {
      project.characters.set(characterId, {
        id: characterId,
        name: action.characterName || '未命名人物',
        actions: [],
      })
    }
    project.characters.get(characterId).actions.push(action)
  }

  return [...projects.values()].map((project) => ({
    ...project,
    characters: [...project.characters.values()],
  }))
})
const activeCollectionActions = computed(() =>
  actions.filter((action) => action.collectionId === activeAction.value.collectionId),
)
const activeCollectionName = computed(() => activeAction.value.collectionName || '当前素材组')
const activeFrameOwner = computed(() => resolveFrameOwner(activeAction.value))
const activeMotion = computed(() => activeAction.value.motion)
const hasVisibleMotion = computed(() =>
  visibleActions.value.some((action) => action.motion?.enabled),
)
const motionSummary = computed(() => {
  const motion = activeMotion.value
  if (!motion?.enabled) return '未启用'
  const parts = []
  if (motion.moveX) parts.push(`X ±${motion.moveX}px`)
  if (motion.moveY) parts.push(`Y ±${motion.moveY}px`)
  if (motion.rotation) parts.push(`旋转 ±${motion.rotation}°`)
  if (motion.scale) parts.push(`缩放 ±${motion.scale}%`)
  return parts.length ? parts.join(' · ') : '已启用，等待设置幅度'
})
const mirrorPolicyRows = computed(() =>
  MIRROR_PAIR_CONFIGS.map((pair) => {
    const left = activeCollectionActions.value.find((action) => action.directionId === pair.leftId)
    const right = activeCollectionActions.value.find((action) => action.directionId === pair.rightId)
    let value = 'real'
    if (right?.mirroredFromId === left?.id) value = 'left-to-right'
    if (left?.mirroredFromId === right?.id) value = 'right-to-left'
    return {
      ...pair,
      left,
      right,
      value,
      canUseReal: Boolean(left && right && !left.generatedMirror && !right.generatedMirror),
      canMirrorLeft: Boolean(left && !left.generatedMirror && right),
      canMirrorRight: Boolean(right && !right.generatedMirror && left),
    }
  }),
)
const maxFrames = computed(() =>
  Math.max(1, ...visibleActions.value.map((action) => framesForAction(action).length)),
)
const atlasFrameStats = computed(() => {
  let logical = 0
  const physical = new Set()
  for (const action of visibleActions.value) {
    const owner = resolveFrameOwner(action)
    framesForAction(action).forEach((_, index) => {
      logical += 1
      physical.add(`${owner.id}:${index}`)
    })
  }
  return { logical, physical: physical.size, saved: logical - physical.size }
})
const frameNumber = computed(() => frameIndex.value + 1)
const isBusy = computed(() => analysisProgress.value > 0 || exportProgress.value > 0)
const cropRatio = computed(() => `${crop.width} × ${crop.height}`)
const exportScaleFactor = computed(() => exportScale.value / 100)
const scaledCrop = computed(() => ({
  width: Math.max(1, Math.round(crop.width * exportScaleFactor.value)),
  height: Math.max(1, Math.round(crop.height * exportScaleFactor.value)),
}))
const compressionColorCount = computed(
  () =>
    ({
      lossless: 0,
      light: 4096,
      balanced: 1024,
      strong: 256,
    })[pngCompression.value] ?? 0,
)
const compressionDescription = computed(
  () =>
    ({
      lossless: '不改变颜色，文件体积最大。',
      light: '推荐档位。保留 4096 色，通常难以察觉差异。',
      balanced: '保留 1024 色，体积更小，渐变可能略有变化。',
      strong: '保留 256 色，压缩最明显，适合色彩简单的素材。',
    })[pngCompression.value] ?? '',
)
const previewDisplayScale = computed(() =>
  Math.min(1, 210 / scaledCrop.value.width, 230 / scaledCrop.value.height),
)
const previewDisplaySize = computed(() => ({
  width: Math.max(1, Math.round(scaledCrop.value.width * previewDisplayScale.value)),
  height: Math.max(1, Math.round(scaledCrop.value.height * previewDisplayScale.value)),
}))
const previewSavings = computed(() => {
  if (!previewOriginalBytes.value || pngCompression.value === 'lossless') return 0
  return Math.max(
    0,
    Math.round((1 - previewBytes.value / previewOriginalBytes.value) * 100),
  )
})

function resolveFrameOwner(action) {
  let owner = action
  const visited = new Set()
  while (owner?.mirroredFromId && !visited.has(owner.id)) {
    visited.add(owner.id)
    const source = actions.find((candidate) => candidate.id === owner.mirroredFromId)
    if (!source) break
    owner = source
  }
  return owner || action
}

function framesForAction(action) {
  return resolveFrameOwner(action)?.frames || []
}

function sourceFramesForAction(action) {
  return resolveFrameOwner(action)?.sourceFrames || []
}

function normalizeFrame(action) {
  const frames = framesForAction(action)
  if (!frames.length) return 0
  return frameIndex.value % frames.length
}

function setMirrorPolicy(pair, value) {
  const left = activeCollectionActions.value.find((action) => action.directionId === pair.leftId)
  const right = activeCollectionActions.value.find((action) => action.directionId === pair.rightId)
  if (!left || !right) return

  for (const action of [left, right]) {
    if (action.generatedMirror) continue
    action.mirroredFromId = null
    action.mirroredFromName = null
    action.flipX = false
  }

  if (value === 'left-to-right' && !left.generatedMirror) {
    right.mirroredFromId = left.id
    right.mirroredFromName = left.name
    right.flipX = true
  } else if (value === 'right-to-left' && !right.generatedMirror) {
    left.mirroredFromId = right.id
    left.mirroredFromName = right.name
    left.flipX = true
  }

  const selectedPair = mirrorPolicyRows.value.find((item) => item.id === pair.id)
  statusMessage.value =
    selectedPair?.value === 'real'
      ? `${pair.label}使用真实双方向`
      : `${pair.label}已启用运行时镜像，Unity 图集将复用纹理`
  persistState()
}

function loadImage(url) {
  if (!url) return Promise.reject(new Error('缺少图片地址'))
  if (imageCache.has(url)) return imageCache.get(url)
  const promise = new Promise((resolve, reject) => {
    const image = new Image()
    image.decoding = 'async'
    image.onload = () => resolve(image)
    image.onerror = () => reject(new Error(`无法载入 ${url}`))
    image.src = url
  })
  imageCache.set(url, promise)
  return promise
}

function loadTransientImage(url) {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.decoding = 'async'
    image.onload = () => resolve(image)
    image.onerror = () => reject(new Error(`无法载入 ${url}`))
    image.src = url
  })
}

function drawActionImage(ctx, image, action, x, y, width = image.naturalWidth, height = image.naturalHeight) {
  if (!action.flipX) {
    ctx.drawImage(image, x, y, width, height)
    return
  }
  ctx.save()
  ctx.translate(x + width, y)
  ctx.scale(-1, 1)
  ctx.drawImage(image, 0, 0, width, height)
  ctx.restore()
}

function motionTransform(action, time = motionTime.value) {
  const motion = action.motion
  if (!motion?.enabled) {
    return { x: 0, y: 0, rotation: 0, scale: 1 }
  }
  const duration = Math.max(0.2, Number(motion.duration) || 2.4)
  const phase = ((Number(motion.phase) || 0) * Math.PI) / 180
  const wave = Math.sin((time / duration) * Math.PI * 2 + phase)
  return {
    x: (Number(motion.moveX) || 0) * wave,
    y: (Number(motion.moveY) || 0) * wave,
    rotation: (Number(motion.rotation) || 0) * wave,
    scale: 1 + ((Number(motion.scale) || 0) / 100) * wave,
  }
}

function applyMotionTransform(ctx, action) {
  const transform = motionTransform(action)
  if (
    transform.x === 0 &&
    transform.y === 0 &&
    transform.rotation === 0 &&
    transform.scale === 1
  ) {
    return
  }
  const pivotX = crop.x + crop.width / 2
  const pivotY = crop.y + crop.height
  ctx.translate(pivotX + transform.x, pivotY + transform.y)
  ctx.rotate((transform.rotation * Math.PI) / 180)
  ctx.scale(transform.scale, transform.scale)
  ctx.translate(-pivotX, -pivotY)
}

function drawCroppedAction(ctx, image, action, x, y, scale, outputWidth, outputHeight) {
  const owner = resolveFrameOwner(action)
  const sourceX = (owner.offsetX - crop.x) * scale
  const sourceY = (owner.offsetY - crop.y) * scale
  const sourceWidth = image.naturalWidth * scale
  const sourceHeight = image.naturalHeight * scale

  ctx.save()
  ctx.beginPath()
  ctx.rect(x, y, outputWidth, outputHeight)
  ctx.clip()
  if (action.mirroredFromId) {
    ctx.translate(x + outputWidth, y)
    ctx.scale(-1, 1)
    ctx.drawImage(image, sourceX, sourceY, sourceWidth, sourceHeight)
  } else {
    ctx.drawImage(image, x + sourceX, y + sourceY, sourceWidth, sourceHeight)
  }
  ctx.restore()
}

function boundsForAction(action, bounds) {
  if (!bounds || !action.flipX) return bounds
  return {
    ...bounds,
    x: (bounds.sourceWidth || SOURCE_WIDTH) - bounds.x - bounds.width,
  }
}

async function drawStage() {
  const canvas = canvasRef.value
  if (!canvas) return
  const token = ++renderToken
  const dpr = Math.min(window.devicePixelRatio || 1, 2)
  if (canvas.width !== SOURCE_WIDTH * dpr || canvas.height !== SOURCE_HEIGHT * dpr) {
    canvas.width = SOURCE_WIDTH * dpr
    canvas.height = SOURCE_HEIGHT * dpr
  }
  const ctx = canvas.getContext('2d')
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, SOURCE_WIDTH, SOURCE_HEIGHT)

  const layers = [...visibleActions.value].sort((a, b) => {
    if (a.id === activeId.value) return 1
    if (b.id === activeId.value) return -1
    return 0
  })

  for (const action of layers) {
    const owner = resolveFrameOwner(action)
    const url = framesForAction(action)[normalizeFrame(action)]
    try {
      const image = await loadImage(url)
      if (token !== renderToken) return
      ctx.save()
      ctx.globalAlpha = action.id === activeId.value ? 1 : action.opacity
      applyMotionTransform(ctx, action)
      drawActionImage(ctx, image, action, owner.offsetX, owner.offsetY)
      ctx.restore()
    } catch {
      statusMessage.value = `载入失败：${action.name}`
    }
  }

  drawCropOverlay(ctx)
}

function drawCropOverlay(ctx) {
  ctx.save()
  ctx.fillStyle = 'rgba(8, 9, 8, 0.62)'
  ctx.beginPath()
  ctx.rect(0, 0, SOURCE_WIDTH, SOURCE_HEIGHT)
  ctx.rect(crop.x, crop.y, crop.width, crop.height)
  ctx.fill('evenodd')

  ctx.strokeStyle = '#d5ff53'
  ctx.lineWidth = 2
  ctx.setLineDash([])
  ctx.strokeRect(crop.x, crop.y, crop.width, crop.height)

  if (showGuides.value) {
    ctx.strokeStyle = 'rgba(213, 255, 83, 0.44)'
    ctx.lineWidth = 1
    ctx.setLineDash([5, 5])
    const centerX = crop.x + crop.width / 2
    const centerY = crop.y + crop.height / 2
    ctx.beginPath()
    ctx.moveTo(centerX, crop.y)
    ctx.lineTo(centerX, crop.y + crop.height)
    ctx.moveTo(crop.x, centerY)
    ctx.lineTo(crop.x + crop.width, centerY)
    ctx.stroke()
  }

  ctx.setLineDash([])
  ctx.fillStyle = '#d5ff53'
  const size = 8
  for (const [x, y] of handlePoints()) {
    ctx.fillRect(x - size / 2, y - size / 2, size, size)
  }
  ctx.restore()
}

function handlePoints() {
  const left = crop.x
  const right = crop.x + crop.width
  const top = crop.y
  const bottom = crop.y + crop.height
  const midX = left + crop.width / 2
  const midY = top + crop.height / 2
  return [
    [left, top, 'nw'],
    [midX, top, 'n'],
    [right, top, 'ne'],
    [right, midY, 'e'],
    [right, bottom, 'se'],
    [midX, bottom, 's'],
    [left, bottom, 'sw'],
    [left, midY, 'w'],
  ]
}

function pointerPosition(event) {
  const rect = canvasRef.value.getBoundingClientRect()
  return {
    x: ((event.clientX - rect.left) / rect.width) * SOURCE_WIDTH,
    y: ((event.clientY - rect.top) / rect.height) * SOURCE_HEIGHT,
  }
}

function hitTest(point) {
  const threshold = 12 / Math.max(zoom.value, 0.5)
  for (const [x, y, handle] of handlePoints()) {
    if (Math.abs(point.x - x) <= threshold && Math.abs(point.y - y) <= threshold) return handle
  }
  const inside =
    point.x >= crop.x &&
    point.x <= crop.x + crop.width &&
    point.y >= crop.y &&
    point.y <= crop.y + crop.height
  return inside ? 'move' : 'new'
}

function onPointerDown(event) {
  if (isBusy.value) return
  const point = pointerPosition(event)
  interaction = {
    type: hitTest(point),
    start: point,
    original: { ...crop },
  }
  canvasRef.value.setPointerCapture(event.pointerId)
  if (interaction.type === 'new') {
    crop.x = Math.round(Math.max(0, Math.min(SOURCE_WIDTH - MIN_CROP, point.x)))
    crop.y = Math.round(Math.max(0, Math.min(SOURCE_HEIGHT - MIN_CROP, point.y)))
    crop.width = MIN_CROP
    crop.height = MIN_CROP
    interaction.original = { ...crop }
  }
}

function onPointerMove(event) {
  if (!interaction) return
  const point = pointerPosition(event)
  const dx = point.x - interaction.start.x
  const dy = point.y - interaction.start.y
  const original = interaction.original
  const type = interaction.type

  if (type === 'move') {
    crop.x = Math.round(clamp(original.x + dx, 0, SOURCE_WIDTH - original.width))
    crop.y = Math.round(clamp(original.y + dy, 0, SOURCE_HEIGHT - original.height))
  } else if (type === 'new') {
    crop.width = Math.round(clamp(point.x - original.x, MIN_CROP, SOURCE_WIDTH - original.x))
    crop.height = Math.round(clamp(point.y - original.y, MIN_CROP, SOURCE_HEIGHT - original.y))
  } else {
    let left = original.x
    let right = original.x + original.width
    let top = original.y
    let bottom = original.y + original.height
    if (type.includes('w')) left = clamp(original.x + dx, 0, right - MIN_CROP)
    if (type.includes('e')) right = clamp(right + dx, left + MIN_CROP, SOURCE_WIDTH)
    if (type.includes('n')) top = clamp(original.y + dy, 0, bottom - MIN_CROP)
    if (type.includes('s')) bottom = clamp(bottom + dy, top + MIN_CROP, SOURCE_HEIGHT)
    crop.x = Math.round(left)
    crop.y = Math.round(top)
    crop.width = Math.round(right - left)
    crop.height = Math.round(bottom - top)
  }
  drawStage()
}

function onPointerUp(event) {
  if (!interaction) return
  interaction = null
  canvasRef.value.releasePointerCapture(event.pointerId)
  persistState()
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value))
}

function safeExportName(value, fallback = '未命名') {
  const name = String(value || fallback)
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_')
    .replace(/[.;\s]+$/g, '')
    .trim()
  return name || fallback
}

function safeSheetValue(value) {
  return String(value || '').replace(/[;\r\n]/g, '_')
}

function sanitizeCrop() {
  crop.width = Math.round(clamp(Number(crop.width) || MIN_CROP, MIN_CROP, SOURCE_WIDTH))
  crop.height = Math.round(clamp(Number(crop.height) || MIN_CROP, MIN_CROP, SOURCE_HEIGHT))
  crop.x = Math.round(clamp(Number(crop.x) || 0, 0, SOURCE_WIDTH - crop.width))
  crop.y = Math.round(clamp(Number(crop.y) || 0, 0, SOURCE_HEIGHT - crop.height))
  persistState()
}

function setFrame(index) {
  const length = maxFrames.value
  frameIndex.value = ((index % length) + length) % length
}

function togglePlayback() {
  isPlaying.value = !isPlaying.value
}

function restartPlayback() {
  stopTimer()
  if (!isPlaying.value) return
  playbackTimer = window.setInterval(() => {
    if (!loop.value && frameIndex.value >= maxFrames.value - 1) {
      isPlaying.value = false
      return
    }
    setFrame(frameIndex.value + 1)
  }, 1000 / activeFps.value)
}

function stopTimer() {
  if (playbackTimer) window.clearInterval(playbackTimer)
  playbackTimer = null
}

function stopMotionLoop() {
  if (motionAnimationFrame) window.cancelAnimationFrame(motionAnimationFrame)
  motionAnimationFrame = null
  motionLastTimestamp = null
}

function motionTick(timestamp) {
  if (!isPlaying.value || !hasVisibleMotion.value) {
    stopMotionLoop()
    return
  }
  if (motionLastTimestamp !== null) {
    const delta = Math.min(0.05, Math.max(0, (timestamp - motionLastTimestamp) / 1000))
    motionTime.value += delta
  }
  motionLastTimestamp = timestamp
  drawStage()
  motionAnimationFrame = window.requestAnimationFrame(motionTick)
}

function restartMotionLoop() {
  stopMotionLoop()
  if (isPlaying.value && hasVisibleMotion.value) {
    motionAnimationFrame = window.requestAnimationFrame(motionTick)
  }
}

function sanitizeMotion() {
  const motion = activeMotion.value
  if (!motion) return
  motion.moveX = Math.round(clamp(Number(motion.moveX) || 0, 0, 240) * 10) / 10
  motion.moveY = Math.round(clamp(Number(motion.moveY) || 0, 0, 240) * 10) / 10
  motion.rotation = Math.round(clamp(Number(motion.rotation) || 0, 0, 45) * 10) / 10
  motion.scale = Math.round(clamp(Number(motion.scale) || 0, 0, 50) * 10) / 10
  motion.duration = Math.round(clamp(Number(motion.duration) || 2.4, 0.2, 20) * 10) / 10
  motion.phase = Math.round(clamp(Number(motion.phase) || 0, 0, 360))
  persistState()
}

function applyMotionPreset(preset) {
  const presets = {
    drift: { enabled: true, moveX: 12, moveY: 0, rotation: 0, scale: 0, duration: 3.2, phase: 0 },
    float: { enabled: true, moveX: 0, moveY: 8, rotation: 0, scale: 0, duration: 2.6, phase: 0 },
    sway: { enabled: true, moveX: 0, moveY: 0, rotation: 3.5, scale: 0, duration: 2.2, phase: 0 },
    breathe: { enabled: true, moveX: 0, moveY: 0, rotation: 0, scale: 3, duration: 1.8, phase: 0 },
  }
  Object.assign(activeMotion.value, presets[preset] || createDefaultMotion())
  motionTime.value = 0
  statusMessage.value = preset === 'reset' ? '已清除当前动作动态' : `已应用动态预设：${activeAction.value.name}`
  persistState()
}

function serializeMotion(action, scale = 1) {
  const motion = action.motion || createDefaultMotion()
  return {
    enabled: Boolean(motion.enabled),
    wave: 'sine',
    pivot: { x: 0.5, y: 0 },
    position: {
      x: Math.round((Number(motion.moveX) || 0) * scale * 1000) / 1000,
      y: Math.round((Number(motion.moveY) || 0) * scale * 1000) / 1000,
    },
    rotationDegrees: Number(motion.rotation) || 0,
    scalePercent: Number(motion.scale) || 0,
    durationSeconds: Number(motion.duration) || 2.4,
    phaseDegrees: Number(motion.phase) || 0,
  }
}

function createMotionManifest(targetActions, scale = 1) {
  return {
    schema: 'octant.motion.v1',
    bakedIntoFrames: false,
    units: 'output-pixels',
    outputScale: Math.round(scale * 100),
    actions: targetActions.map((action) => ({
      id: action.id,
      project: action.projectName,
      character: action.characterName,
      name: action.name,
      ...serializeMotion(action, scale),
    })),
  }
}

function activateAction(action) {
  activeId.value = action.id
  if (!action.visible) action.visible = true
}

function toggleAction(action) {
  action.visible = !action.visible
  if (action.visible) activeId.value = action.id
  if (!visibleActions.value.length) {
    action.visible = true
    activeId.value = action.id
  } else if (!action.visible && action.id === activeId.value) {
    activeId.value = visibleActions.value[0].id
  }
}

function toggleAllActions() {
  const shouldSelectAll = visibleActions.value.length !== actions.length
  for (const action of actions) {
    action.visible = shouldSelectAll ? true : action.id === activeId.value
  }
  statusMessage.value = shouldSelectAll
    ? `已选中全部 ${actions.length} 个动作`
    : `仅显示 ${activeAction.value.name}`
}

function toggleProject(projectId) {
  if (collapsedProjects.has(projectId)) collapsedProjects.delete(projectId)
  else collapsedProjects.add(projectId)
}

function toggleCharacter(characterId) {
  if (collapsedCharacters.has(characterId)) collapsedCharacters.delete(characterId)
  else collapsedCharacters.add(characterId)
}

function renameProject(project) {
  const name = window.prompt('项目名称', project.name)?.trim()
  if (!name || name === project.name) return
  for (const action of actions.filter((item) => item.projectId === project.id)) {
    action.projectName = name
    action.collectionName = `${name} / ${action.characterName}`
  }
  statusMessage.value = `项目已重命名为 ${name}`
  persistState()
}

function renameCharacter(character) {
  const name = window.prompt('人物名称', character.name)?.trim()
  if (!name || name === character.name) return
  for (const action of actions.filter((item) => item.characterId === character.id)) {
    action.characterName = name
    action.collectionName = `${action.projectName} / ${name}`
  }
  statusMessage.value = `人物已重命名为 ${name}`
  persistState()
}

function sanitizeThinInterval() {
  thinInterval.value = Math.round(clamp(Number(thinInterval.value) || 2, 2, 60))
  persistState()
}

function sanitizeExportScale() {
  exportScale.value = Math.round(clamp(Number(exportScale.value) || 100, 25, 100) / 5) * 5
  persistState()
}

function formatFileSize(bytes) {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function paintExportPreview() {
  const canvas = exportPreviewCanvasRef.value
  const source = showOriginalPreview.value ? rawPreviewCanvas : processedPreviewCanvas
  if (!canvas || !source) return
  if (canvas.width !== source.width || canvas.height !== source.height) {
    canvas.width = source.width
    canvas.height = source.height
  }
  const ctx = canvas.getContext('2d')
  ctx.clearRect(0, 0, canvas.width, canvas.height)
  ctx.drawImage(source, 0, 0)
}

function encodePreviewPng(canvas) {
  const ctx = canvas.getContext('2d', { willReadFrequently: true })
  const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height)
  const encoded = UPNG.encode(
    [pixels.data.buffer],
    canvas.width,
    canvas.height,
    compressionColorCount.value,
  )
  return new Blob([encoded], { type: 'image/png' })
}

async function renderExportPreview(token) {
  const action = activeAction.value
  const canvas = exportPreviewCanvasRef.value
  if (!action?.frames.length || !canvas) return
  previewStatus.value = '正在生成'
  previewBytes.value = 0
  previewOriginalBytes.value = 0
  const scale = exportScaleFactor.value
  const rawCanvas = document.createElement('canvas')
  rawCanvas.width = scaledCrop.value.width
  rawCanvas.height = scaledCrop.value.height
  const ctx = rawCanvas.getContext('2d', { alpha: true })
  ctx.imageSmoothingEnabled = scale !== 1
  ctx.imageSmoothingQuality = 'high'

  try {
    const image = await loadImage(framesForAction(action)[normalizeFrame(action)])
    if (token !== previewRenderToken) return
    ctx.clearRect(0, 0, rawCanvas.width, rawCanvas.height)
    drawCroppedAction(
      ctx,
      image,
      action,
      0,
      0,
      scale,
      rawCanvas.width,
      rawCanvas.height,
    )

    const processedBlob =
      pngCompression.value === 'lossless'
        ? await canvasToPngBlob(rawCanvas)
        : encodePreviewPng(rawCanvas)
    if (token !== previewRenderToken) return

    const processedCanvas = document.createElement('canvas')
    processedCanvas.width = rawCanvas.width
    processedCanvas.height = rawCanvas.height
    if (pngCompression.value === 'lossless') {
      processedCanvas.getContext('2d').drawImage(rawCanvas, 0, 0)
    } else {
      const bitmap = await createImageBitmap(processedBlob)
      processedCanvas.getContext('2d').drawImage(bitmap, 0, 0)
      bitmap.close()
    }
    if (token !== previewRenderToken) return

    rawPreviewCanvas = rawCanvas
    processedPreviewCanvas = processedCanvas
    previewBytes.value = processedBlob.size
    if (pngCompression.value === 'lossless') previewOriginalBytes.value = processedBlob.size
    previewStatus.value = '实时'
    paintExportPreview()

    if (pngCompression.value !== 'lossless') {
      const losslessBlob = await canvasToPngBlob(rawCanvas)
      if (token === previewRenderToken) previewOriginalBytes.value = losslessBlob.size
    }
  } catch (error) {
    if (token !== previewRenderToken) return
    previewStatus.value = '预览失败'
  }
}

function scheduleExportPreview() {
  window.clearTimeout(previewTimer)
  const token = ++previewRenderToken
  previewStatus.value = '正在更新'
  previewTimer = window.setTimeout(() => renderExportPreview(token), 50)
}

function thinFrames(scope = 'visible') {
  sanitizeThinInterval()
  const requestedActions = scope === 'active' ? [activeAction.value] : visibleActions.value
  const targetActions = [...new Map(
    requestedActions.map((action) => {
      const owner = resolveFrameOwner(action)
      return [owner.id, owner]
    }),
  ).values()]
  let removed = 0

  for (const action of targetActions) {
    const previousCount = action.frames.length
    const nextFrames = action.frames.filter(
      (_, index) => (index + 1) % thinInterval.value !== 0,
    )
    removed += previousCount - nextFrames.length
    action.frames = nextFrames
  }

  setFrame(Math.min(frameIndex.value, maxFrames.value - 1))
  statusMessage.value = removed
    ? `已基于当前序列继续减帧，共移除 ${removed} 帧`
    : `当前帧数少于 ${thinInterval.value}，没有可移除的帧`
  persistState()
}

function restoreFrames(scope = 'visible') {
  const requestedActions = scope === 'active' ? [activeAction.value] : visibleActions.value
  const targetActions = [...new Map(
    requestedActions.map((action) => {
      const owner = resolveFrameOwner(action)
      return [owner.id, owner]
    }),
  ).values()]
  for (const action of targetActions) {
    action.frames.splice(0, action.frames.length, ...action.sourceFrames)
  }
  setFrame(Math.min(frameIndex.value, maxFrames.value - 1))
  statusMessage.value = `已恢复 ${targetActions.length} 个动作的原始帧`
  persistState()
}

function nudgeActive(dx, dy) {
  activeFrameOwner.value.offsetX = Math.round(activeFrameOwner.value.offsetX + dx)
  activeFrameOwner.value.offsetY = Math.round(activeFrameOwner.value.offsetY + dy)
  persistState()
}

function resetOffsets() {
  for (const action of actions) {
    action.offsetX = 0
    action.offsetY = 0
  }
  statusMessage.value = '已重置全部动作偏移'
  persistState()
}

async function getAlphaBounds(url) {
  if (boundsCache.has(url)) return boundsCache.get(url)
  const image = await loadImage(url)
  const work = document.createElement('canvas')
  work.width = image.naturalWidth || image.width
  work.height = image.naturalHeight || image.height
  const ctx = work.getContext('2d', { willReadFrequently: true })
  ctx.drawImage(image, 0, 0)
  const { data, width, height } = ctx.getImageData(0, 0, work.width, work.height)
  const visited = new Uint8Array(width * height)
  const stack = new Int32Array(width * height)
  let largest = null

  for (let index = 0; index < width * height; index += 1) {
    if (visited[index] || data[index * 4 + 3] <= 8) continue
    let stackSize = 1
    let pixels = 0
    let minX = width
    let minY = height
    let maxX = -1
    let maxY = -1
    stack[0] = index
    visited[index] = 1

    while (stackSize > 0) {
      const current = stack[--stackSize]
      const x = current % width
      const y = Math.floor(current / width)
      pixels += 1
      if (x < minX) minX = x
      if (x > maxX) maxX = x
      if (y < minY) minY = y
      if (y > maxY) maxY = y

      let neighbor
      if (x > 0) {
        neighbor = current - 1
        if (!visited[neighbor] && data[neighbor * 4 + 3] > 8) {
          visited[neighbor] = 1
          stack[stackSize++] = neighbor
        }
      }
      if (x < width - 1) {
        neighbor = current + 1
        if (!visited[neighbor] && data[neighbor * 4 + 3] > 8) {
          visited[neighbor] = 1
          stack[stackSize++] = neighbor
        }
      }
      if (y > 0) {
        neighbor = current - width
        if (!visited[neighbor] && data[neighbor * 4 + 3] > 8) {
          visited[neighbor] = 1
          stack[stackSize++] = neighbor
        }
      }
      if (y < height - 1) {
        neighbor = current + width
        if (!visited[neighbor] && data[neighbor * 4 + 3] > 8) {
          visited[neighbor] = 1
          stack[stackSize++] = neighbor
        }
      }
    }

    if (!largest || pixels > largest.pixels) {
      largest = {
        x: minX,
        y: minY,
        width: maxX - minX + 1,
        height: maxY - minY + 1,
        pixels,
      }
    }
  }

  const bounds = largest
    ? {
        x: largest.x,
        y: largest.y,
        width: largest.width,
        height: largest.height,
        sourceWidth: width,
      }
    : null
  boundsCache.set(url, bounds)
  return bounds
}

async function autoCrop(scope = 'visible') {
  if (isBusy.value) return
  const targetActions = scope === 'active' ? [activeAction.value] : visibleActions.value
  const jobs = targetActions.flatMap((action) =>
    framesForAction(action).map((url) => ({ action, url })),
  )
  if (!jobs.length) return
  analysisProgress.value = 1
  statusMessage.value = '正在分析透明边界'
  let left = SOURCE_WIDTH
  let top = SOURCE_HEIGHT
  let right = 0
  let bottom = 0

  try {
    for (let i = 0; i < jobs.length; i += 1) {
      const { action, url } = jobs[i]
      const bounds = boundsForAction(action, await getAlphaBounds(url))
      const owner = resolveFrameOwner(action)
      if (bounds) {
        left = Math.min(left, bounds.x + owner.offsetX)
        top = Math.min(top, bounds.y + owner.offsetY)
        right = Math.max(right, bounds.x + bounds.width + owner.offsetX)
        bottom = Math.max(bottom, bounds.y + bounds.height + owner.offsetY)
      }
      analysisProgress.value = Math.round(((i + 1) / jobs.length) * 100)
      if (i % 6 === 0) await new Promise((resolve) => requestAnimationFrame(resolve))
    }
    const padding = 12
    crop.x = Math.round(clamp(left - padding, 0, SOURCE_WIDTH - MIN_CROP))
    crop.y = Math.round(clamp(top - padding, 0, SOURCE_HEIGHT - MIN_CROP))
    crop.width = Math.round(clamp(right - left + padding * 2, MIN_CROP, SOURCE_WIDTH - crop.x))
    crop.height = Math.round(clamp(bottom - top + padding * 2, MIN_CROP, SOURCE_HEIGHT - crop.y))
    statusMessage.value = `已按 ${targetActions.length} 个动作紧裁`
    persistState()
  } catch (error) {
    statusMessage.value = `分析失败：${error.message}`
  } finally {
    analysisProgress.value = 0
  }
}

async function alignVisibleToActive() {
  if (isBusy.value || visibleActions.value.length < 2) return
  analysisProgress.value = 1
  statusMessage.value = '正在对齐当前帧'
  try {
    const reference = boundsForAction(
      activeAction.value,
      await getAlphaBounds(framesForAction(activeAction.value)[normalizeFrame(activeAction.value)]),
    )
    if (!reference) return
    const referenceOwner = resolveFrameOwner(activeAction.value)
    const refCenter = reference.x + reference.width / 2 + referenceOwner.offsetX
    const refBottom = reference.y + reference.height + referenceOwner.offsetY

    for (let index = 0; index < visibleActions.value.length; index += 1) {
      const action = visibleActions.value[index]
      if (action.id === activeId.value) continue
      const owner = resolveFrameOwner(action)
      if (owner.id === referenceOwner.id) continue
      const bounds = boundsForAction(
        action,
        await getAlphaBounds(framesForAction(action)[normalizeFrame(action)]),
      )
      if (!bounds) continue
      owner.offsetX = Math.round(refCenter - (bounds.x + bounds.width / 2))
      owner.offsetY = Math.round(refBottom - (bounds.y + bounds.height))
      analysisProgress.value = Math.round(((index + 1) / visibleActions.value.length) * 100)
    }
    statusMessage.value = '已按当前帧中心与脚底对齐'
    persistState()
  } catch (error) {
    statusMessage.value = `对齐失败：${error.message}`
  } finally {
    analysisProgress.value = 0
  }
}

function openFolderPicker() {
  fileInputRef.value?.click()
}

async function importFolders(event) {
  const imported = createImportedActions(event.target.files)
  if (!imported.length) {
    statusMessage.value = '没有找到可导入的图片'
    return
  }

  const existingProjectIds = new Map(
    actions.map((action) => [action.projectName, action.projectId]),
  )
  const existingCharacterIds = new Map(
    actions.map((action) => [
      JSON.stringify([action.projectName, action.characterName]),
      action.characterId,
    ]),
  )
  for (const action of imported) {
    if (existingProjectIds.has(action.projectName)) {
      action.projectId = existingProjectIds.get(action.projectName)
    } else {
      existingProjectIds.set(action.projectName, action.projectId)
    }
    const characterKey = JSON.stringify([action.projectName, action.characterName])
    if (existingCharacterIds.has(characterKey)) {
      action.characterId = existingCharacterIds.get(characterKey)
    } else {
      existingCharacterIds.set(characterKey, action.characterId)
    }
    action.collectionId = action.characterId
  }

  for (const action of imported) actions.push(reactive(action))
  activateAction(imported[0])
  const mirrorCount = imported.filter((action) => action.generatedMirror).length
  const realActions = imported.filter((action) => !action.generatedMirror)
  const projectCount = new Set(realActions.map((action) => action.projectId)).size
  const characterCount = new Set(realActions.map((action) => action.characterId)).size
  statusMessage.value = mirrorCount
    ? `已导入 ${projectCount} 项目、${characterCount} 人物、${realActions.length} 个动作，镜像补齐 ${mirrorCount} 个方向`
    : `已导入 ${projectCount} 项目、${characterCount} 人物、${realActions.length} 个动作`
  event.target.value = ''
}

async function exportSelected() {
  if (isBusy.value || !visibleActions.value.length) return
  exportKind.value = 'frames'
  exportProgress.value = 1
  isPlaying.value = false
  statusMessage.value = '正在生成裁剪序列'
  const zip = new JSZip()
  const exportCanvas = document.createElement('canvas')
  const scale = exportScaleFactor.value
  exportCanvas.width = scaledCrop.value.width
  exportCanvas.height = scaledCrop.value.height
  const ctx = exportCanvas.getContext('2d')
  ctx.imageSmoothingEnabled = scale !== 1
  ctx.imageSmoothingQuality = 'high'
  const total = visibleActions.value.reduce(
    (sum, action) => sum + framesForAction(action).length,
    0,
  )
  let completed = 0
  let pngBytes = 0

  try {
    for (const action of visibleActions.value) {
      const folder = zip
        .folder(safeExportName(action.projectName, '默认项目'))
        .folder(safeExportName(action.characterName, '未命名人物'))
        .folder(safeExportName(action.name, '未命名动作'))
      const actionFrames = framesForAction(action)
      for (let index = 0; index < actionFrames.length; index += 1) {
        const image = await loadImage(actionFrames[index])
        ctx.clearRect(0, 0, exportCanvas.width, exportCanvas.height)
        drawCroppedAction(
          ctx,
          image,
          action,
          0,
          0,
          scale,
          exportCanvas.width,
          exportCanvas.height,
        )
        const blob = await encodeCanvasPng(exportCanvas)
        pngBytes += blob.size
        folder.file(`${action.name}_${String(index + 1).padStart(3, '0')}.png`, blob)
        completed += 1
        exportProgress.value = Math.round((completed / total) * 85)
        if (completed % 5 === 0) await new Promise((resolve) => requestAnimationFrame(resolve))
      }
    }
    zip.file(
      'crop.json',
      JSON.stringify(
        {
          source: { width: SOURCE_WIDTH, height: SOURCE_HEIGHT },
          crop: { ...crop },
          output: {
            width: scaledCrop.value.width,
            height: scaledCrop.value.height,
            scale: exportScale.value,
            compression: pngCompression.value,
          },
          fps: activeFps.value,
          actions: visibleActions.value.map((action) => {
            const owner = resolveFrameOwner(action)
            return {
              project: action.projectName,
              character: action.characterName,
              name: action.name,
              fps: action.fps || DEFAULT_FPS,
              offsetX: owner.offsetX,
              offsetY: owner.offsetY,
              frames: framesForAction(action).length,
              mirroredFrom: action.mirroredFromName || null,
              bakedFlipX: Boolean(action.mirroredFromId),
              motion: serializeMotion(action, scale),
            }
          }),
        },
        null,
        2,
      ),
    )
    const blob = await zip.generateAsync(
      { type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 6 } },
      ({ percent }) => {
        exportProgress.value = Math.round(85 + percent * 0.15)
      },
    )
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `序列裁剪_${scaledCrop.value.width}x${scaledCrop.value.height}.zip`
    link.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
    statusMessage.value = `已导出 ${visibleActions.value.length} 个动作 · PNG ${formatFileSize(pngBytes)}`
  } catch (error) {
    statusMessage.value = `导出失败：${error.message}`
  } finally {
    exportProgress.value = 0
    exportKind.value = ''
  }
}

function createAtlasLayout(frameCount, frameWidth, frameHeight, padding = 2) {
  const maxTextureSize = 8192
  const cellWidth = frameWidth + padding * 2
  const cellHeight = frameHeight + padding * 2
  const maxColumns = Math.floor(maxTextureSize / cellWidth)
  const minColumns = Math.ceil(frameCount / Math.floor(maxTextureSize / cellHeight))
  const idealColumns = Math.ceil(Math.sqrt((frameCount * cellHeight) / cellWidth))
  const columns = clamp(idealColumns, Math.max(1, minColumns), Math.max(1, maxColumns))
  const rows = Math.ceil(frameCount / columns)
  const width = columns * cellWidth
  const height = rows * cellHeight

  if (width > maxTextureSize || height > maxTextureSize) {
    throw new Error('图集超过 Unity 常用的 8192 像素限制，请缩小裁剪框或减少动作')
  }

  return { columns, rows, width, height, cellWidth, cellHeight, padding }
}

async function canvasToPngBlob(canvas) {
  const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/png'))
  if (!blob) throw new Error('浏览器无法生成图集 PNG')
  return blob
}

async function encodeCanvasPng(canvas) {
  if (pngCompression.value === 'lossless') {
    return canvasToPngBlob(canvas)
  }

  await new Promise((resolve) => requestAnimationFrame(resolve))
  const ctx = canvas.getContext('2d', { willReadFrequently: true })
  const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height)
  const encoded = UPNG.encode(
    [pixels.data.buffer],
    canvas.width,
    canvas.height,
    compressionColorCount.value,
  )
  return new Blob([encoded], { type: 'image/png' })
}

async function exportUnityAtlas() {
  if (isBusy.value || !visibleActions.value.length) return
  exportKind.value = 'atlas'
  exportProgress.value = 1
  isPlaying.value = false

  const selected = [...visibleActions.value]
  const scale = exportScaleFactor.value
  const outputWidth = scaledCrop.value.width
  const outputHeight = scaledCrop.value.height
  const logicalFrames = selected.flatMap((action) => {
    const owner = resolveFrameOwner(action)
    return framesForAction(action).map((url, index) => ({
      action,
      owner,
      url,
      index,
      physicalKey: `${owner.id}:${index}`,
    }))
  })
  const physicalFrameMap = new Map()
  for (const frame of logicalFrames) {
    if (!physicalFrameMap.has(frame.physicalKey)) {
      physicalFrameMap.set(frame.physicalKey, {
        action: frame.owner,
        url: frame.url,
        index: frame.index,
        physicalKey: frame.physicalKey,
      })
    }
  }
  const physicalFrames = [...physicalFrameMap.values()]
  const layout = createAtlasLayout(
    physicalFrames.length,
    outputWidth,
    outputHeight,
    Math.max(1, Math.round(2 * scale)),
  )
  const atlasName = 'character_walk'
  const textureName = `${atlasName}.png`
  const sheetName = `${atlasName}.tpsheet`
  const atlas = document.createElement('canvas')
  atlas.width = layout.width
  atlas.height = layout.height
  const ctx = atlas.getContext('2d', { alpha: true })
  ctx.imageSmoothingEnabled = scale !== 1
  ctx.imageSmoothingQuality = 'high'
  ctx.clearRect(0, 0, atlas.width, atlas.height)
  const sheetLines = [
    ':format=40300',
    `:texture=${textureName}`,
    `:size=${layout.width}x${layout.height}`,
    `:fps=${activeFps.value}`,
  ]
  for (const action of selected) {
    const actionFps = action.fps || DEFAULT_FPS
    sheetLines.push(
      `:action-fps=${safeSheetValue(action.id)};${actionFps}`,
    )
    sheetLines.push(
      `# action;${safeSheetValue(action.id)};${safeSheetValue(action.projectName)};${safeSheetValue(action.characterName)};${safeSheetValue(action.name)};fps=${actionFps}`,
    )
  }
  for (const action of selected.filter((item) => item.mirroredFromId)) {
    const owner = resolveFrameOwner(action)
    sheetLines.push(`# mirror;${action.id};${owner.id};flipX=true`)
  }
  for (const action of selected.filter((item) => item.motion?.enabled)) {
    const motion = serializeMotion(action, scale)
    sheetLines.push(
      `# motion;${safeSheetValue(action.id)};x=${motion.position.x};y=${motion.position.y};rotation=${motion.rotationDegrees};scale=${motion.scalePercent};duration=${motion.durationSeconds};phase=${motion.phaseDegrees};wave=sine;pivot=0.5,0`,
    )
  }

  statusMessage.value = `正在排版 ${physicalFrames.length} 个实体帧，${logicalFrames.length} 个逻辑帧`

  try {
    const rectByPhysicalKey = new Map()
    for (let flatIndex = 0; flatIndex < physicalFrames.length; flatIndex += 1) {
      const { action, url, physicalKey } = physicalFrames[flatIndex]
      const image = await loadTransientImage(url)
      const column = flatIndex % layout.columns
      const row = Math.floor(flatIndex / layout.columns)
      const x = column * layout.cellWidth + layout.padding
      const y = row * layout.cellHeight + layout.padding
      drawCroppedAction(
        ctx,
        image,
        action,
        x,
        y,
        scale,
        outputWidth,
        outputHeight,
      )
      image.src = ''
      rectByPhysicalKey.set(physicalKey, { x, y })

      exportProgress.value = Math.round(
        ((flatIndex + 1) / physicalFrames.length) * 82,
      )
      if (flatIndex % 6 === 0) {
        await new Promise((resolve) => requestAnimationFrame(resolve))
      }
    }

    for (const frame of logicalFrames) {
      const rect = rectByPhysicalKey.get(frame.physicalKey)
      const spriteName = `${frame.action.id.replaceAll('-', '_')}_${String(frame.index + 1).padStart(3, '0')}`
      sheetLines.push(
        `${spriteName};${rect.x};${rect.y};${outputWidth};${outputHeight};0.5;0`,
      )
    }

    statusMessage.value = `正在编码 ${layout.width} × ${layout.height} 图集`
    exportProgress.value = 86
    const pngBlob = await encodeCanvasPng(atlas)
    exportProgress.value = 94

    const zip = new JSZip()
    zip.file(textureName, pngBlob, { compression: 'STORE' })
    zip.file(sheetName, `${sheetLines.join('\n')}\n`)
    zip.file(
      `${atlasName}.motion.json`,
      JSON.stringify(createMotionManifest(selected, scale), null, 2),
    )
    const zipBlob = await zip.generateAsync(
      { type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 6 } },
      ({ percent }) => {
        exportProgress.value = Math.round(94 + percent * 0.06)
      },
    )
    const url = URL.createObjectURL(zipBlob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${atlasName}_${selected.length}actions_${logicalFrames.length}frames_${physicalFrames.length}packed_${exportScale.value}pct.zip`
    link.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
    statusMessage.value = `已导出 Unity 图集：${logicalFrames.length} 逻辑帧，${physicalFrames.length} 实体帧 · ${layout.width} × ${layout.height} · PNG ${formatFileSize(pngBlob.size)}`
  } catch (error) {
    statusMessage.value = `Unity 图集导出失败：${error.message}`
  } finally {
    atlas.width = 1
    atlas.height = 1
    exportProgress.value = 0
    exportKind.value = ''
  }
}

function persistState() {
  const serializable = {
    crop: { ...crop },
    fps: activeFps.value,
    loop: loop.value,
    stageBackground: stageBackground.value,
    showGuides: showGuides.value,
    exportScale: exportScale.value,
    pngCompression: pngCompression.value,
    thinInterval: thinInterval.value,
    actions: actions
      .filter((action) => !action.imported)
      .map(({
        id,
        offsetX,
        offsetY,
        motion,
        fps,
        opacity,
        visible,
        frames,
        sourceFrames,
        flipX,
        mirroredFromId,
        mirroredFromName,
        projectId,
        projectName,
        characterId,
        characterName,
        collectionId,
        collectionName,
      }) => ({
        id,
        offsetX,
        offsetY,
        motion: { ...createDefaultMotion(), ...(motion || {}) },
        fps,
        opacity,
        visible,
        flipX,
        mirroredFromId: mirroredFromId || null,
        mirroredFromName: mirroredFromName || null,
        projectId,
        projectName,
        characterId,
        characterName,
        collectionId,
        collectionName,
        keptFrameIndexes: frames
          .map((frame) => sourceFrames.indexOf(frame))
          .filter((index) => index >= 0),
      })),
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(serializable))
}

function restoreState() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY))
    if (!saved) return
    Object.assign(crop, saved.crop || {})
    const legacyFps = saved.fps || DEFAULT_FPS
    loop.value = saved.loop ?? true
    stageBackground.value = saved.stageBackground || 'checker'
    showGuides.value = saved.showGuides ?? true
    exportScale.value = saved.exportScale || 100
    pngCompression.value = saved.pngCompression || 'lossless'
    thinInterval.value = saved.thinInterval || 2
    for (const item of saved.actions || []) {
      const action = actions.find((candidate) => candidate.id === item.id)
      if (action) {
        const { keptFrameIndexes, ...settings } = item
        Object.assign(action, settings)
        if (!item.fps) action.fps = legacyFps
        if (Array.isArray(keptFrameIndexes) && keptFrameIndexes.length) {
          action.frames = keptFrameIndexes
            .map((index) => action.sourceFrames[index])
            .filter(Boolean)
        }
      }
    }
    if (!activeAction.value.visible) {
      const firstVisible = actions.find((action) => action.visible)
      if (firstVisible) {
        activeId.value = firstVisible.id
      } else {
        activeAction.value.visible = true
      }
    }
    sanitizeCrop()
  } catch {
    localStorage.removeItem(STORAGE_KEY)
  }
}

function resetProject() {
  Object.assign(crop, { x: 314, y: 67, width: 198, height: 365 })
  for (const action of actions) {
    if (!action.generatedMirror) {
      action.mirroredFromId = null
      action.mirroredFromName = null
      action.flipX = false
    }
    action.offsetX = 0
    action.offsetY = 0
    Object.assign(action.motion, createDefaultMotion())
    action.fps = DEFAULT_FPS
    action.visible = action.id === 'down'
    action.opacity = action.id === 'down' ? 1 : 0.3
    if (!action.imported) {
      action.projectId = 'builtin-project'
      action.projectName = '默认项目'
      action.characterId = 'builtin-character'
      action.characterName = '主角'
      action.collectionId = 'builtin-character'
      action.collectionName = '默认项目 / 主角'
    }
    action.frames.splice(0, action.frames.length, ...action.sourceFrames)
  }
  activeId.value = 'down'
  frameIndex.value = 0
  motionTime.value = 0
  exportScale.value = 100
  pngCompression.value = 'lossless'
  localStorage.removeItem(STORAGE_KEY)
  statusMessage.value = '已恢复初始状态'
}

function onKeydown(event) {
  const target = event.target
  if (target instanceof HTMLInputElement || target instanceof HTMLSelectElement) return
  if (event.code === 'Space') {
    event.preventDefault()
    togglePlayback()
  } else if (event.key === 'ArrowLeft' && event.shiftKey) {
    event.preventDefault()
    nudgeActive(-1, 0)
  } else if (event.key === 'ArrowRight' && event.shiftKey) {
    event.preventDefault()
    nudgeActive(1, 0)
  } else if (event.key === 'ArrowUp' && event.shiftKey) {
    event.preventDefault()
    nudgeActive(0, -1)
  } else if (event.key === 'ArrowDown' && event.shiftKey) {
    event.preventDefault()
    nudgeActive(0, 1)
  } else if (event.key === 'ArrowLeft') {
    event.preventDefault()
    setFrame(frameIndex.value - 1)
  } else if (event.key === 'ArrowRight') {
    event.preventDefault()
    setFrame(frameIndex.value + 1)
  }
}

watch([isPlaying, activeFps, maxFrames], restartPlayback)
watch([isPlaying, hasVisibleMotion], restartMotionLoop)
watch(
  [
    frameIndex,
    activeId,
    stageBackground,
    showGuides,
    () => actions.map((action) => [
      action.visible,
      action.offsetX,
      action.offsetY,
      action.opacity,
      action.frames.length,
      action.flipX,
      action.mirroredFromId,
      action.motion?.enabled,
      action.motion?.moveX,
      action.motion?.moveY,
      action.motion?.rotation,
      action.motion?.scale,
      action.motion?.duration,
      action.motion?.phase,
    ]),
  ],
  () => {
    nextTick(drawStage)
  },
  { deep: true },
)
watch(
  [loop, stageBackground, showGuides, thinInterval, exportScale, pngCompression],
  () => persistState(),
  { deep: true },
)
watch(
  [
    frameIndex,
    activeId,
    exportScale,
    pngCompression,
    () => [crop.x, crop.y, crop.width, crop.height],
    () => [
      activeFrameOwner.value.offsetX,
      activeFrameOwner.value.offsetY,
      framesForAction(activeAction.value).length,
      activeAction.value.flipX,
      activeAction.value.mirroredFromId,
    ],
  ],
  () => nextTick(scheduleExportPreview),
  { deep: true },
)
watch(showOriginalPreview, paintExportPreview)
watch(
  () =>
    actions
      .filter((action) => !action.imported)
      .map((action) => [
        action.id,
        action.projectName,
        action.characterName,
        action.fps,
        action.visible,
        action.offsetX,
        action.offsetY,
        action.opacity,
        action.flipX,
        action.mirroredFromId,
        action.motion?.enabled,
        action.motion?.moveX,
        action.motion?.moveY,
        action.motion?.rotation,
        action.motion?.scale,
        action.motion?.duration,
        action.motion?.phase,
        action.frames
          .map((frame) => action.sourceFrames.indexOf(frame))
          .join(','),
      ]),
  () => persistState(),
  { deep: true },
)

onMounted(() => {
  restoreState()
  window.addEventListener('keydown', onKeydown)
  drawStage()
  nextTick(scheduleExportPreview)
})

onBeforeUnmount(() => {
  stopTimer()
  stopMotionLoop()
  window.clearTimeout(previewTimer)
  window.removeEventListener('keydown', onKeydown)
  for (const action of actions.filter((item) => item.imported && !item.generatedMirror)) {
    for (const url of action.sourceFrames) URL.revokeObjectURL(url)
  }
})
</script>

<template>
  <div class="app-shell">
    <header class="topbar">
      <div class="brand-block">
        <div class="brand-mark">
          <img src="/branding/octant-icon.png" alt="" />
        </div>
        <div>
          <h1>Octant Sprite Studio</h1>
          <p>项目化序列裁剪与 Unity 图集工作台</p>
        </div>
      </div>

      <div class="topbar-status">
        <span class="status-dot" aria-hidden="true"></span>
        <span>{{ statusMessage }}</span>
        <span class="source-size">{{ SOURCE_WIDTH }} × {{ SOURCE_HEIGHT }}</span>
      </div>

      <div class="topbar-actions">
        <button class="button ghost" type="button" title="恢复初始状态" @click="resetProject">
          <PhArrowCounterClockwise :size="17" />
          重置
        </button>
        <button
          class="button"
          type="button"
          :disabled="isBusy"
          @click="exportSelected"
        >
          <PhDownloadSimple :size="18" weight="bold" />
          {{ exportKind === 'frames' ? `导出中 ${exportProgress}%` : 'PNG 序列' }}
        </button>
        <button
          class="button primary"
          type="button"
          :disabled="isBusy"
          @click="exportUnityAtlas"
        >
          <PhStack :size="18" weight="bold" />
          {{ exportKind === 'atlas' ? `生成中 ${exportProgress}%` : 'Unity 图集' }}
        </button>
      </div>
    </header>

    <main class="workspace">
      <aside class="sidebar action-panel">
        <div class="panel-heading">
          <div>
            <span class="panel-kicker">项目库</span>
            <strong>{{ projectTree.length }} 项目 · {{ actions.length }} 动作</strong>
          </div>
          <button class="panel-text-button" type="button" @click="toggleAllActions">
            {{ visibleActions.length === actions.length ? '仅当前' : '全选' }}
          </button>
        </div>

        <div class="action-list project-tree">
          <section v-for="project in projectTree" :key="project.id" class="project-group">
            <div
              class="tree-node project-node"
              :class="{ current: project.characters.some((item) => item.actions.some((action) => action.id === activeId)) }"
            >
              <button
                class="tree-toggle"
                type="button"
                :title="collapsedProjects.has(project.id) ? '展开项目' : '折叠项目'"
                @click="toggleProject(project.id)"
              >
                <PhCaretRight
                  :size="14"
                  weight="bold"
                  :class="{ expanded: !collapsedProjects.has(project.id) }"
                />
              </button>
              <button class="tree-node-main" type="button" @click="toggleProject(project.id)">
                <span>项目</span>
                <strong>{{ project.name }}</strong>
              </button>
              <span class="tree-count">{{ project.characters.length }} 人物</span>
              <button
                class="tree-edit"
                type="button"
                title="重命名项目"
                @click.stop="renameProject(project)"
              >
                <PhPencilSimple :size="13" />
              </button>
            </div>

            <div v-if="!collapsedProjects.has(project.id)" class="project-children">
              <section
                v-for="character in project.characters"
                :key="character.id"
                class="character-group"
              >
                <div
                  class="tree-node character-node"
                  :class="{ current: character.actions.some((action) => action.id === activeId) }"
                >
                  <button
                    class="tree-toggle"
                    type="button"
                    :title="collapsedCharacters.has(character.id) ? '展开人物' : '折叠人物'"
                    @click="toggleCharacter(character.id)"
                  >
                    <PhCaretRight
                      :size="13"
                      weight="bold"
                      :class="{ expanded: !collapsedCharacters.has(character.id) }"
                    />
                  </button>
                  <button
                    class="tree-node-main"
                    type="button"
                    @click="toggleCharacter(character.id)"
                  >
                    <span>人物</span>
                    <strong>{{ character.name }}</strong>
                  </button>
                  <span class="tree-count">{{ character.actions.length }} 动作</span>
                  <button
                    class="tree-edit"
                    type="button"
                    title="重命名人物"
                    @click.stop="renameCharacter(character)"
                  >
                    <PhPencilSimple :size="13" />
                  </button>
                </div>

                <div
                  v-if="!collapsedCharacters.has(character.id)"
                  class="character-actions"
                >
                  <article
                    v-for="action in character.actions"
                    :key="action.id"
                    class="action-item"
                    :class="{ active: action.id === activeId, visible: action.visible }"
                    @click="activateAction(action)"
                  >
                    <button
                      class="visibility-button"
                      type="button"
                      :title="action.visible ? '隐藏图层' : '叠加显示'"
                      @click.stop="toggleAction(action)"
                    >
                      <PhEye v-if="action.visible" :size="17" weight="fill" />
                      <PhEyeSlash v-else :size="17" />
                    </button>
                    <div class="action-thumb" :class="{ mirrored: action.flipX }">
                      <img :src="framesForAction(action)[normalizeFrame(action)]" alt="" />
                    </div>
                    <div class="action-meta">
                      <div class="action-name-row">
                        <strong>{{ action.name }}</strong>
                        <span v-if="action.mirroredFromId" class="mirror-badge">镜像</span>
                        <span v-if="action.motion?.enabled" class="motion-badge">动态</span>
                      </div>
                      <span>
                        {{ framesForAction(action).length }}
                        <template
                          v-if="framesForAction(action).length !== sourceFramesForAction(action).length"
                        >
                          / {{ sourceFramesForAction(action).length }}
                        </template>
                        帧 · {{ action.fps || DEFAULT_FPS }} FPS ·
                        <template v-if="action.mirroredFromId">
                          来自 {{ action.mirroredFromName }}
                        </template>
                        <template v-else>{{ action.offsetX }}, {{ action.offsetY }}</template>
                      </span>
                    </div>
                    <span class="direction-glyph">{{ action.glyph }}</span>
                  </article>
                </div>
              </section>
            </div>
          </section>
        </div>

        <div class="import-zone">
          <input
            ref="fileInputRef"
            class="visually-hidden"
            type="file"
            accept="image/*"
            webkitdirectory
            multiple
            @change="importFolders"
          />
          <button class="button import-button" type="button" @click="openFolderPicker">
            <PhFolderOpen :size="19" />
            导入项目文件夹
          </button>
          <p>目录结构：项目 / 人物 / 动作 / PNG</p>
          <span v-if="mirroredActions.length" class="mirror-count">
            当前 {{ mirroredActions.length }} 个方向复用帧，Unity 图集会去重
          </span>
        </div>
      </aside>

      <section class="editor">
        <div class="editor-toolbar">
          <div class="toolbar-group">
            <button
              class="icon-button"
              type="button"
              title="上一帧"
              @click="setFrame(frameIndex - 1)"
            >
              <PhCaretLeft :size="19" weight="bold" />
            </button>
            <button class="play-button" type="button" @click="togglePlayback">
              <PhPause v-if="isPlaying" :size="17" weight="fill" />
              <PhPlay v-else :size="17" weight="fill" />
              {{ isPlaying ? '暂停' : '播放' }}
            </button>
            <button
              class="icon-button"
              type="button"
              title="下一帧"
              @click="setFrame(frameIndex + 1)"
            >
              <PhCaretRight :size="19" weight="bold" />
            </button>
            <span class="frame-readout">
              <strong>{{ String(frameNumber).padStart(2, '0') }}</strong>
              <span>/ {{ maxFrames }}</span>
            </span>
          </div>

          <div class="toolbar-group compact-controls">
            <label>
              <span>动作 FPS</span>
              <input v-model.number="activeFps" type="number" min="1" max="30" />
            </label>
            <label class="checkbox-label">
              <input v-model="loop" type="checkbox" />
              循环
            </label>
          </div>

          <div class="toolbar-group stage-controls">
            <button
              class="icon-button"
              type="button"
              :class="{ selected: stageBackground === 'checker' }"
              title="切换预览背景"
              @click="
                stageBackground =
                  stageBackground === 'checker'
                    ? 'dark'
                    : stageBackground === 'dark'
                      ? 'light'
                      : 'checker'
              "
            >
              <PhCheckerboard :size="19" />
            </button>
            <label class="zoom-control">
              <span>{{ Math.round(zoom * 100) }}%</span>
              <input v-model.number="zoom" type="range" min="0.65" max="1.45" step="0.05" />
            </label>
          </div>
        </div>

        <div ref="stageRef" class="stage-viewport">
          <div
            class="canvas-wrap"
            :class="`background-${stageBackground}`"
            :style="{ width: `${SOURCE_WIDTH * zoom}px`, height: `${SOURCE_HEIGHT * zoom}px` }"
          >
            <canvas
              ref="canvasRef"
              :style="{ width: `${SOURCE_WIDTH * zoom}px`, height: `${SOURCE_HEIGHT * zoom}px` }"
              aria-label="序列帧裁剪预览"
              @pointerdown="onPointerDown"
              @pointermove="onPointerMove"
              @pointerup="onPointerUp"
              @pointercancel="onPointerUp"
            ></canvas>
            <div class="canvas-label source-label">原图 {{ SOURCE_WIDTH }} × {{ SOURCE_HEIGHT }}</div>
            <div class="canvas-label crop-label">{{ cropRatio }}</div>
          </div>
        </div>

        <div class="timeline">
          <div class="timeline-track">
            <button
              v-for="(frame, index) in framesForAction(activeAction)"
              :key="`${activeAction.id}-${index}`"
              class="frame-cell"
              :class="{ active: index === normalizeFrame(activeAction) }"
              type="button"
              @click="setFrame(index)"
            >
              <img :src="frame" alt="" loading="lazy" />
              <span>{{ String(index + 1).padStart(2, '0') }}</span>
            </button>
          </div>
        </div>
      </section>

      <aside class="sidebar inspector">
        <section class="inspector-section">
          <div class="section-title">
            <div>
              <span class="panel-kicker">输出区域</span>
              <strong>裁剪框</strong>
            </div>
            <PhFrameCorners :size="21" />
          </div>

          <div class="field-grid">
            <label>
              <span>X</span>
              <input v-model.number="crop.x" type="number" @change="sanitizeCrop" />
            </label>
            <label>
              <span>Y</span>
              <input v-model.number="crop.y" type="number" @change="sanitizeCrop" />
            </label>
            <label>
              <span>宽</span>
              <input v-model.number="crop.width" type="number" min="24" @change="sanitizeCrop" />
            </label>
            <label>
              <span>高</span>
              <input v-model.number="crop.height" type="number" min="24" @change="sanitizeCrop" />
            </label>
          </div>

          <button
            class="button full-width accent-outline"
            type="button"
            :disabled="isBusy"
            @click="autoCrop('visible')"
          >
            <PhMagicWand :size="18" />
            {{ analysisProgress ? `分析中 ${analysisProgress}%` : '按所选动作紧裁' }}
          </button>
          <button
            class="text-button"
            type="button"
            :disabled="isBusy"
            @click="autoCrop('active')"
          >
            只分析当前动作
          </button>

          <label class="toggle-row">
            <span>
              <strong>中心参考线</strong>
              <small>裁剪中心与人物中心校对</small>
            </span>
            <input v-model="showGuides" class="switch" type="checkbox" />
          </label>
        </section>

        <section class="inspector-section mirror-policy-section">
          <div class="section-title">
            <div>
              <span class="panel-kicker">资源复用</span>
              <strong>方向镜像</strong>
            </div>
            <PhArrowsOutCardinal :size="21" />
          </div>

          <div class="mirror-collection-row">
            <span>当前素材组</span>
            <strong>{{ activeCollectionName }}</strong>
          </div>

          <label v-for="pair in mirrorPolicyRows" :key="pair.id" class="mirror-policy-row">
            <span>{{ pair.label }}</span>
            <select
              :value="pair.value"
              @change="setMirrorPolicy(pair, $event.target.value)"
            >
              <option value="real" :disabled="!pair.canUseReal">使用真实双方</option>
              <option value="left-to-right" :disabled="!pair.canMirrorLeft">
                {{ pair.leftLabel }} → {{ pair.rightLabel }}镜像
              </option>
              <option value="right-to-left" :disabled="!pair.canMirrorRight">
                {{ pair.rightLabel }} → {{ pair.leftLabel }}镜像
              </option>
            </select>
          </label>

          <div class="mirror-savings-readout">
            <span>当前所选动作</span>
            <strong>
              {{ atlasFrameStats.physical }} 实体 / {{ atlasFrameStats.logical }} 逻辑帧
            </strong>
          </div>
          <p class="safe-operation-note mirror-policy-note">
            已节省 {{ atlasFrameStats.saved }} 个图集帧。上、下始终使用真实素材；Unity
            播放镜像方向时需要设置 SpriteRenderer.flipX。
          </p>
        </section>

        <section class="inspector-section export-settings-section">
          <div class="section-title">
            <div>
              <span class="panel-kicker">导出处理</span>
              <strong>尺寸与压缩</strong>
            </div>
            <PhSlidersHorizontal :size="21" />
          </div>

          <label class="export-select-field">
            <span>PNG 压缩</span>
            <select v-model="pngCompression">
              <option value="lossless">无损 · 原色</option>
              <option value="light">轻度 · 4096 色</option>
              <option value="balanced">中度 · 1024 色</option>
              <option value="strong">高度 · 256 色</option>
            </select>
          </label>

          <label class="export-scale-field">
            <span>
              <span>等比例缩放</span>
              <strong>{{ exportScale }}%</strong>
            </span>
            <input
              v-model.number="exportScale"
              type="range"
              min="25"
              max="100"
              step="5"
              @change="sanitizeExportScale"
            />
          </label>

          <div class="scale-preset-row" aria-label="导出缩放预设">
            <button
              v-for="preset in [25, 50, 75, 100]"
              :key="preset"
              type="button"
              :class="{ active: exportScale === preset }"
              @click="exportScale = preset"
            >
              {{ preset }}%
            </button>
          </div>

          <div class="export-size-readout">
            <span>单帧输出</span>
            <strong>{{ scaledCrop.width }} × {{ scaledCrop.height }}</strong>
          </div>

          <div class="export-live-preview">
            <div class="export-preview-heading">
              <span>当前帧导出预览</span>
              <strong :class="{ pending: previewStatus !== '实时' }">{{ previewStatus }}</strong>
            </div>
            <div class="export-preview-stage">
              <canvas
                ref="exportPreviewCanvasRef"
                class="export-preview-canvas"
                :style="{
                  width: `${previewDisplaySize.width}px`,
                  height: `${previewDisplaySize.height}px`,
                }"
                aria-label="当前帧导出效果预览"
              ></canvas>
              <span class="export-preview-mode">
                {{ showOriginalPreview ? '无压缩原图' : '导出效果' }}
              </span>
            </div>
            <div class="export-preview-stats">
              <span>
                {{ previewBytes ? `${formatFileSize(previewBytes)} / 帧` : '计算中' }}
              </span>
              <strong v-if="previewSavings">预计减少 {{ previewSavings }}%</strong>
              <strong v-else>{{ previewDisplayScale === 1 ? '1:1 显示' : '适配显示' }}</strong>
            </div>
            <button
              class="compare-preview-button"
              type="button"
              :disabled="pngCompression === 'lossless'"
              :aria-pressed="showOriginalPreview"
              @pointerdown.prevent="showOriginalPreview = true"
              @pointerup="showOriginalPreview = false"
              @pointerleave="showOriginalPreview = false"
              @pointercancel="showOriginalPreview = false"
              @keydown.space.prevent="showOriginalPreview = true"
              @keyup.space.prevent="showOriginalPreview = false"
              @keydown.enter.prevent="showOriginalPreview = true"
              @keyup.enter.prevent="showOriginalPreview = false"
              @blur="showOriginalPreview = false"
            >
              {{ pngCompression === 'lossless' ? '当前已是无损效果' : '按住查看无压缩原图' }}
            </button>
          </div>

          <p class="safe-operation-note export-operation-note">
            {{ compressionDescription }} 只影响导出的 PNG 与图集，不会修改本地素材。
          </p>
        </section>

        <section class="inspector-section alignment-section">
          <div class="section-title">
            <div>
              <span class="panel-kicker">当前动作</span>
              <strong>{{ activeAction.name }}</strong>
            </div>
            <PhCrosshairSimple :size="21" />
          </div>

          <div class="field-grid offset-grid">
            <label>
              <span>偏移 X</span>
              <input v-model.number="activeFrameOwner.offsetX" type="number" />
            </label>
            <label>
              <span>偏移 Y</span>
              <input v-model.number="activeFrameOwner.offsetY" type="number" />
            </label>
          </div>

          <p v-if="activeAction.mirroredFromId" class="mirror-owner-note">
            当前方向复用 {{ activeFrameOwner.name }} 的帧与中心偏移。
          </p>

          <div class="nudge-pad" aria-label="微调当前动作位置">
            <button type="button" title="向上" @click="nudgeActive(0, -1)">↑</button>
            <button type="button" title="向左" @click="nudgeActive(-1, 0)">←</button>
            <div class="nudge-center"><PhArrowsOutCardinal :size="18" /></div>
            <button type="button" title="向右" @click="nudgeActive(1, 0)">→</button>
            <button type="button" title="向下" @click="nudgeActive(0, 1)">↓</button>
          </div>

          <label class="opacity-field">
            <span>
              <span>叠加透明度</span>
              <strong>{{ Math.round(activeAction.opacity * 100) }}%</strong>
            </span>
            <input v-model.number="activeAction.opacity" type="range" min="0.05" max="1" step="0.05" />
          </label>

          <button
            class="button full-width"
            type="button"
            :disabled="visibleActions.length < 2 || isBusy"
            @click="alignVisibleToActive"
          >
            <PhCrosshairSimple :size="18" />
            按当前帧自动对齐
          </button>
          <button class="text-button" type="button" @click="resetOffsets">重置全部偏移</button>
        </section>

        <section class="inspector-section motion-section">
          <div class="section-title">
            <div>
              <span class="panel-kicker">运行时效果</span>
              <strong>轻量动态修饰</strong>
            </div>
            <PhWaveSine :size="21" />
          </div>

          <label class="toggle-row motion-toggle">
            <span>
              <strong>启用当前动作</strong>
              <small>{{ motionSummary }}</small>
            </span>
            <input v-model="activeMotion.enabled" class="switch" type="checkbox" />
          </label>

          <div class="motion-presets" aria-label="动态预设">
            <button type="button" @click="applyMotionPreset('drift')">左右漂移</button>
            <button type="button" @click="applyMotionPreset('float')">上下浮动</button>
            <button type="button" @click="applyMotionPreset('sway')">轻微摇摆</button>
            <button type="button" @click="applyMotionPreset('breathe')">呼吸缩放</button>
          </div>

          <div class="field-grid motion-field-grid">
            <label>
              <span>X 摆动 px</span>
              <input v-model.number="activeMotion.moveX" type="number" min="0" max="240" step="0.5" @change="sanitizeMotion" />
            </label>
            <label>
              <span>Y 摆动 px</span>
              <input v-model.number="activeMotion.moveY" type="number" min="0" max="240" step="0.5" @change="sanitizeMotion" />
            </label>
            <label>
              <span>旋转 °</span>
              <input v-model.number="activeMotion.rotation" type="number" min="0" max="45" step="0.5" @change="sanitizeMotion" />
            </label>
            <label>
              <span>缩放 %</span>
              <input v-model.number="activeMotion.scale" type="number" min="0" max="50" step="0.5" @change="sanitizeMotion" />
            </label>
          </div>

          <label class="motion-duration-field">
            <span>
              <span>单次周期</span>
              <strong>{{ activeMotion.duration }} 秒</strong>
            </span>
            <input v-model.number="activeMotion.duration" type="range" min="0.2" max="8" step="0.1" @change="sanitizeMotion" />
          </label>

          <label class="motion-phase-field">
            <span>起始相位</span>
            <input v-model.number="activeMotion.phase" type="number" min="0" max="360" step="15" @change="sanitizeMotion" />
            <span>°</span>
          </label>

          <p class="safe-operation-note motion-note">
            与上方播放键同步预览，围绕底部中心点运动。动态写入 motion.json 与
            .tpsheet，不会烘焙或增加 PNG 帧。
          </p>
          <button class="text-button motion-reset-button" type="button" @click="applyMotionPreset('reset')">
            清除当前动作动态
          </button>
        </section>

        <section class="inspector-section thinning-section">
          <div class="section-title">
            <div>
              <span class="panel-kicker">非破坏性处理</span>
              <strong>序列减帧</strong>
            </div>
            <PhScissors :size="21" />
          </div>

          <label class="interval-field">
            <span>每</span>
            <input
              v-model.number="thinInterval"
              type="number"
              min="2"
              max="60"
              @change="sanitizeThinInterval"
            />
            <span>帧移除 1 帧</span>
          </label>

          <p class="safe-operation-note">
            每次基于当前剩余帧继续处理，本地 PNG 文件不会删除。
          </p>

          <button
            class="button full-width accent-outline"
            type="button"
            :disabled="isBusy"
            @click="thinFrames('visible')"
          >
            <PhScissors :size="17" />
            对所选动作减帧
          </button>
          <button
            class="text-button"
            type="button"
            :disabled="isBusy"
            @click="thinFrames('active')"
          >
            只处理当前动作
          </button>
          <button
            class="text-button restore-frame-button"
            type="button"
            :disabled="isBusy"
            @click="restoreFrames('visible')"
          >
            恢复所选动作原始帧
          </button>
        </section>

        <section class="shortcut-note">
          <strong>键盘操作</strong>
          <span><kbd>Space</kbd> 播放 / 暂停</span>
          <span><kbd>←</kbd><kbd>→</kbd> 切帧</span>
          <span><kbd>Shift</kbd> + 方向键 微调图层</span>
        </section>
      </aside>
    </main>
  </div>
</template>
