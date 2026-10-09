import React, { useState, useEffect, useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'
import { opportunityApi } from './api/opportunityApi'
import { OpportunityStatsBar } from './components/OpportunityStatsBar'
import { OpportunityTable } from './components/OpportunityTable'
import { OpportunityPipeline } from './components/OpportunityPipeline'
import { OpportunityFilters } from './components/OpportunityFilters'
import {
  ColumnSettings,
  getStoredColumns,
  setStoredColumns,
} from './components/ColumnSettings'
import { OpportunityDrawer } from './components/OpportunityDrawer'
import { OpportunityFormModal } from './components/OpportunityFormModal'
import './OpportunitiesFeature.css'

export const OpportunitiesFeature = () => {
  const [searchParams, setSearchParams] = useSearchParams()
  const { canManage, canCare, isReadOnly, user } = useAuth()

  // URL state parameters
  const view = searchParams.get('view') || 'table'
  const activeTab = searchParams.get('tab') || 'all'
  const stage = searchParams.get('stage') || ''
  const assignee = searchParams.get('assignee') || 'mine'
  const keyword = searchParams.get('keyword') || ''
  const pageIndex = parseInt(searchParams.get('index') || '1', 10) || 1
  const source = searchParams.get('source') ? Number(searchParams.get('source')) : null

  // Local state
  const [stages, setStages] = useState([])
  const [users, setUsers] = useState([])
  const [stats, setStats] = useState(null)
  const [statsLoading, setStatsLoading] = useState(false)

  const [items, setItems] = useState([])
  const [total, setTotal] = useState(0)
  const [tableLoading, setTableLoading] = useState(false)
  const [tableError, setTableError] = useState(null)

  const [columns, setColumns] = useState(() => getStoredColumns())
  const [showFilters, setShowFilters] = useState(false)

  // Drawer & Form modal state
  const [selectedOpportunityId, setSelectedOpportunityId] = useState(null)
  const [isDrawerOpen, setIsDrawerOpen] = useState(false)
  const [isFormModalOpen, setIsFormModalOpen] = useState(false)
  const [formInitialData, setFormInitialData] = useState(null)

  // Load stages and users once on mount
  useEffect(() => {
    let mounted = true
    const initLookups = async () => {
      try {
        const [stageList, userList] = await Promise.all([
          opportunityApi.listStages(),
          opportunityApi.listUsers(),
        ])
        if (mounted) {
          setStages(stageList || [])
          setUsers(userList || [])
        }
      } catch {
        // Lookups error handled gracefully
      }
    }
    initLookups()
    return () => {
      mounted = false
    }
  }, [])

  // Helper to update search params
  const updateUrlParams = useCallback((paramsToUpdate) => {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        Object.entries(paramsToUpdate).forEach(([key, val]) => {
          if (val === null || val === undefined || val === '') {
            next.delete(key)
          } else {
            next.set(key, String(val))
          }
        })
        return next
      },
      { replace: true }
    )
  }, [setSearchParams])

  // View switch handler
  const handleViewChange = (newView) => {
    updateUrlParams({ view: newView })
  }

  // Tab switch handler
  const handleTabChange = (newTab) => {
    updateUrlParams({
      tab: newTab === 'all' ? null : newTab,
      index: null,
    })
  }

  // Filter change handler
  const handleFilterChange = (filterUpdates) => {
    const params = { ...filterUpdates }
    if (params.stage !== undefined) {
      params.stage = filterUpdates.stage
      delete params.stageId
    }
    if (params.index === 1) {
      params.index = null // omit index=1 to keep URL clean
    }
    updateUrlParams(params)
  }

  // Filter reset handler
  const handleFilterReset = () => {
    updateUrlParams({
      stage: null,
      keyword: null,
      source: null,
      assignee: 'mine',
      index: null,
    })
  }

  // Page change handler
  const handlePageChange = (newIndex) => {
    updateUrlParams({
      index: newIndex <= 1 ? null : newIndex,
    })
  }

  // Column settings change handler
  const handleColumnChange = (newColumns) => {
    setColumns(newColumns)
    setStoredColumns(newColumns)
  }

  // Load stats
  const fetchStats = useCallback(async () => {
    setStatsLoading(true)
    try {
      const data = await opportunityApi.stats({
        assigneeFilter: assignee || 'mine',
        stageId: stage || null,
        keyword: keyword || null,
        source: source || null,
      })
      setStats(data)
    } catch {
      // stats failure shouldn't block table
    } finally {
      setStatsLoading(false)
    }
  }, [assignee, stage, keyword, source])

  // Load opportunities table items
  const fetchOpportunities = useCallback(async () => {
    if (activeTab === 'activities') {
      return // activities tab uses placeholder / tasks board
    }

    setTableLoading(true)
    setTableError(null)

    let status = null
    let isArchived = false

    if (activeTab === 'open') {
      status = 1
      isArchived = false
    } else if (activeTab === 'archived') {
      status = null
      isArchived = true
    }

    try {
      const res = await opportunityApi.filter({
        index: pageIndex,
        size: 20,
        stageId: stage || null,
        status,
        isArchived,
        assigneeFilter: assignee || 'mine',
        keyword: keyword || null,
        source: source || null,
      })

      setItems(res?.items || [])
      setTotal(res?.total || 0)
    } catch (err) {
      setTableError(err?.response?.data?.message || err?.message || 'Không thể tải danh sách cơ hội')
    } finally {
      setTableLoading(false)
    }
  }, [activeTab, pageIndex, stage, assignee, keyword, source])

  // Reload when filters/tab change
  useEffect(() => {
    fetchStats()
  }, [fetchStats])

  useEffect(() => {
    fetchOpportunities()
  }, [fetchOpportunities])

  // Row actions
  const handleRowClick = (oppId) => {
    setSelectedOpportunityId(oppId)
    setIsDrawerOpen(true)
  }

  const handleOpenCreate = (prefilledStageId) => {
    setFormInitialData(prefilledStageId ? { stageId: prefilledStageId } : null)
    setIsFormModalOpen(true)
  }

  const handleEdit = (opp) => {
    setFormInitialData(opp)
    setIsFormModalOpen(true)
  }

  const handleMoveStage = async (oppId, stageIdOrData, lostReason) => {
    if (typeof stageIdOrData === 'object' && stageIdOrData !== null) {
      await opportunityApi.moveStage(oppId, stageIdOrData.stageId, stageIdOrData.lostReason)
    } else {
      await opportunityApi.moveStage(oppId, stageIdOrData, lostReason)
    }
    fetchOpportunities()
    fetchStats()
  }

  const handleArchive = async (oppId) => {
    try {
      await opportunityApi.archive(oppId)
      fetchOpportunities()
      fetchStats()
    } catch (err) {
      alert('Lỗi lưu trữ cơ hội: ' + (err?.response?.data?.message || err?.message))
    }
  }

  const handleUnarchive = async (oppId) => {
    try {
      await opportunityApi.unarchive(oppId)
      fetchOpportunities()
      fetchStats()
    } catch (err) {
      alert('Lỗi bỏ lưu trữ cơ hội: ' + (err?.response?.data?.message || err?.message))
    }
  }

  const handleDelete = async (oppId) => {
    try {
      await opportunityApi.delete(oppId)
      fetchOpportunities()
      fetchStats()
    } catch (err) {
      alert('Lỗi xoá cơ hội: ' + (err?.response?.data?.message || err?.message))
    }
  }

  const handleFormSuccess = () => {
    setIsFormModalOpen(false)
    setFormInitialData(null)
    fetchOpportunities()
    fetchStats()
  }

  // Count active non-default filters
  const activeFilterCount = useMemo(() => {
    let count = 0
    if (stage) count++
    if (keyword) count++
    if (source) count++
    if (assignee && assignee !== 'mine') count++
    return count
  }, [stage, keyword, source, assignee])

  return (
    <div className="crm-opp-feature" data-testid="opportunities-feature">
      {/* 1. Header: Tiêu đề "Cơ hội", nút chuyển Bảng | Pipeline */}
      <header className="crm-opp-header" data-testid="opportunity-header">
        <div className="crm-opp-header-left">
          <h1 className="crm-opp-title">Cơ hội</h1>
          <div className="crm-opp-view-switcher" role="radiogroup" aria-label="Chế độ xem">
            <button
              type="button"
              className={`crm-opp-view-btn ${view !== 'pipeline' ? 'crm-opp-view-btn--active' : ''}`}
              onClick={() => handleViewChange('table')}
              data-testid="btn-view-table"
              aria-pressed={view !== 'pipeline'}
            >
              📑 Bảng
            </button>
            <button
              type="button"
              className={`crm-opp-view-btn ${view === 'pipeline' ? 'crm-opp-view-btn--active' : ''}`}
              onClick={() => handleViewChange('pipeline')}
              data-testid="btn-view-pipeline"
              aria-pressed={view === 'pipeline'}
            >
              📊 Pipeline
            </button>
          </div>
        </div>
      </header>

      {/* 2. Thanh thống kê: Tổng, Đang mở, Thắng, Thua, Hoạt động, Doanh thu */}
      <OpportunityStatsBar stats={stats} loading={statsLoading} />

      {/* 3. Main Content: Pipeline vs Bảng */}
      {view === 'pipeline' ? (
        <OpportunityPipeline
          filters={{
            keyword,
            stageId: stage,
            stage,
            assignee,
            source,
          }}
          stages={stages}
          users={users}
          isReadOnly={isReadOnly}
          onOpenCreate={handleOpenCreate}
          onCardClick={handleRowClick}
          onEdit={handleEdit}
          onDelete={handleDelete}
          onRefresh={() => {
            fetchStats()
          }}
          showFilters={showFilters}
          onToggleFilters={() => setShowFilters((prev) => !prev)}
          activeFilterCount={activeFilterCount}
          childrenFilters={
            <OpportunityFilters
              filters={{
                keyword,
                stageId: stage,
                stage,
                assignee,
                source,
              }}
              onChange={handleFilterChange}
              stages={stages}
              users={users}
              canViewAll={canManage}
              isOpen={showFilters}
              onReset={handleFilterReset}
            />
          }
        />
      ) : (
        <OpportunityTable
          items={items}
          total={total}
          pageIndex={pageIndex}
          pageSize={20}
          loading={tableLoading}
          error={tableError}
          activeTab={activeTab}
          onTabChange={handleTabChange}
          columns={columns}
          stats={stats}
          stages={stages}
          isReadOnly={isReadOnly}
          onOpenCreate={handleOpenCreate}
          onRefresh={() => {
            fetchOpportunities()
            fetchStats()
          }}
          onRowClick={handleRowClick}
          onEdit={handleEdit}
          onMoveStage={handleMoveStage}
          onArchive={handleArchive}
          onUnarchive={handleUnarchive}
          onDelete={handleDelete}
          onPageChange={handlePageChange}
          showFilters={showFilters}
          onToggleFilters={() => setShowFilters((prev) => !prev)}
          activeFilterCount={activeFilterCount}
          childrenHeaderRight={
            <ColumnSettings columns={columns} onChange={handleColumnChange} />
          }
          childrenFilters={
            <OpportunityFilters
              filters={{
                keyword,
                stageId: stage,
                stage,
                assignee,
                source,
              }}
              onChange={handleFilterChange}
              stages={stages}
              users={users}
              canViewAll={canManage}
              isOpen={showFilters}
              onReset={handleFilterReset}
            />
          }
        />
      )}

      {/* 4. Drawer chi tiết cơ hội */}
      <OpportunityDrawer
        opportunityId={selectedOpportunityId}
        isOpen={isDrawerOpen}
        onClose={() => {
          setIsDrawerOpen(false)
          setSelectedOpportunityId(null)
        }}
        onEdit={(item) => {
          setIsDrawerOpen(false)
          handleEdit(item)
        }}
        onOpportunityUpdated={() => {
          fetchOpportunities()
          fetchStats()
        }}
        isReadOnly={isReadOnly}
        stages={stages}
        users={users}
      />

      {/* 5. Modal tạo / sửa cơ hội */}
      {!isReadOnly && isFormModalOpen && (
        <OpportunityFormModal
          isOpen={isFormModalOpen}
          onClose={() => {
            setIsFormModalOpen(false)
            setFormInitialData(null)
          }}
          onSuccess={handleFormSuccess}
          initialData={formInitialData}
          stages={stages}
          users={users}
        />
      )}
    </div>
  )
}

export default OpportunitiesFeature
