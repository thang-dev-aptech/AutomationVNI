import PageHeader from '@/shared/components/PageHeader'

export default function ComingSoonPage({ title, description }) {
  return (
    <section>
      <PageHeader title={title} description={description} />
      <div className="card card-body">
        <p style={{ margin: 0, color: 'var(--color-text-muted)' }}>
          Màn hình này sẽ được triển khai ở bước tiếp theo.
        </p>
      </div>
    </section>
  )
}
