const directions = [
  ['up', '上行走', '↑', '行走'],
  ['up-right', '右上行走', '↗', '右上行走'],
  ['right', '右行走', '→', '左行走'],
  ['down-right', '右下行走', '↘', '行走'],
  ['down', '下行走', '↓', '行走'],
  ['down-left', '左下行走', '↙', '行走'],
  ['left', '左行走', '←', '左行走'],
  ['up-left', '左上行走', '↖', '左上行走'],
]

const directionMeta = Object.fromEntries(
  directions.map(([id, name, glyph]) => [id, { id, name, glyph }]),
)

const mirrorPairs = [
  ['left', 'right'],
  ['up-left', 'up-right'],
  ['down-left', 'down-right'],
]

export function createDefaultMotion() {
  return {
    enabled: false,
    moveX: 0,
    moveY: 0,
    rotation: 0,
    scale: 0,
    duration: 2.4,
    phase: 0,
  }
}

export function detectDirectionId(name) {
  const value = String(name || '').toLowerCase()
  const compact = value.replace(/[\s_.-]+/g, '')
  const patterns = [
    ['up-right', /右上|上右|东北|upright|rightup|northeast|ne$/],
    ['up-left', /左上|上左|西北|upleft|leftup|northwest|nw$/],
    ['down-right', /右下|下右|东南|downright|rightdown|southeast|se$/],
    ['down-left', /左下|下左|西南|downleft|leftdown|southwest|sw$/],
    ['right', /右|向东|right|east$/],
    ['left', /左|向西|left|west$/],
    ['up', /上|向北|up|north$/],
    ['down', /下|向南|down|south$/],
  ]
  return patterns.find(([, pattern]) => pattern.test(compact))?.[0] || null
}

function mirroredActionName(name, sourceDirection, targetDirection) {
  const replacements = {
    'left:right': [['左', '右'], ['Left', 'Right'], ['left', 'right']],
    'right:left': [['右', '左'], ['Right', 'Left'], ['right', 'left']],
    'up-left:up-right': [['左上', '右上'], ['上左', '上右'], ['Left', 'Right'], ['left', 'right']],
    'up-right:up-left': [['右上', '左上'], ['上右', '上左'], ['Right', 'Left'], ['right', 'left']],
    'down-left:down-right': [['左下', '右下'], ['下左', '下右'], ['Left', 'Right'], ['left', 'right']],
    'down-right:down-left': [['右下', '左下'], ['下右', '下左'], ['Right', 'Left'], ['right', 'left']],
  }
  let mirrored = name
  for (const [from, to] of replacements[`${sourceDirection}:${targetDirection}`] || []) {
    if (mirrored.includes(from)) {
      mirrored = mirrored.replace(from, to)
      break
    }
  }
  return mirrored === name ? directionMeta[targetDirection].name : mirrored
}

export function createMissingMirroredActions(sourceActions, batchId = `mirror-${Date.now()}`) {
  const actualByDirection = new Map(
    sourceActions
      .filter((action) => action.directionId && !action.mirroredFromId)
      .map((action) => [action.directionId, action]),
  )
  const mirrored = []

  for (const [leftDirection, rightDirection] of mirrorPairs) {
    const left = actualByDirection.get(leftDirection)
    const right = actualByDirection.get(rightDirection)
    if (Boolean(left) === Boolean(right)) continue
    const source = left || right
    const targetDirection = left ? rightDirection : leftDirection
    const meta = directionMeta[targetDirection]
    mirrored.push({
      id: `${batchId}-mirror-${targetDirection}`,
      name: mirroredActionName(source.name, source.directionId, targetDirection),
      glyph: meta.glyph,
      directionId: targetDirection,
      sourceFrames: source.sourceFrames,
      frames: [...source.frames],
      offsetX: source.offsetX,
      offsetY: source.offsetY,
      footAnchor: source.footAnchor ? { ...source.footAnchor } : null,
      motion: createDefaultMotion(),
      fps: source.fps,
      opacity: 0.3,
      visible: false,
      imported: true,
      flipX: true,
      generatedMirror: true,
      projectId: source.projectId,
      projectName: source.projectName,
      characterId: source.characterId,
      characterName: source.characterName,
      collectionId: source.collectionId,
      collectionName: source.collectionName,
      mirroredFromId: source.id,
      mirroredFromName: source.name,
    })
  }

  return mirrored
}

export function createBuiltInActions() {
  return directions.map(([id, name, glyph, prefix]) => {
    const sourceFrames = Array.from(
      { length: 61 },
      (_, index) =>
        `/sprites/${encodeURIComponent(name)}/${encodeURIComponent(prefix)}_${index + 1}.png`,
    )
    return {
      id,
      name,
      glyph,
      directionId: id,
      sourceFrames,
      frames: [...sourceFrames],
      offsetX: 0,
      offsetY: 0,
      footAnchor: { x: 413, y: 411 },
      motion: createDefaultMotion(),
      fps: 12,
      opacity: id === 'down' ? 1 : 0.3,
      visible: id === 'down',
      imported: false,
      flipX: false,
      generatedMirror: false,
      projectId: 'builtin-project',
      projectName: '默认项目',
      characterId: 'builtin-character',
      characterName: '主角',
      collectionId: 'builtin-character',
      collectionName: '默认项目 / 主角',
    }
  })
}

export function createImportedActions(fileList) {
  const groups = new Map()
  const batchId = `imported-${Date.now()}`

  for (const file of fileList) {
    if (!file.type.startsWith('image/')) continue
    const path = file.webkitRelativePath || file.name
    const parts = path.split('/')
    const parentParts = parts.slice(0, -1)
    const actionName = parentParts.at(-1) || '导入动作'
    let projectName = '导入项目'
    let characterName = '未命名人物'

    if (parentParts.length >= 3) {
      projectName = parentParts.at(-3)
      characterName = parentParts.at(-2)
    } else if (parentParts.length === 2) {
      characterName = parentParts.at(-2)
    }

    const groupKey = JSON.stringify([projectName, characterName, actionName])
    if (!groups.has(groupKey)) {
      groups.set(groupKey, { projectName, characterName, actionName, files: [] })
    }
    groups.get(groupKey).files.push(file)
  }

  const numberOf = (name) => Number(name.match(/(\d+)(?=\.[^.]+$)/)?.[1] || 0)
  const projectIds = new Map()
  const characterIds = new Map()
  const imported = [...groups.values()].map((group, index) => {
    const { projectName, characterName, actionName, files } = group
    if (!projectIds.has(projectName)) {
      projectIds.set(projectName, `${batchId}-project-${projectIds.size}`)
    }
    const projectId = projectIds.get(projectName)
    const characterKey = JSON.stringify([projectName, characterName])
    if (!characterIds.has(characterKey)) {
      characterIds.set(characterKey, `${projectId}-character-${characterIds.size}`)
    }
    const characterId = characterIds.get(characterKey)
    const sourceFrames = files
      .sort((a, b) => numberOf(a.name) - numberOf(b.name))
      .map((file) => URL.createObjectURL(file))
    return {
      id: `${batchId}-${index}`,
      name: actionName,
      glyph: '＋',
      directionId: detectDirectionId(actionName),
      sourceFrames,
      frames: [...sourceFrames],
      offsetX: 0,
      offsetY: 0,
      footAnchor: null,
      motion: createDefaultMotion(),
      fps: 12,
      opacity: 0.3,
      visible: false,
      imported: true,
      flipX: false,
      generatedMirror: false,
      projectId,
      projectName,
      characterId,
      characterName,
      collectionId: characterId,
      collectionName: `${projectName} / ${characterName}`,
    }
  })
  for (const action of imported) {
    if (action.directionId) action.glyph = directionMeta[action.directionId].glyph
  }

  const mirrored = []
  for (const characterId of new Set(imported.map((action) => action.characterId))) {
    const characterActions = imported.filter((action) => action.characterId === characterId)
    mirrored.push(...createMissingMirroredActions(characterActions, characterId))
  }
  return [...imported, ...mirrored]
}
