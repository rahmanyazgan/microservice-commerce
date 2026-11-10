import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { ErrorBox, Spinner, StatusBadge } from '../components/common'
import { formatDateTime, formatPrice, shortId } from '../format'
import { useAsync } from '../hooks/useAsync'
import type { IsoView } from '../types'

function IsoBlock({ title, view }: { title: string; view: IsoView | null }) {
  if (!view) return null
  return (
    <div className="card" style={{ marginTop: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h3>
          {title} — <code>MTI {view.mti}</code>
        </h3>
        <span className="badge badge-blue">{view.mtiText}</span>
      </div>
      <p className="small muted" style={{ margin: '6px 0 12px' }}>
        Primary Bitmap (Hex): <code>{view.bitmap}</code>
      </p>

      <table className="table">
        <thead>
          <tr>
            <th style={{ width: 90 }}>Alan</th>
            <th>Açıklama</th>
            <th>Değer</th>
          </tr>
        </thead>
        <tbody>
          {view.fields.map((f) => (
            <tr key={f.number}>
              <td>
                <code>DE-{f.number}</code>
              </td>
              <td>{f.name}</td>
              <td className="mono">{f.value}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div style={{ marginTop: 12 }}>
        <div className="small muted">Ham ASCII Mesaj Çerçevesi:</div>
        <pre className="code-block">{view.raw}</pre>
        <div className="small muted" style={{ marginTop: 8 }}>
          Hex Dökümü:
        </div>
        <pre className="code-block">{view.hex}</pre>
      </div>
    </div>
  )
}

export function PaymentDetailPage() {
  const { paymentId = '' } = useParams()
  const { data: p, error, loading, reload } = useAsync(() => api.payment(paymentId), [paymentId])

  if (loading) return <Spinner label="Ödeme ve ISO 8583 detayları yükleniyor..." />
  if (error !== undefined || !p) return <ErrorBox error={error} onRetry={reload} />

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Ödeme ve ISO 8583 Teknik Dökümü</h1>
          <p className="muted">
            ISO 8583, POS/sanal POS ile kart çıkaran banka (Issuer) arasındaki finansal mesajlaşma standardıdır.
          </p>
        </div>
        <Link to="/siparisler" className="btn">
          ← Siparişlere Dön
        </Link>
      </div>

      <div className="card">
        <div className="kv-grid">
          <div>
            <span className="muted small">Ödeme No</span>
            <div className="mono">{shortId(p.id)}</div>
          </div>
          <div>
            <span className="muted small">Sipariş No</span>
            <div className="mono">{shortId(p.orderId)}</div>
          </div>
          <div>
            <span className="muted small">Tutar</span>
            <strong>{formatPrice(p.amount)}</strong>
          </div>
          <div>
            <span className="muted small">Durum</span>
            <div>
              <StatusBadge status={p.status} />
            </div>
          </div>
          <div>
            <span className="muted small">Maskeli PAN (PCI DSS)</span>
            <div className="mono">{p.maskedPan || '—'}</div>
          </div>
          <div>
            <span className="muted small">Kart Şeması</span>
            <div>{p.brand || '—'}</div>
          </div>
          <div>
            <span className="muted small">DE-39 Yanıt Kodu</span>
            <div>
              <code>{p.responseCode ?? '—'}</code> ({p.responseText ?? '—'})
            </div>
          </div>
          <div>
            <span className="muted small">DE-38 Onay Kodu</span>
            <div className="mono">{p.authCode ?? '—'}</div>
          </div>
          <div>
            <span className="muted small">DE-11 STAN / DE-37 RRN</span>
            <div className="mono">
              {p.stan ?? '—'} / {p.rrn ?? '—'}
            </div>
          </div>
          <div>
            <span className="muted small">3D Secure ECI</span>
            <div className="mono">{p.eci ?? 'Non-3DS'}</div>
          </div>
          <div>
            <span className="muted small">Tarih</span>
            <div>{formatDateTime(p.createdAt)}</div>
          </div>
        </div>
      </div>

      <IsoBlock title="Otorizasyon İsteği (Acquirer → Issuer)" view={p.isoRequest} />
      <IsoBlock title="Otorizasyon Yanıtı (Issuer → Acquirer)" view={p.isoResponse} />
      <IsoBlock title="Ters İşlem / İptal Mesajı (Reversal)" view={p.reversalRequest} />
    </div>
  )
}

