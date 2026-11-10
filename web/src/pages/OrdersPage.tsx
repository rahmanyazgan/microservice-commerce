import { Link } from 'react-router-dom'
import { api } from '../api'
import { Empty, ErrorBox, Spinner, StatusBadge } from '../components/common'
import { formatDateTime, formatPrice, formatTime, shortId } from '../format'
import { useAsync } from '../hooks/useAsync'

export function OrdersPage() {
  const { data: orders, error, loading, reload } = useAsync(() => api.orders(), [], 3000)

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Siparişlerim (Saga Durum Takibi)</h1>
          <p className="muted">Her 3 saniyede bir güncellenir; tüm durum geçişleri ve telafi adımları kaydedilir</p>
        </div>
      </div>

      {loading && <Spinner label="Siparişler yükleniyor..." />}
      {error !== undefined && <ErrorBox error={error} onRetry={reload} />}

      {!loading && !error && (orders?.length ?? 0) === 0 && (
        <Empty>Henüz oluşturulan bir siparişiniz yok.</Empty>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        {(orders ?? []).map((o) => (
          <div key={o.id} className="card">
            <div className="order-head">
              <div>
                <strong>Sipariş #{shortId(o.id)}</strong>
                <span className="muted small" style={{ marginLeft: 10 }}>
                  {formatDateTime(o.createdAt)}
                </span>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <strong className="price">{formatPrice(o.totalAmount)}</strong>
                <StatusBadge status={o.status} label={o.statusText} />
              </div>
            </div>

            {o.cancelReason && (
              <div className="alert alert-error" style={{ marginTop: 10 }}>
                <strong>İptal Nedeni:</strong> {o.cancelReason}
              </div>
            )}

            <div className="grid-2" style={{ marginTop: 12 }}>
              <div>
                <h4 className="small muted">Sipariş Kalemleri</h4>
                <ul className="simple-list">
                  {o.items.map((item) => (
                    <li key={item.productId}>
                      <span>
                        {item.productName} × {item.quantity}
                      </span>
                      <span>{formatPrice(item.unitPrice * item.quantity)}</span>
                    </li>
                  ))}
                </ul>
              </div>

              <div>
                <h4 className="small muted">Saga Zaman Çizelgesi</h4>
                <ul className="timeline">
                  {o.history.map((h, idx) => (
                    <li key={idx}>
                      <span className="mono small muted">{formatTime(h.occurredAt)}</span> — {h.note}
                    </li>
                  ))}
                </ul>
              </div>
            </div>

            <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
              {o.status === 'StockReserved' && (
                <Link to={`/odeme/${o.id}`} className="btn btn-primary btn-sm">
                  Ödemeye Devam Et
                </Link>
              )}
              {o.paymentId && (
                <Link to={`/odeme-detay/${o.paymentId}`} className="btn btn-sm">
                  ISO 8583 Ödeme Detayı
                </Link>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

