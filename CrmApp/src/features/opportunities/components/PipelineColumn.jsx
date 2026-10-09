import React, { useState } from 'react'
import { OpportunityCard } from './OpportunityCard'
import Icon from '../../../shared/components/Icon'

export const PipelineColumn = ({
  column,
  stages = [],
  isReadOnly = false,
  onCardClick,
  onMoveStage,
  onEdit,
  onDelete,
  onAddOpportunity,
  onLoadMore,
  onDragStart,
  onDrop,
  loadingMore = false,
}) => {
  const [isDragOver, setIsDragOver] = useState(false)

  const handleDragOver = (e) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = isReadOnly ? 'none' : 'move'
    if (!isReadOnly && !isDragOver) {
      setIsDragOver(true)
    }
  }

  const handleDragLeave = (e) => {
    // Only clear if leaving the drop container itself
    if (!e.currentTarget.contains(e.relatedTarget)) {
      setIsDragOver(false)
    }
  }

  const handleDrop = (e) => {
    e.preventDefault()
    setIsDragOver(false)
    if (isReadOnly) return
    onDrop?.(e, column.stageId)
  }

  const items = column.items || []
  const total = column.total ?? items.length
  const canLoadMore = items.length < total

  return (
    <div
      className={`crm-opp-pipeline-column ${isDragOver ? 'crm-opp-pipeline-column--dragover' : ''}`}
      data-testid={`pipeline-column-${column.stageId}`}
      style={{ '--stage-color': column.stageColor || '#4f46e5' }}
    >
      {/* Column Header */}
      <div className="crm-opp-column-header">
        <div className="crm-opp-column-header-left">
          <span
            className="crm-opp-column-indicator"
            style={{ backgroundColor: column.stageColor || '#4f46e5' }}
          />
          <h3 className="crm-opp-column-title" title={column.stageName}>
            {column.stageName}
          </h3>
          <span
            className="crm-opp-column-badge"
            data-testid={`stage-count-${column.stageId}`}
          >
            {total}
          </span>
        </div>

        {!isReadOnly && (
          <button
            type="button"
            className="crm-opp-column-add-btn"
            onClick={() => onAddOpportunity?.(column.stageId)}
            title={`Thêm cơ hội vào ${column.stageName}`}
            data-testid={`btn-add-opp-stage-${column.stageId}`}
          >
            +
          </button>
        )}
      </div>

      {/* Cards Drop Zone */}
      <div
        className={`crm-opp-column-dropzone ${isDragOver ? 'crm-opp-column-dropzone--active' : ''}`}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        data-testid={`column-drop-zone-${column.stageId}`}
      >
        {items.length === 0 ? (
          <div className="crm-opp-column-empty" data-testid={`empty-column-${column.stageId}`}>
            <span className="crm-opp-column-empty-icon"><Icon name="inbox-empty" size={32} /></span>
            <p>Chưa có cơ hội</p>
          </div>
        ) : (
          items.map((opp) => (
            <OpportunityCard
              key={opp.id}
              opp={opp}
              stages={stages}
              isReadOnly={isReadOnly}
              onCardClick={onCardClick}
              onMoveStage={onMoveStage}
              onEdit={onEdit}
              onDelete={onDelete}
              onDragStart={onDragStart}
            />
          ))
        )}
      </div>

      {/* Load More Button */}
      {canLoadMore && (
        <div className="crm-opp-column-footer">
          <button
            type="button"
            className="crm-opp-column-loadmore-btn"
            disabled={loadingMore}
            onClick={() => onLoadMore?.(column.stageId)}
            data-testid={`btn-load-more-${column.stageId}`}
          >
            {loadingMore ? 'Đang tải...' : `Tải thêm (${items.length}/${total})`}
          </button>
        </div>
      )}
    </div>
  )
}

export default PipelineColumn
