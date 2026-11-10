import { useState } from 'react'
import { request } from '../api'
import { ErrorBox, Spinner } from '../components/common'
import { useAsync } from '../hooks/useAsync'

interface OpenApiDocument {
  info?: { title?: string; version?: string }
  paths?: Record<string, Record<string, { summary?: string; operationId?: string; responses?: Record<string, { description?: string }> }>>
}

const SERVICES = [
  { key: 'catalog', label: '1. Katalog Servisi (Catalog.Api :5101)' },
  { key: 'basket', label: '2. Sepet Servisi (Basket.Api :5102)' },
  { key: 'order', label: '3. Sipariş & Saga Servisi (Order.Api :5103)' },
  { key: 'payment', label: '4. Ödeme & ISO 8583 Servisi (Payment.Api :5104)' },
  { key: 'notification', label: '5. Bildirim Servisi (Notification.Worker :5105)' },
  { key: 'identity', label: '6. Kimlik & Google Auth Servisi (Identity.Api :5106)' },
]

export function ApiDocsPage() {
  const [selected, setSelected] = useState('catalog')
  const { data, error, loading, reload } = useAsync(
    () => request<OpenApiDocument>(`/api/openapi/${selected}`),
    [selected],
  )

  const paths = Object.entries(data?.paths ?? {})

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Birleşik OpenAPI / Swagger Dokümantasyonu</h1>
          <p className="muted">
            Tüm mikroservislerin <code>/openapi/v1.json</code> şemaları API Gateway üzerinden birleştirilir.
          </p>
        </div>
        <a
          href="http://localhost:5000/swagger"
          target="_blank"
          rel="noreferrer"
          className="btn btn-primary"
        >
          Tam Ekran Swagger UI Aç (localhost:5000/swagger) ↗
        </a>
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {SERVICES.map((s) => (
            <button
              key={s.key}
              type="button"
              className={`btn btn-sm ${selected === s.key ? 'btn-primary' : ''}`}
              onClick={() => setSelected(s.key)}
            >
              {s.label}
            </button>
          ))}
        </div>
      </div>

      {loading && <Spinner label="OpenAPI şeması yükleniyor..." />}
      {error !== undefined && <ErrorBox error={error} onRetry={reload} />}

      {!loading && !error && data && (
        <div className="card">
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 12 }}>
            <h3>{data.info?.title ?? selected}</h3>
            <span className="badge badge-blue">OpenAPI v{data.info?.version ?? '1.0'}</span>
          </div>

          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 110 }}>Metot</th>
                <th>Uç Nokta (Endpoint)</th>
                <th>Yanıt Kodları</th>
              </tr>
            </thead>
            <tbody>
              {paths.flatMap(([path, methods]) =>
                Object.entries(methods).map(([method, op]) => {
                  const upper = method.toUpperCase()
                  const badge =
                    upper === 'GET'
                      ? 'badge-blue'
                      : upper === 'POST'
                        ? 'badge-green'
                        : upper === 'PUT'
                          ? 'badge-amber'
                          : 'badge-red'
                  return (
                    <tr key={`${method}:${path}`}>
                      <td>
                        <span className={`badge ${badge}`}>{upper}</span>
                      </td>
                      <td className="mono">{path}</td>
                      <td className="small muted">
                        {Object.keys(op.responses ?? {}).join(', ') || '200'}
                      </td>
                    </tr>
                  )
                }),
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

