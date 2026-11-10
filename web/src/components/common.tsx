import type { ReactNode } from 'react'
import { ApiError, errorMessage } from '../api'

export function Spinner({ label }: { label?: string }) {
  return (
    <div className="spinner-wrap">
      <span className="spinner" aria-hidden="true" />
      {label && <span>{label}</span>}
    </div>
  )
}

export function ErrorBox({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const offline = error instanceof ApiError && error.status === 0
  return (
    <div className="alert alert-error">
      <strong>{offline ? 'Servislere ulaşılamıyor' : 'Bir hata oluştu'}</strong>
      <p>
        {offline
          ? 'Arka uç servisleri şu anda çalışmıyor olabilir. Lütfen API Gateway\'in (localhost:5000) ayakta olduğundan emin olun.'
          : errorMessage(error)}
      </p>
      {onRetry && (
        <button className="btn btn-sm" onClick={onRetry}>
          Tekrar dene
        </button>
      )}
    </div>
  )
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>
}

const statusClass: Record<string, string> = {
  Pending: 'badge-gray',
  StockReserved: 'badge-amber',
  Confirmed: 'badge-green',
  Cancelled: 'badge-red',
  Approved: 'badge-green',
  Declined: 'badge-red',
  Failed: 'badge-red',
  Reversed: 'badge-amber',
  ThreeDsPending: 'badge-amber',
  AwaitingCard: 'badge-gray',
}

const paymentStatusText: Record<string, string> = {
  ThreeDsPending: '3D Secure bekleniyor',
  Approved: 'Onaylandı',
  Declined: 'Reddedildi',
  Failed: 'Başarısız',
  Cancelled: 'İptal edildi',
  Reversed: 'İade/İptal (Reversal)',
  AwaitingCard: 'Kart bekleniyor',
}

export function StatusBadge({ status, label }: { status: string; label?: string }) {
  return (
    <span className={`badge ${statusClass[status] ?? 'badge-gray'}`}>
      {label ?? paymentStatusText[status] ?? status}
    </span>
  )
}

export function FieldError({ error, field }: { error: unknown; field: string }) {
  if (!(error instanceof ApiError)) return null
  const msg = error.fieldError(field)
  return msg ? <div className="field-error">{msg}</div> : null
}
