import React, { useState, useEffect, useCallback } from 'react'
import { opportunityApi } from '../api/opportunityApi'
import { PipelineColumn } from './PipelineColumn'
import { LostReasonDialog } from './LostReasonDialog'
import Icon from '../../../shared/components/Icon'

export const OpportunityPipeline = ({
  filters,
  stages = [],
  isReadOnly = false,
  onOpenCreate,
  onCardClick,
  onEdit,
  onDelete,
  onRefresh,
  showFilters = false,
  onToggleFilters,
  activeFilterCount = 0,
  childrenFilters,
}) => {
  const [columns, setColumns] = useState([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const [toast, setToast] = useState(null)

  // Per-stage paging and loading state
  const [pageIndices, setPageIndices] = useState({})
  const [loadingMoreStages, setLoadingMoreStages] = useState({})

  // HTML5 Drag & Drop state
  const [draggedItem, setDraggedItem] = useState(null)

  // Lost reason dialog state
  const [lostDialog, setLostDialog] = useState(null)

  // Load pipeline board data
  const fetchPipeline = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const res = await opportunityApi.pipeline({
        filters: {
          assigneeFilter: filters.assignee || 'mine',
          stageId: filters.stage || null,
          keyword: filters.keyword || null,
          source: filters.source || null,
        },
        perStage: 20,
      })
      const cols = res?.columns || []
      setColumns(cols)
      // Reset page indices per stage
      const indices = {}
      cols.forEach((c) => {
        indices[c.stageId] = 1
      })
      setPageIndices(indices)
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Không thể tải pipeline cơ hội')
    } finally {
      setLoading(false)
    }
  }, [filters.assignee, filters.stage, filters.keyword, filters.source])

  useEffect(() => {
    fetchPipeline()
  }, [fetchPipeline])

  // Clear toast after 4s
  useEffect(() => {
    if (toast) {
      const timer = setTimeout(() => setToast(null), 4000)
      return () => clearTimeout(timer)
    }
  }, [toast])

  // Handle Load More per column
  const handleLoadMore = async (stageId) => {
    const nextIndex = (pageIndices[stageId] || 1) + 1
    setLoadingMoreStages((prev) => ({ ...prev, [stageId]: true }))
    try {
      const res = await opportunityApi.pipelineStage(stageId, {
        index: nextIndex,
        size: 20,
        filters: {
          assigneeFilter: filters.assignee || 'mine',
          keyword: filters.keyword || null,
          source: filters.source || null,
        },
      })
      const newItems = res?.items || []
      const newTotal = res?.total

      setColumns((prev) =>
        prev.map((c) => {
          if (c.stageId !== stageId) return c
          // Avoid duplicate items if any
          const existingIds = new Set(c.items.map((i) => i.id))
          const filteredNew = newItems.filter((i) => !existingIds.has(i.id))
          return {
            ...c,
            total: newTotal !== undefined ? newTotal : c.total,
            items: [...c.items, ...filteredNew],
          }
        })
      )
      setPageIndices((prev) => ({ ...prev, [stageId]: nextIndex }))
    } catch (err) {
      setToast('Không thể tải thêm cơ hội: ' + (err?.response?.data?.message || err?.message))
    } finally {
      setLoadingMoreStages((prev) => ({ ...prev, [stageId]: false }))
    }
  };

  // Drag Start on card
  const handleDragStart = (e, opp) => {
    setDraggedItem(opp)
    e.dataTransfer.setData('text/plain', opp.id)
    e.dataTransfer.effectAllowed = 'move'
  }

  // Drop on column
  const handleDrop = (e, targetStageId) => {
    const oppId = e.dataTransfer?.getData('text/plain') || draggedItem?.id
    if (!oppId) return

    // Find opportunity in current columns
    let oppToMove = draggedItem
    if (!oppToMove || oppToMove.id !== oppId) {
      for (const col of columns) {
        const found = col.items.find((i) => i.id === oppId)
        if (found) {
          oppToMove = found
          break
        }
      }
    }

    if (!oppToMove) return
    if (oppToMove.stageId === targetStageId) return

    const targetStage =
      stages.find((s) => s.id === targetStageId) ||
      columns.find((c) => c.stageId === targetStageId)

    // Check if target stage is Lost (Kind === 3)
    const isLost = targetStage?.kind === 3 || targetStage?.kind === 'Lost'
    if (isLost) {
      setLostDialog({
        isOpen: true,
        opp: oppToMove,
        targetStage,
      })
      return
    }

    // Move to non-lost stage
    executeMoveStage(oppToMove, targetStageId, null)
  }

  // Mobile / Keyboard quick move stage from card menu
  const handleCardQuickMove = (opp, targetStageId) => {
    const targetStage =
      stages.find((s) => s.id === targetStageId) ||
      columns.find((c) => c.stageId === targetStageId)

    const isLost = targetStage?.kind === 3 || targetStage?.kind === 'Lost'
    if (isLost) {
      setLostDialog({
        isOpen: true,
        opp,
        targetStage,
      })
      return
    }
    executeMoveStage(opp, targetStageId, null)
  }

  // Execute stage move with optimistic update & rollback
  const executeMoveStage = async (opp, targetStageId, lostReason) => {
    const previousColumns = columns
    const targetStage =
      stages.find((s) => s.id === targetStageId) ||
      columns.find((c) => c.stageId === targetStageId)

    // Optimistically update columns
    setColumns((prev) =>
      prev.map((col) => {
        if (col.stageId === opp.stageId) {
          return {
            ...col,
            total: Math.max(0, (col.total ?? col.items.length) - 1),
            items: col.items.filter((i) => i.id !== opp.id),
          }
        }
        if (col.stageId === targetStageId) {
          const updatedOpp = {
            ...opp,
            stageId: targetStageId,
            stageName: targetStage?.name || col.stageName,
            stageColor: targetStage?.color || col.stageColor,
            stageKind: targetStage?.kind || col.kind,
          }
          return {
            ...col,
            total: (col.total ?? col.items.length) + 1,
            items: [updatedOpp, ...col.items.filter((i) => i.id !== opp.id)],
          }
        }
        return col
      })
    )

    try {
      await opportunityApi.moveStage(opp.id, targetStageId, lostReason)
      onRefresh?.()
    } catch (err) {
      // Rollback on error and show toast
      setColumns(previousColumns)
      const msg = err?.response?.data?.message || err?.message || 'Lỗi chuyển giai đoạn'
      setToast(`Lỗi chuyển giai đoạn cơ hội "${opp.title}": ${msg}`)
    } finally {
      setDraggedItem(null)
    }
  }

  // Handle "+" button on a specific column
  const handleAddOpportunityToStage = (stageId) => {
    onOpenCreate?.(stageId)
  }

  return (
    <div className="crm-opp-pipeline-container" data-testid="opportunity-pipeline">
      {/* Toast Alert Banner */}
      {toast && (
        <div className="crm-opp-toast crm-opp-toast--error" data-testid="pipeline-toast">
          <span><Icon name="alert" size={14} /> {toast}</span>
          <button
            type="button"
            className="crm-opp-toast-close"
            onClick={() => setToast(null)}
            aria-label="Đóng thông báo"
            data-testid="btn-close-toast"
          >
            <Icon name="close" size={14} />
          </button>
        </div>
      )}

      {/* Toolbar */}
      <div className="crm-opp-pipeline-toolbar">
        <div className="crm-opp-pipeline-toolbar-left">
          <span className="crm-opp-pipeline-info-badge">
            <Icon name="pin" size={14} /> {columns.length} giai đoạn
          </span>
        </div>

        <div className="crm-opp-toolbar-actions">
          {!isReadOnly && (
            <button
              type="button"
              className="crm-opp-btn crm-opp-btn--primary"
              onClick={() => onOpenCreate?.()}
              data-testid="btn-create-opportunity"
            >
              + Cơ hội
            </button>
          )}

          <button
            type="button"
            className="crm-opp-btn crm-opp-btn--secondary"
            onClick={() => {
              fetchPipeline()
              onRefresh?.()
            }}
            data-testid="btn-refresh-pipeline"
            title="Làm mới Pipeline"
          >
            <Icon name="refresh" size={16} /> Làm mới
          </button>

          <button
            type="button"
            className={`crm-opp-btn crm-opp-btn--secondary ${showFilters ? 'crm-opp-btn--active' : ''}`}
            onClick={onToggleFilters}
            data-testid="btn-toggle-filters"
          >
            <Icon name="search" size={16} /> Lọc
            {activeFilterCount > 0 && (
              <span className="crm-opp-filter-badge" data-testid="active-filter-badge">
                {activeFilterCount}
              </span>
            )}
          </button>
        </div>
      </div>

      {/* Filters bar */}
      {childrenFilters}

      {/* Loading state */}
      {loading && (
        <div className="crm-opp-pipeline-loading" data-testid="pipeline-loading">
          Đang tải dữ liệu Pipeline Kanban...
        </div>
      )}

      {/* Error state */}
      {error && !loading && (
        <div className="crm-opp-pipeline-error" data-testid="pipeline-error">
          <span><Icon name="alert" size={14} /> {error}</span>
          <button
            type="button"
            className="crm-opp-btn crm-opp-btn--sm crm-opp-btn--danger"
            onClick={fetchPipeline}
            data-testid="btn-retry-pipeline"
          >
            Thử lại
          </button>
        </div>
      )}

      {/* Kanban Board Columns Container */}
      {!loading && !error && (
        <div className="crm-opp-pipeline-board" data-testid="pipeline-board">
          {columns.map((col) => (
            <PipelineColumn
              key={col.stageId}
              column={col}
              stages={stages}
              isReadOnly={isReadOnly}
              onCardClick={onCardClick}
              onMoveStage={handleCardQuickMove}
              onEdit={onEdit}
              onDelete={onDelete}
              onAddOpportunity={handleAddOpportunityToStage}
              onLoadMore={handleLoadMore}
              onDragStart={handleDragStart}
              onDrop={handleDrop}
              loadingMore={Boolean(loadingMoreStages[col.stageId])}
            />
          ))}
        </div>
      )}

      {/* Lost Reason Dialog */}
      <LostReasonDialog
        isOpen={Boolean(lostDialog?.isOpen)}
        opportunity={lostDialog?.opp}
        targetStage={lostDialog?.targetStage}
        onConfirm={(reason) => {
          if (!lostDialog) return
          const { opp, targetStage } = lostDialog
          setLostDialog(null)
          executeMoveStage(opp, targetStage.id, reason)
        }}
        onCancel={() => {
          setLostDialog(null)
          setDraggedItem(null)
        }}
      />
    </div>
  )
}

export default OpportunityPipeline
