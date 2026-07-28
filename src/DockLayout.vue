<script setup>
import { onBeforeUnmount, ref } from 'vue'
import { PhDotsSixVertical } from '@phosphor-icons/vue'

const props = defineProps({
  node: {
    type: Object,
    required: true,
  },
  panelMeta: {
    type: Object,
    required: true,
  },
  dragState: {
    type: Object,
    default: null,
  },
})

const emit = defineEmits([
  'activate',
  'drag-start',
  'drag-end',
  'drag-over',
  'drop-panel',
  'resize-split',
])

const splitRef = ref(null)
let stopResize = null

function startPanelDrag(event, panelId) {
  event.dataTransfer.effectAllowed = 'move'
  event.dataTransfer.setData('text/plain', panelId)
  emit('drag-start', { panelId, sourceGroupId: props.node.id })
}

function dropPanel(event, position) {
  event.preventDefault()
  event.stopPropagation()
  if (!props.dragState?.panelId) return
  emit('drop-panel', {
    panelId: props.dragState.panelId,
    sourceGroupId: props.dragState.sourceGroupId,
    targetGroupId: props.node.id,
    position,
  })
}

function beginSplitResize(event) {
  event.preventDefault()
  const element = splitRef.value
  if (!element) return
  const bounds = element.getBoundingClientRect()
  const resizeClass =
    props.node.direction === 'horizontal'
      ? 'is-resizing-dock-horizontal'
      : 'is-resizing-dock-vertical'
  document.body.classList.add('is-resizing-dock', resizeClass)

  const onMove = (moveEvent) => {
    const rawRatio =
      props.node.direction === 'horizontal'
        ? (moveEvent.clientX - bounds.left) / bounds.width
        : (moveEvent.clientY - bounds.top) / bounds.height
    emit('resize-split', {
      nodeId: props.node.id,
      ratio: Math.max(0.15, Math.min(0.85, rawRatio)),
    })
  }

  const onEnd = () => {
    window.removeEventListener('pointermove', onMove)
    window.removeEventListener('pointerup', onEnd)
    document.body.classList.remove('is-resizing-dock', resizeClass)
    stopResize = null
  }

  window.addEventListener('pointermove', onMove)
  window.addEventListener('pointerup', onEnd, { once: true })
  stopResize = onEnd
}

onBeforeUnmount(() => stopResize?.())
</script>

<template>
  <div
    v-if="node.type === 'split'"
    ref="splitRef"
    class="dock-split"
    :class="`dock-split-${node.direction}`"
  >
    <div
      class="dock-split-pane dock-split-first"
      :style="{ flexBasis: `${node.ratio * 100}%` }"
    >
      <DockLayout
        :node="node.first"
        :panel-meta="panelMeta"
        :drag-state="dragState"
        @activate="$emit('activate', $event)"
        @drag-start="$emit('drag-start', $event)"
        @drag-end="$emit('drag-end')"
        @drag-over="$emit('drag-over', $event)"
        @drop-panel="$emit('drop-panel', $event)"
        @resize-split="$emit('resize-split', $event)"
      />
    </div>
    <div
      class="dock-splitter"
      :aria-orientation="node.direction === 'horizontal' ? 'vertical' : 'horizontal'"
      role="separator"
      @pointerdown="beginSplitResize"
    ></div>
    <div class="dock-split-pane dock-split-second">
      <DockLayout
        :node="node.second"
        :panel-meta="panelMeta"
        :drag-state="dragState"
        @activate="$emit('activate', $event)"
        @drag-start="$emit('drag-start', $event)"
        @drag-end="$emit('drag-end')"
        @drag-over="$emit('drag-over', $event)"
        @drop-panel="$emit('drop-panel', $event)"
        @resize-split="$emit('resize-split', $event)"
      />
    </div>
  </div>

  <section
    v-else
    class="dock-group"
    :class="{ 'is-drop-target': dragState?.overGroupId === node.id }"
    @dragenter.prevent.stop="$emit('drag-over', node.id)"
    @dragover.prevent.stop
  >
    <div class="dock-group-tabs" role="tablist" aria-label="停靠面板标签">
      <button
        v-for="panelId in node.tabs"
        :key="panelId"
        class="dock-tab"
        :class="{ active: node.active === panelId }"
        type="button"
        role="tab"
        draggable="true"
        :aria-selected="node.active === panelId"
        :title="`拖拽 ${panelMeta[panelId]?.label || panelId} 以重新停靠`"
        @click="$emit('activate', { groupId: node.id, panelId })"
        @dragstart="startPanelDrag($event, panelId)"
        @dragend="$emit('drag-end')"
      >
        <PhDotsSixVertical :size="13" class="dock-tab-grip" />
        <span>{{ panelMeta[panelId]?.label || panelId }}</span>
      </button>
    </div>

    <div class="dock-group-body">
      <div
        v-for="panelId in node.tabs"
        :id="`dock-panel-host-${panelId}`"
        :key="panelId"
        v-show="node.active === panelId"
        class="dock-panel-host"
        role="tabpanel"
      ></div>

      <div
        v-if="dragState?.panelId && dragState.overGroupId === node.id"
        class="dock-drop-overlay"
      >
        <div class="dock-drop-zone dock-drop-top" @dragover.prevent @drop="dropPanel($event, 'top')">
          上方
        </div>
        <div class="dock-drop-zone dock-drop-left" @dragover.prevent @drop="dropPanel($event, 'left')">
          左侧
        </div>
        <div class="dock-drop-zone dock-drop-center" @dragover.prevent @drop="dropPanel($event, 'center')">
          合并标签
        </div>
        <div class="dock-drop-zone dock-drop-right" @dragover.prevent @drop="dropPanel($event, 'right')">
          右侧
        </div>
        <div class="dock-drop-zone dock-drop-bottom" @dragover.prevent @drop="dropPanel($event, 'bottom')">
          下方
        </div>
      </div>
    </div>
  </section>
</template>
