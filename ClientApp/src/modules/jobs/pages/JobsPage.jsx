import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import GenerationJobTable from '../components/GenerationJobTable'
import PublishLogTable from '../components/PublishLogTable'
import JobErrorPanel from '../components/JobErrorPanel'
import {
  JOB_STATUS_OPTIONS,
  JOB_TYPE_OPTIONS,
  PUBLISH_STATUS_OPTIONS,
} from '../constants/jobConstants'
import { useGenerationJobs } from '../hooks/useGenerationJobs'
import { usePublishLogs } from '../hooks/usePublishLogs'
import './JobsPage.css'

const TABS = {
  generation: 'generation',
  publish: 'publish',
}

export default function JobsPage() {
  const [activeTab, setActiveTab] = useState(TABS.generation)
  const [keyword, setKeyword] = useState('')
  const [status, setStatus] = useState('')
  const [jobType, setJobType] = useState('')
  const [errorJob, setErrorJob] = useState(null)
  const [actionError, setActionError] = useState('')

  const genParams = useMemo(
    () => ({
      keyword,
      index: 1,
      size: 50,
      status: status ? Number(status) : undefined,
      jobType: jobType ? Number(jobType) : undefined,
    }),
    [keyword, status, jobType],
  )

  const publishParams = useMemo(
    () => ({
      keyword,
      index: 1,
      size: 50,
      status: status !== '' ? Number(status) : undefined,
    }),
    [keyword, status],
  )

  const {
    data: genData,
    isLoading: genLoading,
    isError: genError,
    error: genErrorObj,
    refetch: refetchGen,
    isFetching: genFetching,
  } = useGenerationJobs(genParams, { refetchInterval: 10_000 })

  const {
    data: publishData,
    isLoading: publishLoading,
    isError: publishError,
    error: publishErrorObj,
    refetch: refetchPublish,
    isFetching: publishFetching,
  } = usePublishLogs(publishParams, { refetchInterval: 10_000 })

  const { data: channels = [] } = useSocialChannelAll()
  const channelMap = useMemo(
    () => Object.fromEntries(channels.map((c) => [c.id, c.pageName])),
    [channels],
  )

  const genItems = genData?.items ?? []
  const publishItems = publishData?.items ?? []

  const handleRefresh = () => {
    setActionError('')
    if (activeTab === TABS.generation) refetchGen()
    else refetchPublish()
  }

  const isFetching = activeTab === TABS.generation ? genFetching : publishFetching

  return (
    <section className="jobs-page">
      <PageHeader
        title="Jobs"
        description="Theo dõi và thao tác generation jobs / publish logs"
      />

      <div className="jobs-tabs">
        <button
          type="button"
          className={`jobs-tab${activeTab === TABS.generation ? ' jobs-tab--active' : ''}`}
          onClick={() => { setStatus(''); setJobType(''); setActiveTab(TABS.generation) }}
        >
          Generation Jobs
        </button>
        <button
          type="button"
          className={`jobs-tab${activeTab === TABS.publish ? ' jobs-tab--active' : ''}`}
          onClick={() => { setStatus(''); setJobType(''); setActiveTab(TABS.publish) }}
        >
          Publish Logs
        </button>
      </div>

      <div className="jobs-toolbar">
        <span className="jobs-toolbar-hint">
          Auto-refresh mỗi 10 giây
          {isFetching ? ' · Đang cập nhật...' : ''}
        </span>
        <button type="button" className="btn btn-secondary btn-sm" onClick={handleRefresh}>
          Làm mới
        </button>
      </div>

      {actionError && (
        <div className="alert alert-error" style={{ marginBottom: 16 }}>
          {actionError}
        </div>
      )}

      <div className="card card-body jobs-filters">
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="jobs-keyword">Tìm kiếm</label>
          <input
            id="jobs-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Keyword..."
          />
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="jobs-status">Status</label>
          <select
            id="jobs-status"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
          >
            <option value="">Tất cả</option>
            {(activeTab === TABS.generation ? JOB_STATUS_OPTIONS : PUBLISH_STATUS_OPTIONS).map(
              (option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ),
            )}
          </select>
        </div>
        {activeTab === TABS.generation && (
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label htmlFor="jobs-type">Job type</label>
            <select
              id="jobs-type"
              value={jobType}
              onChange={(event) => setJobType(event.target.value)}
            >
              <option value="">Tất cả</option>
              {JOB_TYPE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      <div className="card card-body">
        {activeTab === TABS.generation ? (
          <GenerationJobTable
            items={genItems}
            isLoading={genLoading}
            isError={genError}
            error={genErrorObj}
            onRetry={refetchGen}
            onViewError={setErrorJob}
            onActionError={(err) => setActionError(getErrorMessage(err))}
          />
        ) : (
          <PublishLogTable
            items={publishItems}
            channelMap={channelMap}
            isLoading={publishLoading}
            isError={publishError}
            error={publishErrorObj}
            onRetry={refetchPublish}
          />
        )}
      </div>

      <JobErrorPanel
        open={Boolean(errorJob)}
        title="Generation job error"
        errorCode={errorJob?.errorCode}
        errorMessage={errorJob?.errorMessage}
        onClose={() => setErrorJob(null)}
      />
    </section>
  )
}
