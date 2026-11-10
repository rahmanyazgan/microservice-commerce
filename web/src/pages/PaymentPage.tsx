import { useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError, errorMessage } from '../api'
import { ErrorBox, FieldError, Spinner, StatusBadge } from '../components/common'
import { useToast } from '../context/contexts'
import { detectBrand, digitsOnly, formatCardNumber, formatPrice, luhnValid, shortId } from '../format'
import { useAsync } from '../hooks/useAsync'
import type { PaymentResult, TestCard } from '../types'

export function PaymentPage() {
  const { orderId = '' } = useParams()
  const toast = useToast()

  const [cardHolder, setCardHolder] = useState('Demo Müşteri')
  const [cardNumber, setCardNumber] = useState('')
  const [expiryMonth, setExpiryMonth] = useState(12)
  const [expiryYear, setExpiryYear] = useState(2028)
  const [cvv, setCvv] = useState('123')
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<unknown>(undefined)
  const [result, setResult] = useState<PaymentResult | null>(null)
  const [otp, setOtp] = useState('123456')
  const [verifyingOtp, setVerifyingOtp] = useState(false)

  const idempotencyKeyRef = useRef<string>(crypto.randomUUID())

  const { data: testCards } = useAsync(() => api.testCards(), [])
  const {
    data: order,
    error: orderError,
    reload: reloadOrder,
  } = useAsync(
    () => api.order(orderId),
    [orderId],
    1000,
    !result || result.status === 'ThreeDsPending',
  )

  const brand = detectBrand(cardNumber)
  const cleanDigits = digitsOnly(cardNumber)
  const isLuhnOk = cleanDigits.length >= 13 && luhnValid(cleanDigits)

  function fillCard(tc: TestCard) {
    setCardNumber(formatCardNumber(tc.number))
    setFormError(undefined)
    idempotencyKeyRef.current = crypto.randomUUID()
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setFormError(undefined)

    try {
      let attempt = 0
      while (true) {
        try {
          const res = await api.pay(
            {
              orderId,
              cardHolder,
              cardNumber: cleanDigits,
              expiryMonth,
              expiryYear,
              cvv,
            },
            idempotencyKeyRef.current,
          )
          setResult(res)
          if (res.status === 'Approved') toast.success('Ödeme onaylandı!')
          else if (res.status === 'Declined' || res.status === 'Failed') toast.error(res.message)
          break
        } catch (err) {
          if (err instanceof ApiError && err.status === 409 && attempt < 4) {
            attempt++
            await new Promise((r) => setTimeout(r, 1000))
            continue
          }
          throw err
        }
      }
    } catch (err) {
      setFormError(err)
      toast.error(errorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleVerify3ds(e: React.FormEvent) {
    e.preventDefault()
    if (!result) return
    setVerifyingOtp(true)
    try {
      const updated = await api.verify3ds(result.paymentId, otp)
      setResult(updated)
      if (updated.status === 'Approved') {
        toast.success('3D Secure doğrulandı ve ödeme onaylandı!')
        void reloadOrder()
      } else if (updated.status !== 'ThreeDsPending') {
        toast.error(updated.message)
        void reloadOrder()
      }
    } catch (err) {
      toast.error(errorMessage(err))
    } finally {
      setVerifyingOtp(false)
    }
  }

  if (orderError !== undefined) return <ErrorBox error={orderError} onRetry={reloadOrder} />
  if (!order) return <Spinner label="Sipariş bilgileri alınıyor..." />

  if (order.status === 'Pending') {
    return (
      <div className="card" style={{ padding: 28, textAlign: 'center' }}>
        <Spinner label="Katalog servisinden stok ayrılıyor (RabbitMQ + Transactional Outbox)..." />
        <p className="muted small" style={{ marginTop: 8 }}>
          Sipariş #{shortId(order.id)} için stok kontrolü tamamlandığında ödeme formu açılacaktır.
        </p>
      </div>
    )
  }

  if (order.status === 'Cancelled' && !result) {
    return (
      <div className="card" style={{ padding: 24 }}>
        <h2>Sipariş İptal Edildi</h2>
        <p className="text-danger" style={{ margin: '12px 0' }}>
          {order.cancelReason ?? 'Sipariş iptal edildi.'}
        </p>
        <Link to="/siparisler" className="btn btn-primary">
          Siparişlerime Git
        </Link>
      </div>
    )
  }

  if (result && (result.status === 'Approved' || result.status === 'Declined' || result.status === 'Failed')) {
    const ok = result.status === 'Approved'
    return (
      <div className="card" style={{ padding: 24 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h2>{ok ? 'Ödeme Başarıyla Tamamlandı' : 'Ödeme Reddedildi / Başarısız'}</h2>
          <StatusBadge status={result.status} />
        </div>
        <p style={{ margin: '12px 0' }}>{result.message}</p>
        {result.responseCode && (
          <p className="muted small">
            ISO 8583 DE-39 Yanıt Kodu: <code>{result.responseCode}</code>
          </p>
        )}
        {!ok && (
          <div className="alert alert-error" style={{ marginTop: 12 }}>
            <strong>Saga Telafisi (Compensation) Başlatıldı:</strong> Ödeme başarısız olduğu için Order Saga orkestratörü{' '}
            <code>ReleaseStock</code> komutu yayınlayarak Katalog servisinde ayrılan stoğu otomatik olarak geri iade etti.
          </div>
        )}
        <div style={{ display: 'flex', gap: 10, marginTop: 16 }}>
          <Link to={`/odeme-detay/${result.paymentId}`} className="btn btn-primary">
            ISO 8583 Mesaj Dökümünü İncele
          </Link>
          <Link to="/siparisler" className="btn">
            Siparişlerime Git
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Kart ile Ödeme — Sipariş #{shortId(order.id)}</h1>
          <p className="muted">Luhn kontrolü, BIN marka tespiti, 3D Secure ve ISO 8583 otorizasyon simülasyonu</p>
        </div>
        <StatusBadge status={order.status} label={order.statusText} />
      </div>

      <div className="grid-2">
        <form className="card" onSubmit={(e) => void handleSubmit(e)}>
          <h3>Kart Bilgileri</h3>

          <div className="field">
            <label>Kart Üzerindeki İsim</label>
            <input
              className="input"
              value={cardHolder}
              onChange={(e) => setCardHolder(e.target.value)}
              placeholder="Ad Soyad"
              required
            />
            <FieldError error={formError} field="cardHolder" />
          </div>

          <div className="field">
            <div style={{ display: 'flex', justifyContent: 'space-between' }}>
              <label>Kart Numarası (PAN)</label>
              <span>
                {brand && <span className="badge badge-blue">{brand}</span>}{' '}
                {cleanDigits.length >= 13 && (
                  <span className={`badge ${isLuhnOk ? 'badge-green' : 'badge-red'}`}>
                    {isLuhnOk ? 'Luhn Geçerli' : 'Luhn Hatalı'}
                  </span>
                )}
              </span>
            </div>
            <input
              className="input mono"
              value={cardNumber}
              onChange={(e) => {
                setCardNumber(formatCardNumber(e.target.value))
                idempotencyKeyRef.current = crypto.randomUUID()
              }}
              placeholder="4543 6000 0000 0006"
              required
            />
            <FieldError error={formError} field="cardNumber" />
          </div>

          <div className="row-3">
            <div className="field">
              <label>Son Kullanma Ay</label>
              <select className="input" value={expiryMonth} onChange={(e) => setExpiryMonth(Number(e.target.value))}>
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <option key={m} value={m}>
                    {String(m).padStart(2, '0')}
                  </option>
                ))}
              </select>
              <FieldError error={formError} field="expiryMonth" />
            </div>

            <div className="field">
              <label>Yıl</label>
              <select className="input" value={expiryYear} onChange={(e) => setExpiryYear(Number(e.target.value))}>
                {Array.from({ length: 10 }, (_, i) => 2026 + i).map((y) => (
                  <option key={y} value={y}>
                    {y}
                  </option>
                ))}
              </select>
              <FieldError error={formError} field="expiryYear" />
            </div>

            <div className="field">
              <label>CVV</label>
              <input
                className="input mono"
                maxLength={4}
                value={cvv}
                onChange={(e) => setCvv(digitsOnly(e.target.value))}
                required
              />
              <FieldError error={formError} field="cvv" />
            </div>
          </div>

          <div className="muted small" style={{ marginBottom: 12 }}>
            Idempotency-Key: <code>{idempotencyKeyRef.current}</code> (çift çekimi önler)
          </div>

          <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={submitting}>
            {submitting ? 'İşleniyor...' : `${formatPrice(order.totalAmount)} Öde`}
          </button>
        </form>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div className="card">
            <h3>Hazır Test Kartları (Tıklayıp Doldurun)</h3>
            <p className="muted small" style={{ marginBottom: 10 }}>
              Farklı banka yanıt kodlarını (DE-39) ve Saga telafi akışını denemek için bir karta tıklayın:
            </p>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {(testCards ?? []).map((tc) => (
                <button
                  key={tc.number}
                  type="button"
                  className="test-card-btn"
                  onClick={() => fillCard(tc)}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <strong className="mono">{formatCardNumber(tc.number)}</strong>
                    <span className="badge badge-gray">
                      {tc.brand} · {tc.threeDs ? '3DS' : 'Non-3DS'}
                    </span>
                  </div>
                  <div className="small muted">
                    {tc.description} → <strong>{tc.expectedResult}</strong>
                  </div>
                </button>
              ))}
            </div>
          </div>

          <div className="card">
            <h3>Sipariş Özeti</h3>
            <ul className="simple-list">
              {order.items.map((i) => (
                <li key={i.productId}>
                  <span>
                    {i.productName} × {i.quantity}
                  </span>
                  <strong>{formatPrice(i.unitPrice * i.quantity)}</strong>
                </li>
              ))}
            </ul>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--border)' }}>
              <span>Toplam Tutar</span>
              <strong className="price">{formatPrice(order.totalAmount)}</strong>
            </div>
          </div>
        </div>
      </div>

      {result?.status === 'ThreeDsPending' && result.threeDs && (
        <div className="modal-backdrop">
          <div className="modal card">
            <div className="modal-header">
              <span className="badge badge-blue">3D Secure 2.2</span>
              <strong>Mini Banka A.Ş. — Güvenli Ödeme Doğrulaması</strong>
            </div>
            <p className="small muted" style={{ margin: '10px 0' }}>
              İşyeri: <strong>{result.threeDs.merchantName}</strong>
              <br />
              Kart: <strong className="mono">{result.threeDs.maskedPan}</strong>
              <br />
              Tutar: <strong>{formatPrice(result.threeDs.amount)}</strong>
            </p>
            {result.message && <div className="alert">{result.message}</div>}
            <form onSubmit={(e) => void handleVerify3ds(e)}>
              <div className="field">
                <label>SMS Doğrulama Kodu (Demo kod: 123456)</label>
                <input
                  className="input mono"
                  value={otp}
                  onChange={(e) => setOtp(e.target.value)}
                  maxLength={6}
                  autoFocus
                  required
                />
                <div className="small muted">Kalan deneme hakkı: {result.threeDs.remainingAttempts}</div>
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                <button type="submit" className="btn btn-primary" disabled={verifyingOtp}>
                  {verifyingOtp ? 'Doğrulanıyor...' : 'Onayla ve Ödemeyi Tamamla'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}

