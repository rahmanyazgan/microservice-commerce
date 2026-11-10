import { api } from '../api'
import { Empty, ErrorBox, Spinner } from '../components/common'
import { formatDateTime } from '../format'
import { useAsync } from '../hooks/useAsync'

export function NotificationsPage() {
  const { data, error, loading, reload } = useAsync(() => api.notifications(), [], 5000)

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Müşteri Bildirimleri</h1>
          <p className="muted">
            Notification.Worker servisi RabbitMQ üzerinden <code>OrderConfirmed</code> ve <code>OrderCancelled</code>{' '}
            olaylarını Inbox (idempotent) deseniyle tüketir.
          </p>
        </div>
      </div>

      {loading && <Spinner label="Bildirimler yükleniyor..." />}
      {error !== undefined && <ErrorBox error={error} onRetry={reload} />}
      {!loading && !error && (data?.length ?? 0) === 0 && <Empty>Henüz bildirim bulunmuyor.</Empty>}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        {(data ?? []).map((n) => (
          <div key={n.id} className="card" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              <span className={`badge ${n.type === 'OrderConfirmed' ? 'badge-green' : 'badge-red'}`} style={{ marginRight: 8 }}>
                {n.type === 'OrderConfirmed' ? 'Onay' : 'İptal'}
              </span>
              <strong>{n.title}</strong>
              <p className="muted small" style={{ marginTop: 4 }}>
                {n.message}
              </p>
            </div>
            <span className="muted small">{formatDateTime(n.createdAt)}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

